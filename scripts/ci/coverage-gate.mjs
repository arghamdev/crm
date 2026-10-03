// Fails the build when line coverage of a gated assembly drops below its floor.
// Usage: node scripts/ci/coverage-gate.mjs <reportgenerator Summary.json> Crm.Domain=75 Crm.Application=80
import { readFileSync } from 'node:fs';

const [summaryPath, ...rules] = process.argv.slice(2);
if (!summaryPath || rules.length === 0) {
  console.error('Usage: coverage-gate.mjs <Summary.json> <Assembly>=<minPercent> ...');
  process.exit(2);
}
const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
const assemblies = new Map((summary.coverage?.assemblies ?? []).map(a => [a.name, a.coverage]));
let failed = false;
for (const rule of rules) {
  const [name, min] = rule.split('=');
  const actual = assemblies.get(name);
  if (actual === undefined) { console.error(`✗ ${name}: not found in coverage report`); failed = true; continue; }
  const ok = actual >= Number(min);
  console.log(`${ok ? '✓' : '✗'} ${name}: ${actual}% line coverage (minimum ${min}%)`);
  if (!ok) failed = true;
}
console.log(`Overall line coverage: ${summary.summary?.linecoverage}%`);
process.exit(failed ? 1 : 0);
