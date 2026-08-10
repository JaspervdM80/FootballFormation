import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { tmpdir } from 'node:os';

const PORT = Number(process.env.UI_TEST_PORT ?? 5299);

// Not 5228: that is what `dotnet run` and scripts/visual-check.sh use, and a test run that killed
// the app someone had open to look at something would be a poor trade.
export const BASE_URL = `http://127.0.0.1:${PORT}`;

// run.mjs makes this per-run and throws it away afterwards. The fallback only matters when someone
// runs `npx playwright test` directly, and it is still not a real database.
const DATA_DIR = process.env.UI_TEST_DATA_DIR ?? join(tmpdir(), 'ff-ui-tests-adhoc');

export const ADMIN_STATE = join(import.meta.dirname, '.auth/admin.json');
export const VISITOR_STATE = join(import.meta.dirname, '.auth/visitor.json');

// A Claude Code web container ships a Chromium already, at a revision that will not match whatever
// this package resolves to — so use the one that is there and let Playwright download its own
// everywhere else. Without this the first run in a web session ends at "npx playwright install",
// which that container's egress policy will not allow.
const PREINSTALLED_CHROMIUM = '/opt/pw-browsers/chromium';
export const CHROMIUM_PATH = process.env.UI_TEST_CHROMIUM
  ?? (existsSync(PREINSTALLED_CHROMIUM) ? PREINSTALLED_CHROMIUM : undefined);

// CI builds the app once, in its own job, and hands both browser jobs the published output — so
// point this at that copy and start it directly rather than paying for a second compile here. Left
// unset locally, where `dotnet run` off the sources is the whole point: it picks up an edit.
const APP_DLL = process.env.UI_TEST_APP_DLL;
const START_APP = APP_DLL
  ? `dotnet ${basename(APP_DLL)} --urls ${BASE_URL}`
  : 'dotnet run --project ../../src/FootballFormation.Web/FootballFormation.Web.csproj'
    + ` -c Release --urls ${BASE_URL}`;

export default defineConfig({
  testDir: './specs',
  outputDir: './test-results',
  globalSetup: './global-setup.js',

  // One worker, because every test shares one app instance and one SQLite file. Tests still have to
  // keep out of each other's way — they name the rows they create after themselves — but they are
  // not also racing for the database.
  workers: 1,
  fullyParallel: false,

  // A Blazor Server page is only interactive once its circuit connects, so an action can genuinely
  // need a moment. Give assertions room rather than sprinkling sleeps through the specs.
  timeout: 60_000,
  expect: { timeout: 15_000 },
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
    // Signed in as an admin unless a spec says otherwise; the visitor state is the same browser
    // with no cookie but the language, so an anonymous test is not also a Dutch test.
    storageState: ADMIN_STATE,
    launchOptions: { executablePath: CHROMIUM_PATH },
  },

  projects: [
    {
      name: 'desktop',
      testIgnore: /mobile\..*\.spec\.js/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
    // Only the specs under specs/mobile run here — the app's phone layout is a different set of
    // controls (a drawer instead of an app-bar nav), not the same page made narrow.
    {
      name: 'mobile',
      testMatch: /mobile\..*\.spec\.js/,
      use: { ...devices['Pixel 7'] },
    },
  ],

  // Started before globalSetup, which is what lets that setup sign in over HTTP.
  webServer: {
    command: START_APP,
    // A published app has to be started *from its own directory*, the way the Dockerfile's WORKDIR
    // does it: the content root is the working directory, and from anywhere else MapStaticAssets
    // answers 200 with an empty body for every file — blazor.web.js included, so the page renders,
    // looks right, and never becomes interactive. Left undefined otherwise, so the command above
    // keeps resolving `--project ../../src/...` against this config's own directory.
    cwd: APP_DLL ? dirname(APP_DLL) : undefined,
    url: `${BASE_URL}/health`,
    // Development, because /dev/login — the route that signs a browser in without a password — is
    // mapped only outside Production, and only for loopback callers.
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      APP_DATA_DIR: DATA_DIR,
      DOTNET_NOLOGO: '1',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    },
    // A cold `dotnet build` on a first run is most of this. A published app skips that entirely and
    // is up in a couple of seconds, so the long timeout only ever applies to the local shape.
    timeout: APP_DLL ? 60_000 : 240_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
