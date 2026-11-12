import { execFile } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, realpath, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, extname, join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { readBundle } from './bundle.mjs';

const exec = promisify(execFile);
const identifier = /^[a-z][a-z0-9_]{0,63}$/;
const cli = () => process.env.PHOTOCRAFT_CLI || 'photocraft-cli';
const options = { windowsHide: true, timeout: 120_000, maxBuffer: 16 * 1024 * 1024 };
const command = (name, params) => ['--cmd', name, '--params', JSON.stringify(params)];
const call = (method, params) => ({ method, params });
const edit = (command, params) => ({ command, params });

function inside(child, parent) {
  return ['x', 'y', 'width', 'height'].every((key) => Number.isSafeInteger(child?.[key]))
    && child.x >= 0 && child.y >= 0 && child.width > 0 && child.height > 0
    && child.x + child.width <= parent.width && child.y + child.height <= parent.height;
}

function touches(a, b) {
  return a.x < b.x + b.width && b.x < a.x + a.width
    && a.y < b.y + b.height && b.y < a.y + a.height;
}

function compilePlan(plan) {
  if (plan?.version !== 1 || typeof plan.source !== 'string' || typeof plan.id !== 'string'
    || !identifier.test(plan.id) || !Array.isArray(plan.nodes) || !plan.nodes.length
    || !inside({ x: 0, y: 0, width: plan.width, height: plan.height }, { width: 16384, height: 16384 })) {
    throw new Error('Supply a v1 slice plan with a source image, screen ID and integer reference size');
  }
  const ids = new Set();
  const sprites = [];
  const operations = [];
  const validTypes = new Set(['group', 'image', 'button', 'text']);

  function visit(spec, parent, owner) {
    if (!spec || typeof spec.id !== 'string' || !identifier.test(spec.id) || ids.has(spec.id)
      || !validTypes.has(spec.type) || !inside(spec.rect, parent)) {
      throw new Error(`Invalid or duplicate slice node: ${spec?.id ?? '(missing ID)'}`);
    }
    ids.add(spec.id);
    const { capture, border, children = [], ...node } = spec;
    if (!Array.isArray(children)) throw new Error(`Expected children array: ${spec.id}`);
    for (const [index, child] of children.entries()) {
      if (!inside(child?.rect, spec.rect)) throw new Error(`Slice outside its parent: ${child?.id}`);
      if (children.slice(0, index).some((sibling) => touches(sibling.rect, child.rect))) {
        throw new Error(`Overlapping sibling slices: ${spec.id}/${child.id}`);
      }
    }
    const rect = { ...spec.rect, x: parent.x + spec.rect.x, y: parent.y + spec.rect.y };
    const isContainer = spec.type === 'group' || children.length > 0;
    const name = isContainer ? `Group · ${spec.id}` : owner;
    if (isContainer) operations.push({ type: 'group', name, owner });
    let crop;
    if (spec.type === 'image' || spec.type === 'button' || (spec.type === 'group' && capture)) {
      const id = spec.type === 'group' ? `${spec.id}_plate` : spec.id;
      if (spec.type === 'group') {
        if (!identifier.test(id) || ids.has(id)) throw new Error(`Reserved plate ID already used: ${id}`);
        ids.add(id);
      } else {
        node.asset = id;
      }
      const file = `art/${id}.png`;
      crop = { type: 'crop', id, owner: name, file, rect, clear: [], repair: [] };
      sprites.push({ id, file, ...(border ? { border } : {}) });
      operations.push(crop);
    }
    if (spec.type === 'text') {
      if (children.length) throw new Error(`Text slice cannot contain children: ${spec.id}`);
      operations.push({ type: 'text', owner, rect, node });
    }

    const results = children.map((child) => visit(child, rect, name));
    if (crop) {
      for (const covered of results.flatMap((result) => result.coverage)) {
        const relative = { ...covered.rect, x: covered.rect.x - rect.x, y: covered.rect.y - rect.y };
        crop[covered.type === 'text' ? 'repair' : 'clear'].push(relative);
      }
    }
    node.children = results.map((result) => result.node);
    if (crop && spec.type === 'group') {
      node.children.unshift({ id: crop.id, type: 'image', asset: crop.id, anchor: 'stretch',
        rect: { x: 0, y: 0, width: rect.width, height: rect.height } });
    }
    const coverage = crop || spec.type === 'text'
      ? [{ type: spec.type, rect }] : results.flatMap((result) => result.coverage);
    return { node, coverage };
  }

  const root = { id: 'reference_layers', type: 'group', capture: plan.captureBackground !== false,
    rect: { x: 0, y: 0, width: plan.width, height: plan.height }, children: plan.nodes };
  const { node } = visit(root, root.rect, null);
  return {
    screen: { version: 1, id: plan.id, width: plan.width, height: plan.height, assets: sprites, nodes: node.children },
    operations,
  };
}

