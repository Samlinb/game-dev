import { access, mkdir, mkdtemp, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { compareBundles, readBundle, readPacked } from './src/bundle.mjs';
import { resyncLayers, splitReference } from './src/photocraft.mjs';
import { renderPreview } from './src/preview.mjs';

const [command, source, output, ...extra] = process.argv.slice(2);

async function pack(bundle, files, directory) {
  const root = resolve(directory);
  const name = `${bundle.id}_${bundle.revision}`;
  const target = join(root, name);
  const manifest = JSON.stringify(bundle, null, 2) + '\n';
  await mkdir(root, { recursive: true });
  const temporary = await mkdtemp(join(root, '.ui-workflow-'));
  try {
    await mkdir(join(temporary, 'images'));
    for (const [file, png] of files) await writeFile(join(temporary, file), png);
    await writeFile(join(temporary, 'bundle.json'), manifest);
    try {
      await rename(temporary, target);
    } catch (error) {
      if (!['EEXIST', 'ENOTEMPTY', 'EPERM'].includes(error.code)) throw error;
      const existing = await readFile(join(target, 'bundle.json'), 'utf8');
      if (existing !== manifest) throw new Error(`Bundle output conflicts with ${target}`);
      for (const [file, png] of files) {
        if (!(await readFile(join(target, file))).equals(png)) {
          throw new Error(`Existing bundle has changed: ${join(target, file)}`);
        }
      }
    }
  } finally {
    await rm(temporary, { recursive: true, force: true });
  }
  return target;
}

async function stage(project, bundle, files) {
  const root = resolve(project);
  try {
    await access(join(root, 'ProjectSettings', 'ProjectVersion.txt'));
  } catch {
    const config = await readFile(join(root, 'package.json'), 'utf8').then(JSON.parse).catch(() => null);
    if (!config?.creator?.version) throw new Error('Choose a Unity or Cocos Creator project root');
    await access(join(root, 'assets'));
    return pack(bundle, files, join(root, 'assets', 'UIWorkflow'));
  }
  await access(join(root, 'Assets'));
  return pack(bundle, files, join(root, 'Assets', 'UIWorkflow'));
}

async function run() {
  if (extra.length || !source || !['check', 'pack', 'preview', 'split', 'resync', 'stage', 'diff'].includes(command)
    || (command === 'check' ? Boolean(output) : !output)) {
    throw new Error('Commands: check <screen.json> | pack <screen.json> <output-dir> | preview <screen.json> <output.html> | split <plan.json> <new-dir> | resync <screen.json> <new-dir> | stage <bundle.json> <engine-project> | diff <old-bundle.json> <new-bundle.json>');
  }
  if (command === 'split') return { ok: true, ...await splitReference(source, output) };
  if (command === 'resync') return { ok: true, ...await resyncLayers(source, output) };
  if (command === 'stage') {
    const { bundle, files } = await readPacked(source);
    return { ok: true, output: await stage(output, bundle, files) };
  }
  if (command === 'diff') {
    const { bundle: before } = await readPacked(source);
    const { bundle: after } = await readPacked(output);
    return { ok: true, ...compareBundles(before, after) };
  }
  const { bundle, files } = await readBundle(source);
  const result = { ok: true, id: bundle.id, revision: bundle.revision, actions: bundle.actions };
  if (command === 'pack') result.output = await pack(bundle, files, output);
  if (command === 'preview') {
    result.output = resolve(output);
    if (!result.output.endsWith('.html')) throw new Error('Preview output must end in .html');
    await mkdir(dirname(result.output), { recursive: true });
    await writeFile(result.output, renderPreview(bundle, files));
  }
  return result;
}

try {
  console.log(JSON.stringify(await run(), null, 2));
} catch (error) {
  console.error(JSON.stringify({ ok: false, issues: error.issues ?? [{ message: error.message }] }, null, 2));
  process.exitCode = 1;
}
