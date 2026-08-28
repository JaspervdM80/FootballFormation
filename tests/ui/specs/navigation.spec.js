// The back arrow, and the trail behind it.
//
// The trail is a cookie (`ff.trail`) written per page served, because Blazor's enhanced navigation
// sends the *destination* as the Referer — see docs/known_issues/blazor-components.md. That has two
// halves worth pinning from a browser, and neither is visible from a unit test: a page with no
// circuit follows the trail (trainings.spec.js has that one, from /trainings to a player), and a
// page inside an interactive island deliberately does not.
import { test, expect } from '../fixtures.js';
import { createMatch, gameRow, goto, gotoRendered } from '../helpers.js';

const backArrow = (page) => page.locator('a.back-button').first();

test('an interactive page takes its fallback, and does not change its mind once the circuit connects', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Kruimelpad' });

  // Two navigations inside one circuit is the whole point: the circuit is created on /players and
  // survives both, so the RequestContext it holds — and the trail on it — still describes /players.
  // Reading that would offer the squad from a match's formation builder.
  await goto(page, '/players');
  await page.locator('.topbar-nav a[href="/games"]').first().click();
  await expect(page).toHaveURL(/\/games$/);
  await gameRow(page, 'FC Kruimelpad').locator('.game-opponent').click();
  await expect(page).toHaveURL(/\/games\/\d+\/formation/);

  // Asserted twice with the settle in between, because the failure this guards against is a change
  // rather than a value: the prerender reads a fresh scope and the circuit's re-render a stale one.
  await expect(backArrow(page)).toHaveAttribute('href', '/games');
  await expect(backArrow(page)).toHaveAttribute('title', 'Back to Games');
  await expect(backArrow(page)).toHaveAttribute('href', '/games');
});

test('a page opened cold falls back, having nothing behind it', async ({ page, context }) => {
  // Cleared explicitly: `ff.trail` is a session cookie, so the signed-in state global-setup saved
  // carries whatever it was on when it was written. This is the bookmark case.
  await context.clearCookies({ name: 'ff.trail' });

  await gotoRendered(page, '/stats/positions');
  await expect(backArrow(page)).toHaveAttribute('href', '/stats');
});
