import { cp, mkdir, rm } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const assets = resolve(root, 'unity/SkyKeyYuyan.Unity/Assets');
const output = resolve(assets, 'SkyKeyYuyanSamples');
const inside = relative(assets, output);
if (!inside || inside.startsWith('..') || isAbsolute(inside)) throw new Error('Sample output is outside Assets');
await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
await cp(resolve(root, 'unity/SkyKeyYuyan.Unity/Packages/com.stellarnox.skykeyyuyan.ui.ugui/Samples~/QuickStart'), output, { recursive: true });
process.stdout.write(`Unity development samples: ${output}\n`);
