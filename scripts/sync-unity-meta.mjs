import { readdir, readFile, writeFile, access } from 'node:fs/promises';
import { resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packages = resolve(root, 'unity/SkyKeyYuyan.Unity/Packages');
let created = 0;

async function meta(path, folder = false) {
  try { await access(path + '.meta'); return; } catch { /* 已存在的资源身份保持不变。 */ }
  const extension = extname(path).toLowerCase();
  const importer = folder ? 'DefaultImporter' : extension === '.cs' ? 'MonoImporter' : extension === '.asmdef' ? 'AssemblyDefinitionImporter' : ['.bytes', '.json', '.txt', '.xml', '.csv'].includes(extension) ? 'TextScriptImporter' : extension === '.prefab' ? 'PrefabImporter' : 'DefaultImporter';
  let content = `fileFormatVersion: 2\nguid: ${randomUUID().replaceAll('-', '')}\n` + (folder ? 'folderAsset: yes\n' : '') + `${importer}:\n  externalObjects: {}\n`;
  if (importer === 'MonoImporter') content += '  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n';
  content += '  userData: \n  assetBundleName: \n  assetBundleVariant: \n';
  await writeFile(path + '.meta', content);
  created++;
}
async function walk(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name.startsWith('.') || entry.name.endsWith('.meta')) continue;
    const path = resolve(directory, entry.name);
    await meta(path, entry.isDirectory());
    if (entry.isDirectory()) await walk(path);
  }
}
for (const entry of await readdir(packages, { withFileTypes: true })) if (entry.isDirectory() && entry.name.startsWith('com.stellarnox.skykeyyuyan.')) await walk(resolve(packages, entry.name));
const assets = resolve(root, 'unity/SkyKeyYuyan.Unity/Assets');
await walk(assets);
process.stdout.write(`Unity metadata created: ${created}\n`);
