// Which team the app is showing is a cookie, and everything that reads it — the app bar, the page
// heading, the manifest — reads it off the request that created the scope. None of that is visible
// from one page view, which is why it needs a browser: break the cookie and the app still works, it
// just quietly shows the wrong team, or forgets which one you picked.
import { test, expect } from '../fixtures.js';
import {
  addAdmin, addTeam, clickFor, confirmDialog, dropAndRejoin, goto, settle, showTeam, signInAs, teamRow,
} from '../helpers.js';

const COOKIE = 'ff.team';
const A_YEAR = 365 * 24 * 60 * 60;

const SECOND_TEAM = 'MO17-Uitest';
const SEEDED_TEAM = 'GJS MO15-2';

const heading = (page, name) => page.getByRole('heading', { name, exact: false }).first();

/** Adds the second team this file needs, unless a previous test in the file already did. */
const ensureSecondTeam = page => addTeam(page, SECOND_TEAM);

const cookie = async page => (await page.context().cookies()).find(c => c.name === COOKIE);

test('a visit is remembered as the team it was about, even without choosing one', async ({ page }) => {
  // The saved state already carries one from global setup's first request, and this visit has to write its own.
  await page.context().clearCookies({ name: COOKIE });
  await goto(page, '/games');

  // Nobody has picked anything, so this is the fallback being written down: the point is that the
  // next visit reads a cookie rather than resolving the first team all over again.
  expect(await cookie(page), `no ${COOKIE} cookie — the visit is not being remembered at all`)
    .toBeTruthy();
});

