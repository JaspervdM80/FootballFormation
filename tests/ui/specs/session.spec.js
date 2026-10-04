// Staying signed in — the parts of authentication only a real browser can answer for.
//
// Two things live here. The cookie's own attributes, which decide whether the browser keeps it and
// whether it agrees to send it back; and the circuit's revalidation loop, which is what takes
// authority away from someone who is already looking at the app. No C# test can see either: the
// first is the browser reading Set-Cookie, and the second only happens over a live SignalR circuit.
import { test, expect } from '../fixtures.js';
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import { ADMIN_PASSWORD, ADMIN_USERNAME } from '../global-setup.js';
import { clickFor, confirmDialog, fillField, goto, openDialog, settle, submitDialog } from '../helpers.js';

const AUTH_COOKIE = 'ff.auth';

/** One account's row in the users table. */
const userRow = (page, username) =>
  page.locator('.users-table .mud-table-body .mud-table-row', { hasText: username }).first();

/**
 * Signs in through the form a person actually uses — /dev/login mints the same principal, but only
 * /auth/login issues the cookie this file is about.
 */
async function signInThroughTheForm(page, username = ADMIN_USERNAME, password = ADMIN_PASSWORD) {
  await goto(page, '/login');
  await page.fill('input[name="username"]', username);
  await page.fill('input[name="password"]', password);
  await page.click('button[type="submit"]');

  // Asserted rather than waited on: a refused sign-in comes back to /login?error=true, which is
  // still /login, so `waitForURL` would spend its whole timeout and then report a navigation that
  // never happened instead of the credentials that were rejected.
  await expect(page).not.toHaveURL(/\/login/, { timeout: 20_000 });
  // The start page it lands on opens a circuit, and leaving mid-handshake logs "Failed to complete negotiation".
  await settle(page);
}

test.describe('signing in', () => {
  test.use({ storageState: VISITOR_STATE });

  test('leaves a cookie the browser keeps after it closes, and sends back from another site', async ({ page, context }) => {
    await signInThroughTheForm(page);

    const cookie = (await context.cookies(BASE_URL)).find(c => c.name === AUTH_COOKIE);
    expect(cookie, 'signing in should set the auth cookie').toBeDefined();

    // -1 is Playwright for "session cookie" — no Expires on the header, so the browser is entitled
    // to drop it the moment it decides the session ended. That is what it used to be, and on a
    // phone reclaiming a backgrounded tab it meant signing in again on the touchline.
    expect(cookie.expires, 'a session cookie does not survive the browser closing').toBeGreaterThan(0);

    const daysFromNow = (cookie.expires * 1000 - Date.now()) / 86_400_000;
    expect(daysFromNow).toBeGreaterThan(13);
    expect(daysFromNow).toBeLessThan(15);

    expect(cookie.httpOnly, 'script must not be able to read it').toBe(true);

    // Strict is the value that fails this: it withholds the cookie on any cross-site navigation,
    // including the plain link click the next test follows.
    expect(cookie.sameSite).toBe('Lax');
  });
});

test.describe('an admin who is already signed in', () => {
  test('is still signed in when following a link from another site', async ({ page }) => {
    // A stand-in for WhatsApp, an email or a search result: a real top-level navigation from a
    // different site, which is the case SameSite governs. The origin is fulfilled in the browser,
    // so nothing leaves it and no DNS name has to exist.
    await page.route('http://link.example/', route => route.fulfill({
      contentType: 'text/html',
      body: `<a href="${BASE_URL}/preferences">Match preferences</a>`,
    }));

    await page.goto('http://link.example/', { waitUntil: 'domcontentloaded' });
    await page.click('a');

    // /preferences is admin-only, so landing on it *is* the assertion that the cookie came along.
    // With the cookie withheld this redirects to /login instead.
    await expect(page).toHaveURL(/\/preferences$/);
    await expect(page.getByRole('heading', { name: 'Match Preferences', exact: false }).first())
      .toBeVisible();
  });
});