async function cropImage(source, output, crop) {
  const args = ['run', source, ...command('image.crop', crop.rect)];
  if (crop.clear.length || crop.repair.length) {
    args.push(...command('layer.new.layerFromBackground', { ifPresent: true }));
    for (const rect of crop.repair) {
      args.push(...command('select.rect', { ...rect, antiAlias: false }),
        ...command('edit.contentAwareFill', { output: 'current', seed: 1 }));
    }
    for (const [index, rect] of crop.clear.entries()) {
      args.push(...command('select.rect', { ...rect, antiAlias: false, mode: index ? 'add' : 'replace' }));
    }
    if (crop.clear.length) args.push(...command('edit.clear', {}));
  }
  try {
    await exec(cli(), [...args, '--out', join(output, crop.file)], options);
  } catch (error) {
    throw new Error(`PhotoCraft crop ${crop.id}: ${error.stderr?.trim() || error.message}`);
  }
}

async function saveNative(output, screen, operations) {
  const steps = [];
  for (const op of operations) {
    const rect = op.rect;
    let layer;
    if (op.type === 'group') {
      layer = op.name;
      steps.push(edit('layer.new.group', { name: layer }));
    } else if (op.type === 'crop') {
      layer = op.id;
      steps.push(call('doc.place', { path: join(output, op.file), fit: false, scale: 100,
        center: [rect.x + rect.width / 2, rect.y + rect.height / 2] }));
      steps.push(edit('layer.renameLayer', { name: layer }));
    } else {
      layer = op.node.id;
      steps.push(edit('type.create', { name: layer, text: op.node.text,
        box: [rect.x, rect.y, rect.width, rect.height], size: op.node.fontSize,
        color: op.node.color, align: op.node.align }));
    }
    if (op.owner) steps.push(edit('layer.moveTo', { layer, target: op.owner, position: 'into' }));
  }
  const native = join(output, 'layers.pcraft');
  const calls = [call('doc.new', { width: screen.width, height: screen.height, background: 'transparent', name: screen.id })];
  for (let index = 0; index < steps.length; index += 128) calls.push(call('batch', { steps: steps.slice(index, index + 128) }));
  calls.push(call('doc.save', { path: native, pcraftFullComposite: true }));
  const job = exec(cli(), ['serve', '--automation-read-root', output, '--automation-write-root', output],
    { ...options, env: { ...process.env, PHOTOCRAFT_CONTROL_PORT: '' } });
  // promisify(execFile) exposes the child process as .child.
  job.child.stdin.on('error', () => {}); // execFile reports an early exit.
  job.child.stdin.end(calls.map((message, index) => JSON.stringify({ id: index + 1, ...message })).join('\n') + '\n');
  const { stdout } = await job;
  const replies = stdout.trim().split('\n').filter(Boolean).map((line) => JSON.parse(line));
  for (const reply of replies) {
    if (reply.error || reply.result?.failed) throw new Error(`PhotoCraft layering failed: ${JSON.stringify(reply)}`);
  }
  if (replies.length !== calls.length) throw new Error('PhotoCraft did not complete the layering requests');
  await realpath(native);
}

