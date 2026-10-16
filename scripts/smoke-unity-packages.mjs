import { spawn, execFile } from 'node:child_process';
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { promisify } from 'node:util';
import { artifacts, removeArtifact, root } from './lib/artifacts.mjs';

const command = promisify(execFile);
function option(name, required = false) {
  const index = process.argv.indexOf(name);
  const value = index >= 0 ? process.argv[index + 1] : null;
  if ((required || index >= 0) && (!value || value.startsWith('--'))) throw new Error(`${name} requires a path`);
  return value ? resolve(value) : null;
}
const editor = option('--unity', true);
const sdk = option('--fairygui');
const project = resolve(artifacts, 'UnityPackageSmoke');
const cache = resolve(artifacts, 'UnityPackageSmokeCache');
const staging = resolve(artifacts, 'UnityPackageSmokeSamples');
const player = resolve(artifacts, 'PackagePlayer/Mono');
const reports = resolve(artifacts, 'Reports');
const packageReport = JSON.parse(await readFile(resolve(reports, 'upm-packages.json'), 'utf8'));
const packages = new Map();
for (const entry of packageReport.packages) {
  const path = resolve(artifacts, 'UPM', entry.file);
  const { stdout } = await command('tar', ['-xOf', path, 'package/package.json'], { windowsHide: true });
  const manifest = JSON.parse(stdout);
  if (manifest.name !== entry.name || manifest.version !== entry.version) throw new Error('Archive metadata differs from the package report');
  packages.set(manifest.name, { manifest, file: entry.file });
}
for (const directory of [project, cache, staging, player]) await removeArtifact(directory);
await mkdir(resolve(project, 'Packages'), { recursive: true });
await mkdir(resolve(project, 'Assets'), { recursive: true });
await mkdir(reports, { recursive: true });
const source = resolve(root, 'unity/SkyKeyYuyan.Unity');
await cp(resolve(source, 'ProjectSettings'), resolve(project, 'ProjectSettings'), { recursive: true });
await cp(resolve(source, 'Assets/Validation'), resolve(project, 'Assets/Validation'), { recursive: true });
await cp(resolve(source, 'Assets/Validation.meta'), resolve(project, 'Assets/Validation.meta'));
const sample = packages.get('com.stellarnox.skykeyyuyan.ui.ugui');
await mkdir(staging, { recursive: true });
await command('tar', ['-xzf', resolve(artifacts, 'UPM', sample.file), '-C', staging, 'package/Samples~/QuickStart'], { windowsHide: true });
await cp(resolve(staging, 'package/Samples~/QuickStart'), resolve(project, 'Assets/SkyKeyYuyanSamples'), { recursive: true });
await removeArtifact(staging);
if (sdk) {
  const destination = resolve(project, 'Assets/ThirdParty/FairyGUI');
  await mkdir(destination, { recursive: true });
  for (const folder of ['Scripts', 'Resources']) await cp(resolve(sdk, 'Assets', folder), resolve(destination, folder), { recursive: true });
  await cp(resolve(sdk, 'LICENSE'), resolve(destination, 'LICENSE.txt'));
  const settingsFile = resolve(project, 'ProjectSettings/ProjectSettings.asset');
  const settings = await readFile(settingsFile, 'utf8');
  if (!settings.includes('  scriptingDefineSymbols: {}')) throw new Error('Expected an unconfigured scripting define map');
  await writeFile(settingsFile, settings.replace('  scriptingDefineSymbols: {}', '  scriptingDefineSymbols:\n    Standalone: SKY_FAIRYGUI'));
}

async function run(file, args, label) {
  process.stdout.write(`${label}\n`);
  await new Promise((accept, reject) => {
    const child = spawn(file, args, { cwd: root, stdio: 'inherit', windowsHide: true, shell: false, env: { ...process.env, UPM_CACHE_ROOT: cache } });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? accept() : reject(new Error(`${label} failed: ${code}; logs: ${reports}`)));
  });
}
const manifest = JSON.parse(await readFile(resolve(source, 'Packages/manifest.json'), 'utf8'));
delete manifest.dependencies['com.unity.test-framework'];
for (const { manifest: value, file } of packages.values())
  manifest.dependencies[value.name] = 'file:' + resolve(artifacts, 'UPM', file).replaceAll('\\', '/');
await writeFile(resolve(project, 'Packages/manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
await run(editor, ['-batchmode', '-quit', '-projectPath', project, '-executeMethod', 'SkyKeyYuyan.Validation.Editor.SkyPlayerBuild.Build', '--sky-backend', 'Mono', '--sky-output', player, '-logFile', resolve(reports, 'unity-package-build.log')], 'Build installed UPM packages');
const lock = JSON.parse(await readFile(resolve(project, 'Packages/packages-lock.json'), 'utf8'));
const installed = [...packages.values()].map(({ manifest: value }) => {
  const dependency = lock.dependencies[value.name];
  if (dependency?.source !== 'local-tarball' || dependency.version !== manifest.dependencies[value.name]) throw new Error(`Package did not resolve from its archive: ${value.name}`);
  return { name: value.name, version: value.version, source: dependency.source };
});
const reportPath = resolve(reports, 'player-package-mono.json');
await run(resolve(player, 'SkyKeyYuyan.exe'), ['-batchmode', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '--sky-validation', '--sky-report', reportPath, '-logFile', resolve(reports, 'player-package-mono.log')], 'Run installed UPM packages');
const result = JSON.parse(await readFile(reportPath, 'utf8'));
if (!result.passed) throw new Error(result.error);
const dependencies = Object.fromEntries(Object.entries(lock.dependencies).filter(([name]) => ['com.unity.inputsystem', 'com.unity.textmeshpro', 'com.unity.ugui'].includes(name)).map(([name, entry]) => [name, entry.version]));
if (sdk) dependencies.fairygui = (await command('git', ['-C', sdk, 'rev-parse', 'HEAD'], { windowsHide: true })).stdout.trim();
await writeFile(resolve(reports, 'unity-package-install.json'), JSON.stringify({ passed: true, version: packageReport.version, unity: result.unity, dependencies, packages: installed, cases: result.cases }, null, 2) + '\n');
process.stdout.write(`UPM consumer passed: ${installed.length} packages, ${result.cases.length} cases\n`);
