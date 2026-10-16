import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { artifacts, removeArtifact, root } from './lib/artifacts.mjs';

const properties = await readFile(resolve(root, 'dotnet/Directory.Build.props'), 'utf8');
const version = properties.match(/<Version>([^<]+)<\/Version>/)?.[1];
if (!version) throw new Error('Package version is missing');
const cache = resolve(artifacts, 'NuGetConsumerPackages');
await removeArtifact(cache);
const configuration = resolve(artifacts, 'NuGetConsumer.config');
const localSource = resolve(artifacts, 'NuGet').replaceAll('&', '&amp;').replaceAll('"', '&quot;');
await mkdir(artifacts, { recursive: true });
await writeFile(configuration, `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="${localSource}" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="SkyKeyYuyan.*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
`);
async function run(args) {
  await new Promise((accept, reject) => {
    const child = spawn('dotnet', args, { cwd: root, stdio: 'inherit', shell: false });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? accept() : reject(new Error(`NuGet consumer failed: ${code}`)));
  });
}
const project = 'scripts/smoke/NuGetConsumer.csproj';
const settings = [`-p:SkyPackageVersion=${version}`, `-p:RestorePackagesPath=${cache}`];
await run(['restore', project, ...settings, '--force', '--configfile', configuration]);
await run(['run', '--project', project, '--configuration', 'Release', '--no-restore', ...settings, '--', resolve(artifacts, 'Reports/nuget-consumer.json')]);
