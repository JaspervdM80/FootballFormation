// Runs the UI tests against a database that exists only for the run.
//
// This wrapper owns the throwaway data directory rather than playwright.config.js, because that
// config is imported again by every worker process — anything it created or deleted would happen
// more than once, and deleting a database halfway through a run is not a failure mode worth having.
// Here it happens once, before Playwright is started, and the path is handed down through the
// environment. Same reasoning as scripts/visual-check.sh, which owns its own temp database.
//
//   npm test                 # everything
//   npm test -- squad        # specs matching "squad"
//   npm run test:headed      # watch it happen
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const dataDir = mkdtempSync(join(tmpdir(), 'ff-ui-tests-'));
const keep = process.env.UI_TEST_KEEP_DATA === '1';

console.log(`Database for this run: ${dataDir}${keep ? ' (kept)' : ''}`);

const result = spawnSync('npx', ['playwright', 'test', ...process.argv.slice(2)], {
  stdio: 'inherit',
  env: { ...process.env, UI_TEST_DATA_DIR: dataDir },
  cwd: import.meta.dirname,
  shell: process.platform === 'win32',
});

if (!keep) {
  // The app holds the SQLite file open until it exits, which Playwright has already waited for.
  rmSync(dataDir, { recursive: true, force: true });
}

process.exit(result.status ?? 1);