test('choosing a team moves the app onto it, and keeps it there', async ({ page }) => {
  await ensureSecondTeam(page);

  // Proves the switch is telling us something: the app does not start on the second team.
  await goto(page, '/');
  await expect(heading(page, SEEDED_TEAM)).toBeVisible();

  await goto(page, '/teams');
  await showTeam(page, SECOND_TEAM);

  // The badge is the page's own answer; the heading on / is the chrome's, read off a later request.
  await expect(teamRow(page, SECOND_TEAM)).toContainText('Selected');

  await goto(page, '/');
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

const appTitle = page => page.locator('.mud-appbar .app-title-text');

/** The absolute link /teams copies for a team's parents — the text the copy button hands the clipboard. */
async function teamLink(page, name) {
  await goto(page, '/teams');
  return (await teamRow(page, name).locator('[id^="team-link-"]').textContent()).trim();
}

/** A real page load through the team link, so the choice is clicked once and waited for rather than retried. */
async function pickTeamInDrawer(page, name) {
  const picker = page.locator('.app-drawer .team-picker > summary');
  await clickFor(page.locator('label.nav-hamburger'), () => expect(picker).toBeInViewport());
  await picker.click();
  await page.locator('.team-menu-item', { hasText: name }).click();
}

test('a visitor picks another team from the drawer and stays on the page they were on', async ({ page, visitor }) => {
  await ensureSecondTeam(page);

  await goto(visitor, '/games');
  await expect(appTitle(visitor)).toHaveText(SEEDED_TEAM);

  await pickTeamInDrawer(visitor, SECOND_TEAM);
  await visitor.waitForURL(url => url.pathname === '/games');

  await expect(appTitle(visitor)).toHaveText(`GJS ${SECOND_TEAM}`);
});

test('switching team from a page about one player starts over at home', async ({ page, visitor }) => {
  await ensureSecondTeam(page);

  // The other team cannot see this player, so staying put would land on its not-found page.
  await goto(visitor, '/players');
  const stats = await visitor.locator('a[href^="/players/"][href$="/stats"]').first().getAttribute('href');
  await goto(visitor, stats);

  await pickTeamInDrawer(visitor, SECOND_TEAM);

  await visitor.waitForURL(url => url.pathname === '/');
  await expect(appTitle(visitor)).toHaveText(`GJS ${SECOND_TEAM}`);
});

test('a team link opens the app on its team by club and team name, and a link to no team is not found', async ({ page, visitor }) => {
  await ensureSecondTeam(page);
  const link = await teamLink(page, SECOND_TEAM);
  expect(new URL(link).pathname).toBe(`/team/gjs/${SECOND_TEAM.toLowerCase()}`);

  await goto(visitor, '/');
  await expect(appTitle(visitor)).toHaveText(SEEDED_TEAM);

  await goto(visitor, new URL(link).pathname);
  expect(new URL(visitor.url()).pathname).toBe('/');
  await expect(appTitle(visitor)).toHaveText(`GJS ${SECOND_TEAM}`);

  // A link sent before its team was deleted must say so, not open whichever team comes first.
  const gone = await visitor.request.get('/team/999999', { maxRedirects: 0 });
  expect(gone.status()).toBe(404);
  const renamed = await visitor.request.get('/team/gjs/mo99-gone', { maxRedirects: 0 });
  expect(renamed.status()).toBe(404);

  for (const away of ['%2F%2Fexample.com', '%2F%09%2Fexample.com']) {
    const elsewhere = await visitor.request.get(`${new URL(link).pathname}?redirectUri=${away}`, { maxRedirects: 0 });
    expect(elsewhere.headers()['location']).toBe('/');
  }

  // The id form is what every link sent before the readable one carries.
  await goto(visitor, '/team/1');
  expect(new URL(visitor.url()).pathname).toBe('/');
  await expect(appTitle(visitor)).toHaveText(SEEDED_TEAM);
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

test('a first visit while there are two teams is asked which one, then sent on to where it was going', async ({ page, visitor }) => {
  await ensureSecondTeam(page);
  await visitor.context().clearCookies({ name: COOKIE });

  await goto(visitor, '/games');
  expect(new URL(visitor.url()).pathname).toBe('/team/choose');
  // No team to name yet, so no drawer offering the fallback the question is there to avoid.
  await expect(visitor.locator('label.nav-hamburger')).toHaveCount(0);

  await visitor.locator('a.choose-team', { hasText: SECOND_TEAM }).click();
  await visitor.waitForURL(url => url.pathname === '/games');
  await settle(visitor);

  await expect(appTitle(visitor)).toHaveText(`GJS ${SECOND_TEAM}`);
  await expect(visitor.locator('label.nav-hamburger')).toHaveCount(1);
});

test('an admin of one team is shown another as a visitor is, and keeps their own password', async ({ page, visitor: theirPage }) => {
  await ensureSecondTeam(page);
  const { username } = await addAdmin(page, 'oneteam');
  await signInAs(theirPage, username);

  // Their own team first, so the absences below are the team's doing and not the account's.
  const add = theirPage.getByRole('button', { name: 'Add', exact: true });
  await goto(theirPage, '/games');
  await expect(appTitle(theirPage)).toHaveText(SEEDED_TEAM);
  await expect(add.first()).toBeVisible();

  await pickTeamInDrawer(theirPage, SECOND_TEAM);
  await theirPage.waitForURL(url => url.pathname === '/games');
  await settle(theirPage);
  await expect(appTitle(theirPage)).toHaveText(`GJS ${SECOND_TEAM}`);
  await expect(add).toHaveCount(0);
  await expect(theirPage.locator('.logout-btn')).toBeVisible();

  // Home, not the login page: signing in again would change nothing.
  await theirPage.goto('/trainings');
  await theirPage.waitForURL(url => url.pathname === '/');
  await settle(theirPage);

  await goto(theirPage, '/settings');
  await expect(theirPage.getByRole('button', { name: 'Change password', exact: false })).toBeVisible();
  await expect(theirPage.getByRole('heading', { name: 'Season Settings', exact: true })).toHaveCount(0);
});

test('a circuit rejoining after another tab switched team still answers for its own team', async ({ page, visitor: theirPage }) => {
  await ensureSecondTeam(page);
  const link = await teamLink(page, SECOND_TEAM);
  const { username } = await addAdmin(page, 'rejoin');
  await signInAs(theirPage, username);

  const add = theirPage.getByRole('button', { name: 'Add', exact: true });
  await goto(theirPage, '/games');
  await expect(add.first()).toBeVisible();

  // The same browser opening a parent's link elsewhere: the cookie moves, and the rejoin is made with it.
  await theirPage.request.get(new URL(link).pathname);
  await dropAndRejoin(theirPage);

  await clickFor(add.first(), () => expect(theirPage.locator('.mud-dialog')).toBeVisible());
});
