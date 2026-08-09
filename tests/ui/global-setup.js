// Gets the app into the state a test can start from, once, before any spec runs.
//
// Three things have to happen here and they are all the app behaving correctly:
//
//   1. The language is pinned to English. The UI is Dutch by default and English is the
//      resource-key fallback, so pinning it means a selector is the same string as the source it
//      came from. localization.spec.js is the one place that opts out and checks the default.
//   2. A freshly seeded admin still holds the password it was created with, and that locks every
//      route to /settings until it changes. Skip this and every test navigates to the same page.
//   3. Changing a password rotates the account's security stamp, and OnValidatePrincipal rejects
//      the cookie issued before it — so it signs in again afterwards, or the saved state is an
//      anonymous one.
//
// It then seeds a small squad through the real dialogs, because a squad is the precondition for
// most of what follows and seeding it once is faster than each spec building its own. Specs that
// are *about* adding players add their own, named after themselves.
import { chromium, expect } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { ADMIN_STATE, BASE_URL, CHROMIUM_PATH, VISITOR_STATE } from './playwright.config.js';
import { addPlayer, createMatch, goto, waitForHandlers } from './helpers.js';

const SEED_PASSWORD = 'admin';
const NEW_PASSWORD = 'uitest-admin-1';

// Named so a failure screenshot says where they came from. Shirt numbers are high to stay clear of
// anything a spec invents.
export const FIXTURE_MATCH = 'Fixture United';

export const SQUAD = [
  { firstName: 'Fixture', surname: 'Keeper', shirt: 90 },
  { firstName: 'Fixture', surname: 'Defender', shirt: 91 },
  { firstName: 'Fixture', surname: 'Midfielder', shirt: 92 },
  { firstName: 'Fixture', surname: 'Striker', shirt: 93 },
];

export default async function globalSetup() {
  mkdirSync(dirname(ADMIN_STATE), { recursive: true });

  const browser = await chromium.launch({ executablePath: CHROMIUM_PATH });
  const context = await browser.newContext({ baseURL: BASE_URL });
  const page = await context.newPage();

  // The switcher's own endpoint, so the cookie is exactly the one the app would have set.
  await page.goto(`/culture/set?culture=en&redirectUri=${encodeURIComponent('/')}`);
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await context.storageState({ path: VISITOR_STATE });

  await signIn(page);
  await clearSeededPassword(page);

  for (const player of SQUAD) {
    await addPlayer(page, player);
  }

  // One match on file as well, so a spec that only reads — an anonymous visitor opening a match
  // report — has something to open without needing an admin to create it first.
  await createMatch(page, { opponent: FIXTURE_MATCH });

  await context.storageState({ path: ADMIN_STATE });
  await browser.close();
}

/** Development-only, loopback-only route that mints the same principal /auth/login does. */
async function signIn(page) {
  await page.goto('/dev/login', { waitUntil: 'domcontentloaded' });
  await page.waitForURL(url => !url.pathname.startsWith('/dev/login'), { timeout: 15_000 })
    .catch(() => { /* the route may render rather than redirect; the check below is the real one */ });
}

async function clearSeededPassword(page) {
  await goto(page, '/settings');

  const notice = page.getByText('still uses the password', { exact: false });
  if (!(await notice.isVisible().catch(() => false))) return;

  const fields = page.locator('input[type="password"]');
  // These three are on the first render of the page, so they are the one case that has to prove the
  // handlers are attached before typing — see waitForHandlers.
  await waitForHandlers(fields.first());
  await fields.nth(0).fill(SEED_PASSWORD);
  await fields.nth(1).fill(NEW_PASSWORD);
  await fields.nth(2).fill(NEW_PASSWORD);

  // Clicked exactly once, deliberately: changing a password is not idempotent, and a second attempt
  // would be made with a password that is no longer the current one. Everywhere else in this suite
  // a click can be retried; here the reload below is what confirms it worked.
  await page.getByRole('button', { name: 'Change password', exact: false }).click();
  await expect(notice).toBeHidden({ timeout: 20_000 });

  await signIn(page);
  await goto(page, '/settings');
  await expect(notice).toBeHidden();
}
