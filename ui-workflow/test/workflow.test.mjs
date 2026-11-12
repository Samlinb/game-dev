import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';
import { compareBundles, readBundle, readPacked } from '../src/bundle.mjs';
import { renderPreview } from '../src/preview.mjs';

const exec = promisify(execFile);
const cli = fileURLToPath(new URL('../cli.mjs', import.meta.url));
const reward = fileURLToPath(new URL('../examples/reward', import.meta.url));
const screen = join(reward, 'screen.json');

async function scratch(t) {
  const folder = await mkdtemp(join(tmpdir(), 'ui-workflow-test-'));
  t.after(() => rm(folder, { recursive: true, force: true }));
  return folder;
}

async function run(...args) {
  const { stdout } = await exec(process.execPath, [cli, ...args], { windowsHide: true });
  return JSON.parse(stdout);
}

test('flat import data and HTML retain parent, state and action bindings', async () => {
  const { bundle, files } = await readBundle(screen);
  const button = bundle.nodes.find((node) => node.id === 'claim_button');
  assert.equal(button.parent, 'dialog');
  assert.equal(button.action, 'claim_reward');
  assert.equal(button.pressed, 'claim_pressed');
  assert.equal(bundle.nodes.find((node) => node.id === 'claim_label').parent, button.id);
  assert.match(renderPreview(bundle, files), /data-action="claim_reward"/);
});

test('pack and stage are idempotent and detect edited payloads', async (t) => {
  const folder = await scratch(t);
  const first = await run('pack', screen, join(folder, 'pack'));
  assert.equal((await run('pack', screen, join(folder, 'pack'))).output, first.output);
  const bundle = join(first.output, 'bundle.json');
  await mkdir(join(folder, 'Assets'));
  await mkdir(join(folder, 'ProjectSettings'));
  await writeFile(join(folder, 'ProjectSettings', 'ProjectVersion.txt'), 'm_EditorVersion: 2022.3.28f1');
  const staged = await run('stage', bundle, folder);
  assert.equal((await run('stage', bundle, folder)).output, staged.output);
  const stagedFile = join(staged.output, 'bundle.json');
  assert.equal((await readPacked(stagedFile)).bundle.revision, first.revision);
  await writeFile(join(staged.output, 'images', 'claim.png'), 'replaced');
  await assert.rejects(readPacked(stagedFile), /Bundle PNG differs/);
});

test('revision diff reports text changes and sibling order', async (t) => {
  const folder = await scratch(t);
  await cp(reward, folder, { recursive: true });
  const changedPath = join(folder, 'screen.json');
  const changed = JSON.parse(await readFile(changedPath, 'utf8'));
  changed.nodes[1].children[0].text = 'Updated title';
  changed.nodes[1].children.push(changed.nodes[1].children.shift());
  await writeFile(changedPath, JSON.stringify(changed));
  const { bundle: before } = await readBundle(screen);
  const { bundle: after } = await readBundle(changedPath);
  const diff = compareBundles(before, after);
  assert.notEqual(diff.from, diff.to);
  assert(diff.nodes.changed.includes('reward_title'));
  assert(diff.nodes.changed.includes('close_button'));
  assert.deepEqual(diff.assets.changed, []);
});

test('PhotoCraft creates a native document and a valid package', { skip: !process.env.PHOTOCRAFT_CLI }, async (t) => {
  const folder = await scratch(t);
  const plan = fileURLToPath(new URL('../examples/photo-crop/plan.json', import.meta.url));
  const result = await run('split', plan, join(folder, 'art'));
  const { bundle } = await readBundle(result.screen);
  assert.equal(bundle.actions[0].action, 'claim_reward');
  const { stdout } = await exec(process.env.PHOTOCRAFT_CLI, ['run', result.layers, '--cmd', 'document.inspect']);
  const info = JSON.parse(stdout.trim()).result;
  assert.equal(info.width, 480);
  const root = info.layers.find((layer) => layer.name === 'Group · reference_layers');
  const content = root.children.find((layer) => layer.name === 'Group · content');
  assert.deepEqual(content.children.find((layer) => layer.name === 'claim').bounds, [120, 410, 240, 60]);
  const resynced = await run('resync', result.screen, join(folder, 'resynced'));
  assert.equal((await readBundle(resynced.screen)).bundle.revision, bundle.revision);
});