async function publishArt(destination, writeArt) {
  const target = resolve(destination);
  const directory = dirname(target);
  await mkdir(directory, { recursive: true });
  const temporary = await mkdtemp(join(directory, '.ui-slice-'));
  try {
    await mkdir(join(temporary, 'art'));
    await writeArt(temporary);
    await readBundle(join(temporary, 'screen.json'));
    await rename(temporary, target);
  } finally {
    await rm(temporary, { recursive: true, force: true });
  }
  return { output: target, screen: join(target, 'screen.json'), layers: join(target, 'layers.pcraft') };
}

export async function splitReference(file, destination) {
  const planPath = await realpath(file);
  const plan = JSON.parse(await readFile(planPath, 'utf8'));
  const { screen, operations } = compilePlan(plan);
  const source = await realpath(resolve(dirname(planPath), plan.source));
  if (!['.png', '.jpg', '.jpeg'].includes(extname(source).toLowerCase())) {
    throw new Error('Slice plans take a flattened PNG or JPEG reference');
  }
  return publishArt(destination, async (temporary) => {
    const { stdout } = await exec(cli(), ['info', source], options);
    const sourceInfo = JSON.parse(stdout);
    if (sourceInfo.width !== plan.width || sourceInfo.height !== plan.height) {
      throw new Error(`Slice plan is ${plan.width}×${plan.height}, but PhotoCraft read ${sourceInfo.width}×${sourceInfo.height}`);
    }
    for (const crop of operations.filter((op) => op.type === 'crop')) await cropImage(source, temporary, crop);
    const screenPath = join(temporary, 'screen.json');
    await writeFile(screenPath, JSON.stringify(screen, null, 2) + '\n');
    await readBundle(screenPath);
    await saveNative(temporary, screen, operations);
  });
}

export async function resyncLayers(file, destination) {
  const source = await realpath(file);
  const { bundle } = await readBundle(source);
  const screen = JSON.parse(await readFile(source, 'utf8'));
  const native = await realpath(join(dirname(source), 'layers.pcraft'));
  const { stdout } = await exec(cli(), ['info', native], options);
  const info = JSON.parse(stdout);
  if (info.width !== bundle.width || info.height !== bundle.height) throw new Error('PhotoCraft canvas differs from screen.json');
  const layers = new Map();
  function collect(children) {
    for (const layer of children) {
      const named = layers.get(layer.name) ?? [];
      named.push(layer);
      layers.set(layer.name, named);
      collect(layer.children ?? []);
    }
  }
  collect(info.layers);
  function getLayer(id) {
    const named = layers.get(id) ?? [];
    if (named.length !== 1) throw new Error(`Expected one PhotoCraft layer named ${id}`);
    return named[0];
  }
  function updateText(nodes) {
    for (const node of nodes) {
      if (node.type === 'text') {
        const layer = getLayer(node.id);
        if (layer.kind !== 'Type' || typeof layer.text?.text !== 'string') throw new Error(`Expected an editable text layer: ${node.id}`);
        node.text = layer.text.text;
      }
      updateText(node.children ?? []);
    }
  }
  updateText(screen.nodes);
  return publishArt(destination, async (temporary) => {
    for (const asset of screen.assets) {
      if (asset.file !== `art/${asset.id}.png`) throw new Error(`Expected a PhotoCraft work folder: ${asset.id}`);
      const layer = getLayer(asset.id);
      await exec(cli(), ['run', native, ...command('layer.quickExportAsPng',
        { layer: layer.id, path: join(temporary, asset.file) })], options);
    }
    await copyFile(native, join(temporary, 'layers.pcraft'));
    await writeFile(join(temporary, 'screen.json'), JSON.stringify(screen, null, 2) + '\n');
  });
}
