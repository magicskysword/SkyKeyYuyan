import { spawn } from 'node:child_process';
import { readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { root } from './lib/artifacts.mjs';

async function run(command, args) {
  await new Promise((accept, reject) => {
    const child = spawn(command, args, { cwd: root, stdio: 'inherit', shell: false });
    child.on('error', reject); child.on('exit', code => code === 0 ? accept() : reject(new Error(`${command} failed: ${code}`)));
  });
}
await run(process.execPath, ['scripts/generate-data.mjs']);
await run('dotnet', ['build', 'SkyKeyYuyan.sln', '--configuration', 'Release']);
await run('dotnet', ['test', 'SkyKeyYuyan.sln', '--no-build', '--configuration', 'Release', '--logger', 'trx;LogFilePrefix=dotnet', '--results-directory', 'artifacts/Reports']);
const tests = (await readdir(resolve(root, 'scripts/tests'))).filter(name => name.endsWith('.test.mjs')).map(name => `scripts/tests/${name}`);
await run(process.execPath, ['--test', ...tests]);
await run('dotnet', ['pack', 'SkyKeyYuyan.sln', '--no-build', '--configuration', 'Release']);
await run(process.execPath, ['scripts/smoke-nuget.mjs']);
await run(process.execPath, ['scripts/sync-unity-meta.mjs']);
await run(process.execPath, ['scripts/install-unity-samples.mjs']);
const repositoryIndex = process.argv.indexOf('--repository');
await run(process.execPath, ['scripts/pack-upm.mjs', ...(repositoryIndex >= 0 ? ['--repository', process.argv[repositoryIndex + 1]] : [])]);
