import { createHash } from 'node:crypto';
import { readFile, realpath } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';

const identifier = /^[a-z][a-z0-9_]{0,63}$/;
const isId = (value) => typeof value === 'string' && identifier.test(value);
const anchors = ['topLeft', 'topRight', 'center', 'bottomCenter', 'stretch'];
const types = ['group', 'image', 'text', 'button'];
const pngSignature = Buffer.from('89504e470d0a1a0a', 'hex');
export const digest = (data) => createHash('sha256').update(data).digest('hex');

export async function readPacked(source) {
  const file = await realpath(source);
  const bundle = JSON.parse(await readFile(file, 'utf8'));
  const { revision, ...payload } = bundle;
  if (bundle.format !== 'ui-workflow' || bundle.version !== 1 || !isId(bundle.id)
    || revision !== digest(JSON.stringify(payload)).slice(0, 16)
    || !Array.isArray(bundle.assets) || !Array.isArray(bundle.nodes)) {
    throw new Error('Invalid or modified UI Workflow bundle manifest');
  }
  const files = new Map();
  for (const asset of bundle.assets) {
    if (!isId(asset.id) || asset.file !== `images/${asset.id}.png` || files.has(asset.file)) {
      throw new Error('Invalid or duplicate bundle image path');
    }
    const png = await readFile(resolve(dirname(file), asset.file));
    if (digest(png) !== asset.sha256) throw new Error(`Bundle PNG differs from its manifest: ${asset.id}`);
    files.set(asset.file, png);
  }
  return { bundle, files };
}

export function compareBundles(before, after) {
  if (before.id !== after.id) throw new Error('Choose two revisions of the same screen');
  const compare = (left, right) => {
    const oldItems = new Map(left.map((item) => [item.id, item]));
    const newItems = new Map(right.map((item) => [item.id, item]));
    return {
      added: right.filter((item) => !oldItems.has(item.id)).map((item) => item.id),
      removed: left.filter((item) => !newItems.has(item.id)).map((item) => item.id),
      changed: right.filter((item) => oldItems.has(item.id)
        && JSON.stringify(oldItems.get(item.id)) !== JSON.stringify(item)).map((item) => item.id),
    };
  };
  function withSiblingOrder(nodes) {
    const counts = new Map();
    return nodes.map((node) => {
      const sibling = counts.get(node.parent) ?? 0;
      counts.set(node.parent, sibling + 1);
      return { ...node, sibling };
    });
  }
  return { id: before.id, from: before.revision, to: after.revision,
    referenceSizeChanged: before.width !== after.width || before.height !== after.height,
    nodes: compare(withSiblingOrder(before.nodes), withSiblingOrder(after.nodes)),
    assets: compare(before.assets, after.assets) };
}

