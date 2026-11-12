const { createHash } = require('node:crypto');
const { readFile } = require('node:fs/promises');
const { dirname, join } = require('node:path');

const request = (...args) => Editor.Message.request(...args);
const scene = (method, ...args) => request('scene', 'execute-scene-script', { name: 'ui-workflow', method, args });
const identifier = /^[a-z][a-z0-9_]{0,63}$/;
let importing = false;

async function importBundle() {
  const ids = Editor.Selection.getSelected('asset');
  if (ids.length !== 1) throw new Error('Select one bundle.json asset');
  const manifest = await request('asset-db', 'query-asset-info', ids[0]);
  if (!manifest?.url?.startsWith('db://assets/UIWorkflow/') || !manifest.url.endsWith('/bundle.json')) {
    throw new Error('Select bundle.json under assets/UIWorkflow');
  }
  const bundle = JSON.parse(await readFile(manifest.file, 'utf8'));
  if (bundle.format !== 'ui-workflow' || bundle.version !== 1 || !identifier.test(bundle.id)
    || !/^[a-f0-9]{16}$/.test(bundle.revision)
    || manifest.url !== `db://assets/UIWorkflow/${bundle.id}_${bundle.revision}/bundle.json`) {
    throw new Error('Expected an intact UI Workflow v1 bundle in its revision folder');
  }
  const folder = manifest.url.slice(0, -'/bundle.json'.length);
  const prefab = `${folder}/${bundle.id}.prefab`;
  for (const asset of bundle.assets) {
    if (!identifier.test(asset.id) || asset.file !== `images/${asset.id}.png`) throw new Error('Invalid bundle image path');
    const png = await readFile(join(dirname(manifest.file), asset.file));
    if (createHash('sha256').update(png).digest('hex') !== asset.sha256) throw new Error(`PNG hash differs: ${asset.id}`);
  }
  if (await request('asset-db', 'query-asset-info', prefab)) return prefab;

  const frames = {};
  for (const asset of bundle.assets) {
    const url = `${folder}/${asset.file}`;
    const info = await request('asset-db', 'query-asset-info', url);
    if (!info?.uuid) throw new Error(`Cocos has not imported ${url}`);
    const meta = await request('asset-db', 'query-asset-meta', info.uuid);
    const frame = Object.values(meta?.subMetas ?? {}).find((sub) => sub.importer === 'sprite-frame');
    if (!frame?.uuid) throw new Error(`Set the PNG asset type to Sprite Frame: ${url}`);
    if (asset.border) {
      const { left, top, right, bottom } = asset.border;
      const settings = { trimType: 'none', borderLeft: left, borderTop: top, borderRight: right, borderBottom: bottom };
      if (Object.entries(settings).some(([key, value]) => frame.userData[key] !== value)) {
        Object.assign(frame.userData, settings);
        await request('asset-db', 'save-asset-meta', info.uuid, JSON.stringify(meta));
      }
    }
    frames[asset.id] = frame.uuid;
  }

  const uuid = await scene('build', bundle, frames);
  if (typeof uuid !== 'string' || !uuid) throw new Error('Cocos did not create a scene node');
  try {
    const result = await request('scene', 'create-prefab', uuid, prefab);
    if (!result && !(await request('asset-db', 'query-asset-info', prefab))) throw new Error(`Cocos did not save ${prefab}`);
  } finally {
    await scene('remove', uuid);
  }
  return prefab;
}

exports.load = () => {};
exports.unload = () => {};
exports.methods = {
  async importBundle() {
    if (importing) return;
    importing = true;
    try {
      console.log(`[UI Workflow] ${await importBundle()}`);
    } catch (error) {
      console.error(`[UI Workflow] ${error.message}`);
    } finally {
      importing = false;
    }
  },
};