/** Creates an admin account through the real dialog and returns what it can sign in with. */
async function addAdmin(page, name) {
  // Named per attempt, not per test: Playwright's CI retry re-runs this against the database the
  // failed attempt left behind, and a username is unique — a fixed one would fail the retry on
  // "already exists" rather than on whatever went wrong. See docs/known_issues/authentication.md.
  const username = `${name}-${Date.now()}`;
  const password = `${name}-admin-1`;

  await goto(page, '/users');
  await clickFor(
    page.getByRole('button', { name: 'Add User' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible());

  const dialog = await openDialog(page);
  await fillField(dialog, 'Name', `${name} admin`);
  await fillField(dialog, 'Username', username);
  await dialog.getByLabel('Password', { exact: false }).first().fill(password);
  await dialog.getByLabel('Confirm password', { exact: false }).first().fill(password);
  await submitDialog(page);
  await expect(userRow(page, username)).toBeVisible();

  return { username, password };
}

// The circuit half of revocation. A Blazor Server tab makes almost no HTTP requests after its first
// page load, so `OnValidatePrincipal` — which runs per request — is not what takes authority away
// from someone already looking at the app. That is the revalidation loop, and this is the only place
// it is exercised: `Auth__RevalidationIntervalSeconds` is two seconds here against five minutes in
// production (see playwright.config.js).
test.describe('an account revoked while its owner is looking at the app', () => {
  test('loses its authority without anyone reloading anything', async ({ page, visitor: theirPage }) => {
    const { username, password } = await addAdmin(page, 'revoked');
    // A second admin on the team, or the delete below is refused for leaving the team without one.
    await addAdmin(page, 'stays');

    // A second browser, signed in as that account and sitting on an admin page.
    await signInThroughTheForm(theirPage, username, password);

    await goto(theirPage, '/users');
    await expect(theirPage.getByRole('heading', { name: 'Users', exact: false }).first()).toBeVisible();

    // Delete the account from the first browser. Nothing in the second one makes a request
    // through any of this — its circuit is open and idle, which is the whole scenario.
    await goto(page, '/users');
    const menu = userRow(page, username).locator('.mud-menu button').first();
    const entry = page.locator('.mud-popover-open').getByText('Delete User', { exact: true });
    await clickFor(menu, () => expect(entry).toBeVisible());
    await entry.click();
    await confirmDialog(page, 'Delete');
    await expect(userRow(page, username)).toHaveCount(0);

    // The circuit notices on its own and RedirectToLogin force-loads — which is also the request
    // that finally clears the cookie, since a circuit has no response to clear it on.
    await theirPage.waitForURL(/\/login/, { timeout: 30_000 });
  });
});

// global-setup.js leans on this to get the suite started at all: it changes the seeded admin's
// password and has to sign in again afterwards, or the state it saves is an anonymous one. Asserted
// here on purpose, because a regression would otherwise surface as every spec in the directory going
// red at once with a message about none of this.
test.describe('an admin who changes their own password', () => {
  test('is signed out of the session that changed it, and back in with the new one', async ({ page, visitor: theirPage }) => {
    const { username, password } = await addAdmin(page, 'rotated');
    const replacement = `${password}-2`;

    await signInThroughTheForm(theirPage, username, password);
    await goto(theirPage, '/settings');

    const fields = theirPage.locator('input[type="password"]');
    await fields.nth(0).fill(password);
    await fields.nth(1).fill(replacement);
    await fields.nth(2).fill(replacement);

    // Clicked exactly once, deliberately: a second attempt would be made with a password that is
    // no longer the current one. And waited on the navigation rather than on the form clearing —
    // the re-render lands before the cookie is dropped, and signing in on that signal starts a
    // navigation while the circuit's own is still in flight.
    await theirPage.getByRole('button', { name: 'Change password', exact: false }).click();
    await theirPage.waitForURL(/\/login/, { timeout: 30_000 });

    await signInThroughTheForm(theirPage, username, replacement);
    await goto(theirPage, '/settings');
    // The password card, not the page: /settings is open to visitors too, and only the card
    // behind its AuthorizeView says the new password signed anyone in.
    await expect(theirPage.getByRole('button', { name: 'Change password', exact: false }))
      .toBeVisible();
  });
});

test.describe('an anonymous visitor', () => {
  test.use({ storageState: VISITOR_STATE });

  test('carries no auth cookie at all', async ({ page, context }) => {
    await goto(page, '/players');

    const cookie = (await context.cookies(BASE_URL)).find(c => c.name === AUTH_COOKIE);
    expect(cookie, 'reading is public and should mint nothing').toBeUndefined();
  });
});
