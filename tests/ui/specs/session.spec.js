// Staying signed in — the part of authentication that only a real browser can answer for.
//
// Everything here is about the cookie itself rather than what it unlocks: whether the browser is
// told to keep it, and whether it agrees to send it back. Both were wrong in ways no C# test could
// see, because both are decisions the *browser* makes from the attributes on the Set-Cookie header.
import { test, expect } from '../fixtures.js';
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import { ADMIN_PASSWORD, ADMIN_USERNAME } from '../global-setup.js';
import { goto } from '../helpers.js';

const AUTH_COOKIE = 'ff.auth';

/**
 * Signs in through the form a person actually uses — /dev/login mints the same principal, but only
 * /auth/login issues the cookie this file is about.
 *
 * `goto`, not `page.goto`, even though the form itself is plain `method="post"` HTML that needs no
 * circuit to submit: the page *around* it is a Blazor component, and the render that arrives when
 * the circuit connects resets the inputs. Filling the prerender and submitting therefore posts two
 * empty strings, the app answers `/login?error=true`, and the sign-in silently does nothing —
 * intermittently, because whether the re-render lands between the fill and the click depends on how
 * busy the machine is. Waiting for handlers puts the typing after that render instead.
 */
async function signInThroughTheForm(page) {
  await goto(page, '/login');
  await page.fill('input[name="username"]', ADMIN_USERNAME);
  await page.fill('input[name="password"]', ADMIN_PASSWORD);
  await page.click('button[type="submit"]');

  // Asserted rather than waited on: a refused sign-in comes back to /login?error=true, which is
  // still /login, so `waitForURL` would spend its whole timeout and then report a navigation that
  // never happened instead of the credentials that were rejected.
  await expect(page).not.toHaveURL(/\/login/, { timeout: 20_000 });
}

test.describe('signing in', () => {
  test.use({ storageState: VISITOR_STATE });

  test('leaves a cookie the browser will keep after it closes', async ({ page, context }) => {
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
  });

  test('leaves a cookie that survives arriving from another site', async ({ page, context }) => {
    await signInThroughTheForm(page);

    // Strict is the value that fails this: it withholds the cookie on any cross-site navigation,
    // including the plain link click below.
    const cookie = (await context.cookies(BASE_URL)).find(c => c.name === AUTH_COOKIE);
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
      body: `<a href="${BASE_URL}/settings">Match preferences</a>`,
    }));

    await page.goto('http://link.example/', { waitUntil: 'domcontentloaded' });
    await page.click('a');

    // /settings is admin-only, so landing on it *is* the assertion that the cookie came along. With
    // the cookie withheld this redirects to /login instead.
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('heading', { name: 'Match Preferences', exact: false }).first())
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
