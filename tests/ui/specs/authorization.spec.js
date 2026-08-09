// Reading is public; every change needs an admin. That split is the app's central rule — the squad,
// the fixtures and the statistics are meant to be shareable with parents, and nothing else is.
//
// These tests are about what a visitor *sees*, which is the first line of defence rather than the
// only one; the service-level guard behind it is covered by the C# suite. Both matter: a button
// that should not be there is a bug even when pressing it would be refused.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { goto } from '../helpers.js';
import { FIXTURE_MATCH } from '../global-setup.js';

test.describe('an anonymous visitor', () => {
  test.use({ storageState: VISITOR_STATE });

  test('can read the squad, the fixtures and the statistics', async ({ page }) => {
    for (const [path, heading] of [['/players', 'Squad'], ['/games', 'Games'], ['/stats', 'Statistics']]) {
      await goto(page, path);
      await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    }

    // Not an empty page that happens to have the right title: the seeded squad is on it.
    await goto(page, '/players');
    await expect(page.getByText('Fixture Keeper', { exact: false })).toBeVisible();
  });

  test('is offered nothing that would change anything', async ({ page }) => {
    await goto(page, '/players');
    await expect(page.getByRole('button', { name: 'Add Player' })).toHaveCount(0);

    await goto(page, '/games');
    await expect(page.getByRole('button', { name: 'Add', exact: true })).toHaveCount(0);
    await expect(page.getByTitle('Edit', { exact: false })).toHaveCount(0);
    await expect(page.getByTitle('Delete', { exact: false })).toHaveCount(0);
  });

  test('is sent to the login page by an admin-only route', async ({ page }) => {
    for (const path of ['/settings', '/users']) {
      await page.goto(path, { waitUntil: 'domcontentloaded' });
      await page.waitForURL(/\/login/, { timeout: 15_000 });

      // The route it was after is carried along, so signing in lands where it was going. Two things
      // in this app can do the redirecting — the cookie middleware, which spells it ReturnUrl, and
      // Blazor's RedirectToLogin, which spells it returnUrl — and the visitor does not care which.
      const query = new URL(page.url()).searchParams;
      const returnUrl = query.get('ReturnUrl') ?? query.get('returnUrl');
      expect(returnUrl, `${path} should be remembered across the login`).toContain(path);
    }
  });

  test('can still open a match report, which is the point of sharing one', async ({ page }) => {
    await goto(page, '/games');
    const seeded = page.locator('.game-row', { hasText: FIXTURE_MATCH }).first();

    await seeded.getByTitle('Overview', { exact: false }).click();
    await expect(page).toHaveURL(/\/games\/\d+\/overview/);
    await expect(page.getByText(FIXTURE_MATCH, { exact: false }).first()).toBeVisible();
  });
});

test.describe('an admin', () => {
  test('is offered the controls a visitor is not', async ({ page }) => {
    await goto(page, '/players');
    await expect(page.getByRole('button', { name: 'Add Player' })).toBeVisible();

    await goto(page, '/games');
    await expect(page.getByRole('button', { name: 'Add', exact: true }).first()).toBeVisible();
  });

  test('reaches the admin-only routes directly', async ({ page }) => {
    for (const [path, heading] of [['/settings', 'Match Preferences'], ['/users', 'Users']]) {
      await goto(page, path);
      await expect(page).toHaveURL(new RegExp(`${path}$`));
      await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    }
  });
});
