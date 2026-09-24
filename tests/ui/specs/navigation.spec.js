// The back arrow through back.js. The static page whose fallback is not where it came from is in trainings.spec.js.
import { test, expect } from '../fixtures.js';
import { createMatch, gameRow, goto, gotoRendered } from '../helpers.js';

const backArrow = (page) => page.locator('a.back-button').first();

test('an interactive page goes back to where the tab came from, not to its fallback', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Kruimelpad' });
  await gameRow(page, 'FC Kruimelpad').locator('.game-opponent').click();
  await expect(page).toHaveURL(/\/games\/\d+\/formation/);
  const formation = page.url();

  await goto(page, '/players');
  await page.locator('.topbar-nav a[href="/stats"]').first().click();
  // The URL changes before the page arrives, and a page left before it arrived is not one to go back to.
  await expect(page.locator('[data-page-path="/stats"]')).toBeAttached();
  await goto(page, formation);

  await expect(backArrow(page)).toHaveAttribute('href', '/games');
  await backArrow(page).hover();
  await expect(backArrow(page)).toHaveAttribute('title', 'Back to Season');

  await backArrow(page).click();
  await expect(page).toHaveURL(/\/stats$/);
});

test('a page the app cannot name is stepped past', async ({ page }) => {
  await gotoRendered(page, '/games');
  await gotoRendered(page, '/login');
  await gotoRendered(page, '/stats/positions');

  await backArrow(page).click();
  await expect(page).toHaveURL(/\/games$/);
});

test('a page opened cold falls back, having nothing behind it', async ({ page }) => {
  await gotoRendered(page, '/stats/positions');

  await expect(backArrow(page)).toHaveAttribute('title', 'Back to Season');
  await backArrow(page).click();
  await expect(page).toHaveURL(/\/stats$/);
});
