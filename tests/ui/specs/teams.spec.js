// Which team the app is showing is a cookie, and everything that reads it — the app bar, the page
// heading, the manifest — reads it off the request that created the scope. None of that is visible
// from one page view, which is why it needs a browser: break the cookie and the app still works, it
// just quietly shows the wrong team, or forgets which one you picked.
import { test, expect } from '../fixtures.js';
import { goto, gotoRendered, openDialog, submitDialog, confirmDialog, fillField } from '../helpers.js';

const COOKIE = 'ff.team';
const A_YEAR = 365 * 24 * 60 * 60;

const SECOND_TEAM = 'MO17-Uitest';
const SEEDED_TEAM = 'GJS MO15-2';

const teamRow = (page, name) => page.locator('.list-row', { hasText: name });

const heading = (page, name) => page.getByRole('heading', { name, exact: false }).first();

/** Adds the second team this file needs, unless a previous test in the file already did. */
async function ensureSecondTeam(page) {
  await goto(page, '/teams');
  if (await teamRow(page, SECOND_TEAM).count() > 0) return;

  await page.locator('.teams-head .action-add').click();
  const dialog = await openDialog(page);
  // The field is labelled "Team" — the dialog names the thing, not the property.
  await fillField(dialog, 'Team', SECOND_TEAM);
  await submitDialog(page);

  await expect(teamRow(page, SECOND_TEAM)).toHaveCount(1);
}

/**
 * Switching team is a link to /team/set and a real page load, not a click to retry — pressing again
 * while the first navigation is in flight is what clickFor would do.
 */
async function showTeam(page, name) {
  await teamRow(page, name).locator('a[href^="/team/set"]').click();
  await page.waitForURL(url => !url.pathname.startsWith('/team/set'));
}

const cookie = async page => (await page.context().cookies()).find(c => c.name === COOKIE);

test('a visit is remembered as the team it was about, even without choosing one', async ({ page }) => {
  await goto(page, '/games');

  // Nobody has picked anything, so this is the fallback being written down: the point is that the
  // next visit reads a cookie rather than resolving the first team all over again.
  expect(await cookie(page), `no ${COOKIE} cookie — the visit is not being remembered at all`)
    .toBeTruthy();
});

test('choosing a team moves the app onto it, and keeps it there', async ({ page }) => {
  await ensureSecondTeam(page);

  // Proves the switch is telling us something: the app does not start on the second team.
  await gotoRendered(page, '/');
  await expect(heading(page, SEEDED_TEAM)).toBeVisible();

  await goto(page, '/teams');
  await showTeam(page, SECOND_TEAM);

  // The badge is the page's own answer; the heading on / is the chrome's, read off a later request.
  await expect(teamRow(page, SECOND_TEAM)).toContainText('Selected');

  await gotoRendered(page, '/');
  await expect(heading(page, `GJS ${SECOND_TEAM}`)).toBeVisible();
});

test('the choice is written as a cookie that outlasts the season', async ({ page }) => {
  await ensureSecondTeam(page);
  await showTeam(page, SECOND_TEAM);

  const stored = await cookie(page);
  expect(stored).toBeTruthy();
  expect(stored.path).toBe('/');

  // A year, where the season cookie gets eight hours: which team you follow is not a match-day
  // choice. A wrong lifetime is invisible in a browser — the choice sticks either way — so the
  // number itself is what gets asserted.
  const days = (stored.expires - Date.now() / 1000) / 86_400;
  expect(days, 'the cookie should expire in about a year').toBeGreaterThan(364);
  expect(stored.expires - Date.now() / 1000).toBeLessThanOrEqual(A_YEAR);
});

test('the team the app falls back to cannot be deleted, however many others there are', async ({ page }) => {
  await ensureSecondTeam(page);

  // Every visitor who has never chosen lands on it, so deleting it would move the whole app — its
  // title, its crest and its manifest — while every season and game stayed where it was.
  await teamRow(page, 'MO15-2').first().locator('.action-delete').click();
  await confirmDialog(page, 'Delete');

  await expect(page.locator('.mud-snackbar-content-message', { hasText: 'the team the app is showing' }))
    .toBeVisible();
  await expect(teamRow(page, 'MO15-2')).toHaveCount(1);
});
