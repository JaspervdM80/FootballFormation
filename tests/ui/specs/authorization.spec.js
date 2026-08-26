// Reading is public; every change needs an admin. That split is the app's central rule — the squad,
// the fixtures and the statistics are meant to be shareable with parents, and nothing else is.
//
// These tests are about what a visitor *sees*, which is the first line of defence rather than the
// only one; the service-level guard behind it is covered by the C# suite. Both matter: a button
// that should not be there is a bug even when pressing it would be refused.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { clickFor, goto, gotoRendered } from '../helpers.js';
import { FIXTURE_MATCH } from '../global-setup.js';

test.describe('an anonymous visitor', () => {
  test.use({ storageState: VISITOR_STATE });

  test('can read the squad, the fixtures and the statistics', async ({ page }) => {
    // gotoRendered throughout: a visitor is offered no MudBlazor control on any of these, so there
    // is no handler for goto to wait on — /stats has no circuit at all, and on the other two every
    // control is behind AuthorizeView.
    for (const [path, heading] of [['/players', 'Squad'], ['/games', 'Games'], ['/stats', 'Statistics']]) {
      await gotoRendered(page, path);
      await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    }

    // Not an empty page that happens to have the right title: the seeded squad is on it.
    await gotoRendered(page, '/players');
    await expect(page.getByText('Fixture Keeper', { exact: false })).toBeVisible();
  });

  test('is offered nothing that would change anything', async ({ page }) => {
    await gotoRendered(page, '/players');
    await expect(page.getByRole('button', { name: 'Add Player' })).toHaveCount(0);

    await gotoRendered(page, '/games');
    await expect(page.getByRole('button', { name: 'Add', exact: true })).toHaveCount(0);
    await expect(page.getByTitle('Edit', { exact: false })).toHaveCount(0);
    await expect(page.getByTitle('Delete', { exact: false })).toHaveCount(0);

    // Trainings are admin-only outright, so the menu must not offer a link that would only bounce
    // the visitor to the login page. Both renderings of the menu — app bar and drawer — are on the
    // page at once, so a count of zero covers each.
    await expect(page.getByRole('link', { name: 'Trainings', exact: false })).toHaveCount(0);
  });

  test('is not shown the playing-time table on the season statistics', async ({ page }) => {
    await gotoRendered(page, '/stats');

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
    for (const path of ['/settings', '/users', '/stats/positions', '/trainings']) {
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
    // Nothing here is a MudBlazor control, so there is no handler to wait on and the click has to
    // carry its own proof that it landed — which is exactly what clickFor is for.
    await gotoRendered(page, '/games');
    const seeded = page.locator('.game-row', { hasText: FIXTURE_MATCH }).first();

    await clickFor(
      seeded.getByTitle('Overview', { exact: false }),
      options => expect(page).toHaveURL(/\/games\/\d+\/overview/, options));
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

  test('is shown the playing-time table a visitor is not', async ({ page }) => {
    await gotoRendered(page, '/stats');
    await expect(page.getByText('Top scorers', { exact: false })).toBeVisible();
    await expect(page.getByText('Playing time', { exact: false })).toBeVisible();
  });

  test('reaches the admin-only routes directly', async ({ page }) => {
    // /stats/positions carries its only handler on a MudTable, which binds no `_bl_` — see
    // gotoRendered. It is still an admin-only route, which is what this test is about.
    for (const [path, heading, open] of [
      ['/settings', 'Match Preferences', goto],
      ['/users', 'Users', goto],
      ['/stats/positions', 'Position Development', gotoRendered],
      ['/trainings', 'Trainings', goto],
    ]) {
      await open(page, path);
      await expect(page).toHaveURL(new RegExp(`${path}$`));
      await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    }
  });

  test('is offered the position development grid from the season statistics', async ({ page }) => {
    await gotoRendered(page, '/stats');
    await page.getByRole('link', { name: 'Position Development' }).click();
    await expect(page).toHaveURL(/\/stats\/positions$/);
  });
});
