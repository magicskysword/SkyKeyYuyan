import { spawn, execFile } from 'node:child_process';
import { resolve } from 'node:path';
import { promisify } from 'node:util';
import { artifacts, root } from './lib/artifacts.mjs';

let processor = process.env.PROCESSOR_IDENTIFIER ?? process.arch;
if (process.platform === 'win32') {
  const result = await promisify(execFile)('powershell.exe', ['-NoProfile', '-Command', '(Get-CimInstance Win32_Processor).Name'], { windowsHide: true });
  processor = result.stdout.trim();
}
await new Promise((accept, reject) => {
  const child = spawn('dotnet', ['run', '--project', 'scripts/benchmark/SkyKeyYuyan.Benchmarks.csproj', '--configuration', 'Release', '--', root, resolve(artifacts, 'Reports/performance.json'), processor], { cwd: root, stdio: 'inherit', windowsHide: true, shell: false });
  child.on('error', reject);
  child.on('exit', code => code === 0 ? accept() : reject(new Error(`Benchmark failed: ${code}`)));
});
