// The app is Dutch for the people who use it, and English is there for everyone else.
//
// This is the one spec that does not run in the pinned English of the rest of the suite: it starts
// from no culture cookie at all, which is what a parent following a shared link actually gets.
import { test, expect } from '../fixtures.js';
import { goto, gotoRendered } from '../helpers.js';

// No storage state at all — not even the language cookie the other specs carry.
test.use({ storageState: { cookies: [], origins: [] } });

test('a first visit is in Dutch', async ({ page }) => {
  // No storage state at all, so no admin: every control on /players is behind AuthorizeView and
  // there is no handler to wait on — see gotoRendered.
  await gotoRendered(page, '/players');

  await expect(page.locator('html')).toHaveAttribute('lang', 'nl');
  await expect(page.getByRole('heading', { name: 'Selectie', exact: false }).first()).toBeVisible();
  // The app-bar sections, in the language the club speaks.
  await expect(page.locator('.mud-appbar').getByText('Wedstrijden', { exact: false }).first()).toBeVisible();
});

test('the language switcher moves the whole app to English and it sticks', async ({ page }) => {
  await gotoRendered(page, '/players');

  // A <details> disclosure of plain links: opening it is a local toggle the browser does itself,
  // and choosing a language is a navigation to /culture/set rather than a click to retry.
  const english = page.locator('.language-picker-menu').getByText('English', { exact: true });
  await page.locator('.language-picker > summary').click();
  await expect(english).toBeVisible();
  await english.click();

  // Switching reloads the page: the circuit's culture is fixed when it starts, so the cookie only
  // takes effect on a fresh load.
  await expect(page.locator('html')).toHaveAttribute('lang', 'en', { timeout: 20_000 });
  await expect(page.getByRole('heading', { name: 'Squad', exact: false }).first()).toBeVisible();

  // And the choice survives going somewhere else, because it is a cookie rather than page state.
  await gotoRendered(page, '/games');
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await expect(page.getByRole('heading', { name: 'Games', exact: false }).first()).toBeVisible();
});

test('a missing Dutch translation falls back to the English key rather than blanking', async ({ page }) => {
  // Only Strings.nl.resx exists — English *is* the key — so nothing should ever render empty.
  await gotoRendered(page, '/stats');

  const heading = page.getByRole('heading').first();
  await expect(heading).not.toHaveText('');
});