export async function readBundle(source) {
  const file = await realpath(source);
  const art = JSON.parse(await readFile(file, 'utf8'));
  if (!art || typeof art !== 'object' || Array.isArray(art)) {
    throw new Error('Expected a screen document object');
  }

  const issues = [];
  const require = (condition, where, message) => {
    if (!condition) issues.push({ where, message });
    return Boolean(condition);
  };
  require(art.version === 1, 'version', 'Expected workflow schema version 1');
  require(isId(art.id), 'id', 'Use a stable lowercase identifier');
  const positiveInt = (value) => Number.isInteger(value) && value > 0 && value <= 16384;
  require(positiveInt(art.width) && positiveInt(art.height), 'screen', 'Specify a valid reference width and height');
  require(Array.isArray(art.assets), 'assets', 'Expected an array');
  require(Array.isArray(art.nodes) && art.nodes.length, 'nodes', 'Expected at least one node');

  const files = new Map();
  const assets = new Map();
  const directory = dirname(file);
  for (const [index, asset] of (Array.isArray(art.assets) ? art.assets : []).entries()) {
    const where = `assets[${index}]`;
    if (!require(asset && isId(asset.id), where, 'Invalid asset ID')) continue;
    if (!require(!assets.has(asset.id), where, 'Duplicate asset ID')) continue;
    const entry = { id: asset.id, file: `images/${asset.id}.png` };
    assets.set(asset.id, entry);
    if (!require(typeof asset.file === 'string' && !isAbsolute(asset.file) && asset.file.endsWith('.png'),
      where, 'Use a relative PNG path')) continue;

    try {
      const image = await realpath(resolve(directory, asset.file));
      const within = relative(directory, image);
      if (!within || within === '..' || within.startsWith(`..${sep}`) || isAbsolute(within)) {
        throw new Error('PNG must be inside the source folder');
      }
      const png = await readFile(image);
      if (png.length < 45 || !png.subarray(0, 8).equals(pngSignature)
        || png.readUInt32BE(8) !== 13 || png.toString('ascii', 12, 16) !== 'IHDR'
        || png.toString('ascii', png.length - 8, png.length - 4) !== 'IEND') {
        throw new Error('Invalid PNG header or missing IEND');
      }
      entry.width = png.readUInt32BE(16);
      entry.height = png.readUInt32BE(20);
      if (!positiveInt(entry.width) || !positiveInt(entry.height)) throw new Error('Invalid PNG dimensions');
      if (asset.border !== undefined) {
        const border = asset.border;
        if (!['left', 'top', 'right', 'bottom'].every((key) => Number.isInteger(border?.[key]) && border[key] >= 0)
          || border.left + border.right >= entry.width || border.top + border.bottom >= entry.height) {
          throw new Error('Nine-slice borders must fit inside the source PNG');
        }
        entry.border = { left: border.left, top: border.top, right: border.right, bottom: border.bottom };
      }
      entry.sha256 = digest(png);
      files.set(entry.file, png);
    } catch (error) {
      issues.push({ where, message: error.message });
    }
  }

  const seen = new Set();
  const actions = [];
  const compile = (nodes, parent, bounds) => nodes.flatMap((node, index) => {
    const where = `${parent}.children[${index}]`;
    if (!require(node && isId(node.id), where, 'Invalid node ID')) return [];
    const location = node.id;
    if (!require(!seen.has(node.id), location, 'Duplicate node ID')) return [];
    seen.add(node.id);
    require(types.includes(node.type), location, 'Expected group, image, text or button');
    const anchor = node.anchor ?? 'topLeft';
    require(anchors.includes(anchor), location, 'Unknown anchor');
    const { x, y, width, height } = node.rect ?? {};
    require([x, y].every(Number.isFinite) && positiveInt(width) && positiveInt(height), location, 'Invalid rect');
    const allowed = new Set(['id', 'type', 'rect', 'anchor', 'children']);
    const compiled = { id: node.id, parent, type: node.type, anchor, rect: { x, y, width, height } };
    const setImage = (field) => {
      allowed.add(field);
      if (require(assets.has(node[field]), location, `Unknown ${field} PNG asset`)) {
        const border = assets.get(node[field]).border;
        require(!border || (width >= border.left + border.right && height >= border.top + border.bottom),
          location, 'Rect is smaller than its nine-slice borders');
      }
      compiled[field] = node[field];
    };
    if (node.type === 'image' || node.type === 'button') setImage('asset');
    if (node.type === 'text') {
      for (const field of ['text', 'fontSize', 'color', 'align']) allowed.add(field);
      require(typeof node.text === 'string' && !/[\r\n]/.test(node.text), location, 'Supply one line of plain text');
      require(positiveInt(node.fontSize) && node.fontSize <= 512, location, 'Invalid font size');
      require(/^#[0-9a-f]{6}$/i.test(node.color ?? ''), location, 'Use #RRGGBB color');
      require(['left', 'center', 'right'].includes(node.align), location, 'Invalid text alignment');
      Object.assign(compiled, { text: node.text, fontSize: node.fontSize, color: node.color, align: node.align });
    }
    if (node.type === 'button') {
      allowed.add('action');
      require(isId(node.action), location, 'Supply a stable action ID');
      compiled.action = node.action;
      for (const field of ['pressed', 'disabled']) {
        allowed.add(field);
        if (node[field] !== undefined) setImage(field);
      }
      for (const field of ['pressed', 'disabled']) {
        const state = assets.get(node[field]);
        const normal = assets.get(node.asset);
        if (state && normal) {
          require(state.width === normal.width && state.height === normal.height
            && JSON.stringify(state.border) === JSON.stringify(normal.border),
          location, `${field} must have the normal sprite dimensions and borders`);
        }
      }
      actions.push({ nodeId: node.id, action: node.action });
    }
    require(Object.keys(node).every((key) => allowed.has(key)), location, 'Unexpected fields for this node type');
    if (anchor === 'stretch') {
      require(x >= 0 && y >= 0 && x + width <= bounds.width && y + height <= bounds.height,
        location, 'Stretch offsets must fit inside the reference parent');
    }
    const children = node.children ?? [];
    return require(Array.isArray(children), location, 'Expected a children array')
      ? [compiled, ...compile(children, node.id, compiled.rect)] : [compiled];
  });
  const nodes = Array.isArray(art.nodes) ? compile(art.nodes, '$root', art) : [];
  if (issues.length) {
    const error = new Error(issues.map(({ where, message }) => `${where}: ${message}`).join('\n'));
    error.issues = issues;
    throw error;
  }
  const bundle = { format: 'ui-workflow', version: 1, id: art.id, width: art.width, height: art.height,
    assets: [...assets.values()], nodes, actions };
  return { bundle: { ...bundle, revision: digest(JSON.stringify(bundle)).slice(0, 16) }, files };
}
