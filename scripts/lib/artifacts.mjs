import { rm } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export const artifacts = resolve(root, 'artifacts');
export async function removeArtifact(path) {
  const inside = relative(artifacts, resolve(path));
  if (!inside || inside.startsWith('..') || isAbsolute(inside)) throw new Error('Output is outside artifacts');
  await rm(path, { recursive: true, force: true });
}
