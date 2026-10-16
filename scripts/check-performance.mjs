import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { artifacts, root } from './lib/artifacts.mjs';

const budget = JSON.parse(await readFile(resolve(root, 'scripts/benchmark/budgets.json'), 'utf8'));
const report = JSON.parse(await readFile(resolve(artifacts, 'Reports/performance.json'), 'utf8'));
const checks = [];
function same(name, actual, expected) { checks.push({ name, actual, expected, passed: actual === expected }); }
function limit(name, actual, maximum) { checks.push({ name, actual, maximum, passed: Number.isFinite(actual) && actual <= maximum }); }
same('data-version', report.dataVersion, budget.dataVersion);
for (const [key, expected] of Object.entries(budget.environment)) same('environment/' + key, report.environment[key], expected);
for (const entry of report.initialization) {
  limit(entry.mode + '/ms', entry.elapsedMs, budget.managed.initializationMs);
  limit(entry.mode + '/allocated-bytes', entry.allocatedBytes, budget.managed.initializationAllocatedBytes);
  limit(entry.mode + '/retained-bytes', entry.retainedManagedBytes, budget.managed.retainedManagedBytes);
}
for (const entry of report.measurements) {
  const type = entry.Name.split('/')[0];
  if (type === 'query' || type === 'feedback') {
    limit(entry.Name + '/p95-ms', entry.P95Ms, budget.managed[type + 'P95Ms']);
    limit(entry.Name + '/allocated-bytes', entry.AllocatedBytesPerOperation, budget.managed[type + 'AllocatedBytesPerOperation']);
  } else if (type === 'history') limit(entry.Name + '/p95-ms', entry.P95Ms, budget.managed.historyP95Ms);
}
limit('history/retained-bytes', report.history.retainedManagedBytes, budget.managed.historyRetainedBytes);
const playerIndex = process.argv.indexOf('--player-report');
if (playerIndex >= 0) {
  if (!process.argv[playerIndex + 1]) throw new Error('--player-report requires a JSON path');
  const player = JSON.parse(await readFile(resolve(process.argv[playerIndex + 1]), 'utf8'));
  same('player/passed', player.passed, true);
  same('player/processor', player.processor, budget.environment.processor);
  same('player/unity', player.unity, budget.player.unity);
  same('player/backend', player.backend, budget.player.backend);
  if (!player.measurements?.length) throw new Error('Player measurements are missing');
  for (const entry of player.measurements) limit(entry.name + '/' + entry.entries + '/p95-ms', entry.p95Ms, budget.player.mainThreadP95Ms);
}
const passed = checks.every(entry => entry.passed);
await writeFile(resolve(artifacts, 'Reports/performance-budget.json'), JSON.stringify({ version: budget.version, passed, checks }, null, 2) + '\n');
for (const entry of checks.filter(entry => !entry.passed)) process.stderr.write(JSON.stringify(entry) + '\n');
if (!passed) throw new Error('Performance budget checks failed');
process.stdout.write(`Performance budget passed: ${checks.length} checks\n`);
