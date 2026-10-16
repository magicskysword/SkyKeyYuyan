import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'unity/SkyKeyYuyan.Unity');
const versionIndex = process.argv.indexOf('--unity-version');
const unityVersion = versionIndex >= 0 ? process.argv[versionIndex + 1] : null;
if (versionIndex >= 0 && !/^\d+\.\d+\.\d+[abfp]\d+$/.test(unityVersion ?? '')) throw new Error('--unity-version requires an exact editor version');
const output = resolve(root, 'artifacts', unityVersion ? `UnityValidation-${unityVersion}` : 'UnityValidation');
const symbolGroup = unityVersion?.startsWith('2020.') ? '1' : 'Standalone';
const sdkIndex = process.argv.indexOf('--fairygui');
const sdk = sdkIndex >= 0 ? resolve(process.argv[sdkIndex + 1] ?? '') : null;
if (sdkIndex >= 0 && !process.argv[sdkIndex + 1]) throw new Error('--fairygui requires the SDK repository path');
const profileIndex = process.argv.indexOf('--profile');
const profile = profileIndex >= 0 ? process.argv[profileIndex + 1] : 'full';
const profiles = ['full', 'core', 'ugui', 'tmp', 'input-system', 'modules-off'];
if (!profiles.includes(profile)) throw new Error(`Unknown validation profile: ${profile}`);
if (sdk && profile !== 'full') throw new Error('FairyGUI validation uses the full profile');

async function removeOutput(target) {
  const inside = relative(resolve(root, 'artifacts'), resolve(target));
  if (!inside || inside.startsWith('..') || isAbsolute(inside)) throw new Error('Validation output is outside artifacts');
  await rm(target, { recursive: true, force: true });
}

await mkdir(output, { recursive: true });
for (const folder of ['Assets', 'Packages', 'ProjectSettings']) {
  const target = resolve(output, folder);
  await removeOutput(target);
  await cp(resolve(source, folder), target, { recursive: true });
}
if (unityVersion) {
  const manifestFile = resolve(output, 'Packages/manifest.json');
  const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));
  if (unityVersion.startsWith('2020.')) manifest.dependencies['com.unity.inputsystem'] = '1.0.2';
  if (Number(unityVersion.split('.')[0]) >= 6000) {
    manifest.dependencies['com.unity.ugui'] = '2.0.0';
    delete manifest.dependencies['com.unity.textmeshpro'];
  }
  await writeFile(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
  await writeFile(resolve(output, 'ProjectSettings/ProjectVersion.txt'), `m_EditorVersion: ${unityVersion}\n`);
}
if (profile !== 'full') {
  await removeOutput(resolve(output, 'Assets'));
  await mkdir(resolve(output, 'Assets'), { recursive: true });
  if (profile === 'core' || profile === 'input-system')
    await removeOutput(resolve(output, 'Packages/com.stellarnox.skykeyyuyan.ui.ugui'));
  const manifestFile = resolve(output, 'Packages/manifest.json');
  const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));
  delete manifest.dependencies['com.unity.test-framework'];
  const keep = {
    core: [], ugui: ['com.unity.ugui'], tmp: ['com.unity.ugui', 'com.unity.textmeshpro'],
    'input-system': ['com.unity.inputsystem'], 'modules-off': ['com.unity.ugui', 'com.unity.textmeshpro', 'com.unity.inputsystem']
  }[profile];
  for (const dependency of ['com.unity.ugui', 'com.unity.textmeshpro', 'com.unity.inputsystem'])
    if (!keep.includes(dependency)) delete manifest.dependencies[dependency];
  await writeFile(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
  const settingsFile = resolve(output, 'ProjectSettings/ProjectSettings.asset');
  let settings = await readFile(settingsFile, 'utf8');
  settings = settings.replace(/  activeInputHandler: \d+/, `  activeInputHandler: ${profile === 'input-system' ? 1 : 0}`);
  if (profile === 'modules-off') {
    const disabled = ['UGUI', 'TMP', 'LEGACY', 'INPUT_SYSTEM', 'UGUI_UI', 'FAIRYGUI_UI'].map(name => `SKY_DISABLE_${name}`).join(';');
    settings = settings.replace('  scriptingDefineSymbols: {}', `  scriptingDefineSymbols:\n    ${symbolGroup}: ${disabled}`);
  }
  await writeFile(settingsFile, settings);
}
if (sdk) {
  const destination = resolve(output, 'Assets/ThirdParty/FairyGUI');
  await mkdir(destination, { recursive: true });
  for (const folder of ['Scripts', 'Resources'])
    await cp(resolve(sdk, 'Assets', folder), resolve(destination, folder), { recursive: true });
  await cp(resolve(sdk, 'LICENSE'), resolve(destination, 'LICENSE.txt'));
  const settingsFile = resolve(output, 'ProjectSettings/ProjectSettings.asset');
  const settings = await readFile(settingsFile, 'utf8');
  if (!settings.includes('  scriptingDefineSymbols: {}')) throw new Error('Expected an unconfigured scripting define map');
  await writeFile(settingsFile, settings.replace('  scriptingDefineSymbols: {}', `  scriptingDefineSymbols:\n    ${symbolGroup}: SKY_FAIRYGUI`));
}
process.stdout.write(`Unity validation project: ${output} (${profile})\n`);
