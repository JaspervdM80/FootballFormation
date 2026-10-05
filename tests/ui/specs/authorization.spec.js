// Reading is public; every change needs an admin. That split is the app's central rule — the squad,
// the fixtures and the statistics are meant to be shareable with parents, and nothing else is.
//
// These tests are about what a visitor *sees*, which is the first line of defence rather than the
// only one; the service-level guard behind it is covered by the C# suite. Both matter: a button
// that should not be there is a bug even when pressing it would be refused.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { createMatch, goto } from '../helpers.js';

test.describe('an anonymous visitor', () => {
  test.use({ storageState: VISITOR_STATE });

  test('can read the squad, the fixtures and the statistics', async ({ page }) => {
    for (const [path, heading] of [['/games', 'Games'], ['/stats', 'Statistics'], ['/players', 'Squad']]) {
      await goto(page, path);
      await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    }

    // Not an empty page that happens to have the right title: the seeded squad is on it.
    await expect(page.getByText('Fixture Keeper', { exact: false })).toBeVisible();
  });

  test('is offered nothing that would change anything', async ({ page }) => {
    await goto(page, '/players');
    await expect(page.getByRole('button', { name: 'Add Player' })).toHaveCount(0);

    await goto(page, '/games');
    await expect(page.getByRole('button', { name: 'Add', exact: true })).toHaveCount(0);
    // Edit and Delete live in each card's menu, so the menu has to be opened for their absence to mean anything.
    const row = page.locator('.game-row').first();
    await row.locator('.game-more-static > summary').click();
    await expect(row.locator('.game-more-item', { hasText: 'Overview' })).toBeVisible();
    await expect(row.locator('.game-more-item', { hasText: 'Edit' })).toHaveCount(0);
    await expect(row.locator('.game-more-item', { hasText: 'Delete' })).toHaveCount(0);

    // Trainings are admin-only outright, so the menu must not offer a link that would only bounce
    // the visitor to the login page. Both renderings of the menu — app bar and drawer — are on the
    // page at once, so a count of zero covers each.
    await expect(page.getByRole('link', { name: 'Trainings', exact: false })).toHaveCount(0);
  });

  test('is not greeted on the start page as the coach', async ({ page }) => {
    await goto(page, '/');

    await expect(page.getByRole('heading', { name: 'GJS MO15-2', exact: false })).toBeVisible();
    await expect(page.getByText('Plan your team\'s formations', { exact: false })).toHaveCount(0);
  });

  test('reaches the settings for the language, and is offered none of the admin sections', async ({ page }) => {
    await goto(page, '/settings');

    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.locator('.settings-language-option')).toHaveCount(2);

    // Not the notification section: it is drawn only where the browser can do push, which is not
    // every runner — see docs/known_issues/touch-pwa.md. The three admin sections are the rest.
    for (const heading of ['Season Settings', 'Training Settings', 'Account']) {
      await expect(page.getByRole('heading', { name: heading, exact: true })).toHaveCount(0);
    }
  });

  test('is not shown the playing-time table on the season statistics', async ({ page }) => {
    await goto(page, '/stats');

    // The card next to it, so the absence below is a rule rather than a page that failed to render.
    await expect(page.getByText('Top scorers', { exact: false })).toBeVisible();
    await expect(page.getByText('Playing time', { exact: false })).toHaveCount(0);

    // Goalkeeper minutes are the one deliberate exception — who kept goal, and for how long, is
    // what the squad asks about. If that ever changes, this line is the one to delete.
    // The card's own heading, not the text: its empty state says "No goalkeeper minutes yet" and
    // matches a loose search too.
    await expect(page.locator('.card-label', { hasText: 'Goalkeeper minutes' })).toBeVisible();
  });

  test('is sent to the login page by an admin-only route', async ({ page }) => {
    for (const path of ['/preferences', '/users', '/teams', '/stats/positions', '/trainings']) {
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
});

test.describe('an admin', () => {
  test('is shown the playing-time table a visitor is not', async ({ page }) => {
    await goto(page, '/stats');
    await expect(page.getByText('Top scorers', { exact: false })).toBeVisible();
    await expect(page.getByText('Playing time', { exact: false })).toBeVisible();
  });

  test('is offered the admin-only shortcuts on the start page', async ({ page }) => {
    await goto(page, '/');
    await expect(page.locator('.home-tile-link', { hasText: 'Trainings' })).toBeVisible();
    await expect(page.locator('.home-tile-link', { hasText: 'Preferences' })).toBeVisible();
  });

  test('opens the next match on the start page at its lineup, where a visitor gets the overview', async ({ page, visitor }) => {
    // A fixture of its own, so there is a next match even when the seeded one falls on today.
    await createMatch(page, { opponent: 'FC Startpagina' });

    await goto(page, '/');
    await expect(page.locator('.home-fixture')).toHaveAttribute('href', /\/games\/\d+\/formation$/);

    await goto(visitor, '/');
    await expect(visitor.locator('.home-fixture')).toHaveAttribute('href', /\/games\/\d+\/overview$/);
  });

  test('is offered the position development grid from the season statistics', async ({ page }) => {
    await goto(page, '/stats');
    await page.getByRole('link', { name: 'Position Development' }).click();
    await expect(page).toHaveURL(/\/stats\/positions$/);
  });
});
