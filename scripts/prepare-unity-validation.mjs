import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'unity/SkyKeyYuyan.Unity');
const output = resolve(root, 'artifacts/UnityValidation');
const sdkIndex = process.argv.indexOf('--fairygui');
const sdk = sdkIndex >= 0 ? resolve(process.argv[sdkIndex + 1] ?? '') : null;
if (sdkIndex >= 0 && !process.argv[sdkIndex + 1]) throw new Error('--fairygui requires the SDK repository path');

await mkdir(output, { recursive: true });
for (const folder of ['Assets', 'Packages', 'ProjectSettings']) {
  const target = resolve(output, folder);
  const inside = relative(resolve(root, 'artifacts'), target);
  if (inside.startsWith('..') || isAbsolute(inside)) throw new Error('Validation output is outside artifacts');
  await rm(target, { recursive: true, force: true });
  await cp(resolve(source, folder), target, { recursive: true });
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
  await writeFile(settingsFile, settings.replace('  scriptingDefineSymbols: {}', '  scriptingDefineSymbols:\n    Standalone: SKY_FAIRYGUI'));
}
process.stdout.write(`Unity validation project: ${output}\n`);
