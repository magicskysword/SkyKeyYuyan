import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import { resolve, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const destination = resolve(root, 'data/sources');
const manifest = JSON.parse(await readFile(resolve(root, 'data/manifests/sources.json'), 'utf8'));
if (manifest.formatVersion !== 1) throw new Error('数据来源清单版本无效');

for (const source of manifest.sources) {
  const path = resolve(destination, source.file);
  if (!path.startsWith(destination + sep)) throw new Error('来源文件位置超出数据目录');
  if (!process.argv.includes('--refresh')) {
    try { await access(path); continue; } catch { /* 缺失的数据从固定来源版本取得。 */ }
  }
  let failure;
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const response = await fetch(source.url, { signal: AbortSignal.timeout(60000) });
      if (!response.ok) throw new Error(`${source.id}: HTTP ${response.status}`);
      const bytes = new Uint8Array(await response.arrayBuffer());
      await mkdir(dirname(path), { recursive: true });
      await writeFile(path, bytes);
      process.stdout.write(`${source.id}: ${bytes.length} bytes\n`);
      failure = null;
      break;
    } catch (error) { failure = error; }
  }
  if (failure) throw failure;
}
