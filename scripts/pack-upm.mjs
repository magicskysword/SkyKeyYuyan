import { cp, mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { artifacts, removeArtifact, root } from './lib/artifacts.mjs';

const run = promisify(execFile);
const packagesRoot = resolve(root, 'unity/SkyKeyYuyan.Unity/Packages');
const output = resolve(artifacts, 'UPM');
await mkdir(output, { recursive: true });
const names = (await readdir(packagesRoot)).filter(name => name.startsWith('com.stellarnox.skykeyyuyan.')).sort();
if (names.length !== 8) throw new Error('Expected eight SKY packages');
const packages = await Promise.all(names.map(async name => ({ ...JSON.parse(await readFile(resolve(packagesRoot, name, 'package.json'), 'utf8')), path: resolve(packagesRoot, name) })));
const version = packages[0].version;
if (!/^\d+\.\d+\.\d+(?:-[\da-z.-]+)?$/i.test(version)) throw new Error('Package version is not semver');
const properties = await readFile(resolve(root, 'dotnet/Directory.Build.props'), 'utf8');
if (properties.match(/<Version>([^<]+)<\/Version>/)?.[1] !== version) throw new Error('.NET and UPM versions differ');
const tag = process.env.GITHUB_REF_TYPE === 'tag' ? process.env.GITHUB_REF_NAME : null;
if (tag && tag !== `v${version}`) throw new Error('Release tag and package versions differ');

const libraries = { core: 'SkyKeyYuyan.Core', chinese: 'SkyKeyYuyan.Languages.Chinese', english: 'SkyKeyYuyan.Languages.English', korean: 'SkyKeyYuyan.Languages.Korean' };
const reports = [];
for (const manifest of packages) {
  if (manifest.version !== version || manifest.unity !== '2020.3') throw new Error(`Version baseline differs: ${manifest.name}`);
  for (const [name, required] of Object.entries(manifest.dependencies ?? {}))
    if (names.includes(name) && required !== version) throw new Error(`Dependency version differs: ${manifest.name} / ${name}`);
  const module = manifest.name.substring('com.stellarnox.skykeyyuyan.'.length);
  const required = ['package.json', 'LICENSE'];
  if (libraries[module]) for (const extension of ['dll', 'pdb', 'xml']) required.push(`Plugins/${libraries[module]}.${extension}`, `Plugins/${libraries[module]}.${extension}.meta`);
  if (['chinese', 'english', 'korean'].includes(module)) {
    const resources = ['keyboard.bytes', ...(module === 'korean' ? [] : ['lexicon.bytes'])];
    for (const resource of resources) required.push(`Data/Resources/SkyKeyYuyan/${module}/${resource}`, `Data/Resources/SkyKeyYuyan/${module}/${resource}.meta`);
  }
  if (module === 'ui.fairygui') {
    const resource = 'Runtime/Resources/SkyKeyYuyan/UI/FairyGUI/SkyKeyYuyan_fui.bytes'; required.push(resource, resource + '.meta');
    if ((await readFile(resolve(manifest.path, resource))).subarray(0, 42).toString().startsWith('version https://git-lfs.github.com')) throw new Error('Fetch the Git LFS interface resource before packing');
  }
  for (const path of required) if (!(await stat(resolve(manifest.path, path))).isFile()) throw new Error(`Package input is missing: ${manifest.name}/${path}`);
  const staging = resolve(artifacts, 'UPMStaging', manifest.name);
  await removeArtifact(staging); await mkdir(staging, { recursive: true });
  await cp(manifest.path, resolve(staging, 'package'), { recursive: true });
  await cp(resolve(root, 'docs'), resolve(staging, 'package/Documentation~'), { recursive: true });
  await writeFile(resolve(staging, 'package/README.md'), `# ${manifest.displayName}\n\n${manifest.description}\n\n[快速开始](Documentation~/getting-started.md) · [接口与数据契约](Documentation~/api-contracts.md) · [Unity 接入](Documentation~/unity-integration.md)\n`);
  const filename = `${manifest.name}-${version}.tgz`;
  await run('tar', ['-czf', resolve(output, filename), '-C', staging, 'package'], { maxBuffer: 1024 * 1024 });
  const { stdout } = await run('tar', ['-tzf', resolve(output, filename)], { maxBuffer: 4 * 1024 * 1024 });
  const entries = new Set(stdout.split(/\r?\n/).filter(Boolean).map(path => path.replace(/^\.\//, '')));
  for (const path of required) if (!entries.has(`package/${path}`)) throw new Error(`Archive input is missing: ${filename}/${path}`);
  reports.push({ name: manifest.name, version, file: filename, bytes: (await stat(resolve(output, filename))).size });
  await removeArtifact(staging);
}
await mkdir(resolve(artifacts, 'Reports'), { recursive: true });
await writeFile(resolve(artifacts, 'Reports/upm-packages.json'), JSON.stringify({ version, packages: reports }, null, 2) + '\n');

const repositoryIndex = process.argv.indexOf('--repository');
let repository = repositoryIndex >= 0 ? process.argv[repositoryIndex + 1] : process.env.GITHUB_REPOSITORY;
if (repositoryIndex >= 0 && !repository) throw new Error('--repository requires a GitHub repository');
if (repository) {
  if (/^[\w.-]+\/[\w.-]+$/.test(repository)) repository = `https://github.com/${repository}`;
  const address = new URL(repository);
  if (address.protocol !== 'https:' || address.hostname !== 'github.com' || !/^\/[\w.-]+\/[\w.-]+\/?$/.test(address.pathname)) throw new Error('Repository must be a GitHub HTTPS repository URL');
  repository = repository.replace(/\/$/, '').replace(/\.git$/, '');
  const metadata = resolve(artifacts, 'OpenUPM'); await mkdir(metadata, { recursive: true });
  for (const manifest of packages) {
    const content = [
      `name: ${manifest.name}`, `displayName: ${JSON.stringify(manifest.displayName)}`, `description: ${JSON.stringify(manifest.description)}`,
      `repoUrl: ${JSON.stringify(repository)}`, 'licenseSpdxId: MIT', 'licenseName: MIT License', 'topics:', '  - utilities',
      'trackingMode: githubRelease', `githubReleaseAssetName: ${JSON.stringify(manifest.name + '-')}`, `readme: "main:docs/README.md"`, `minVersion: ${JSON.stringify(version)}`
    ].join('\n');
    await writeFile(resolve(metadata, manifest.name + '.yml'), content + '\n');
  }
}
process.stdout.write(`UPM packages: ${reports.length}, version ${version}\n`);
