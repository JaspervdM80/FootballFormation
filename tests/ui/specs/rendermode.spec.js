// Which pages open a SignalR circuit, and which deliberately do not.
//
// This is the assertion the render-mode split exists for. A statistics page with no circuit can
// never show "Reconnecting…", never force a reload, and survives a phone suspending the app,
// because there is no socket to lose — see docs/known_issues/blazor-mudblazor.md. None of that is visible from
// looking at the page: it works either way, only worse.
//
// Signed out on purpose. The parents watching from the touchline are most of the traffic and never
// sign in, so theirs is the session that has to stay cheap.
//
// gotoRendered throughout, never goto: a visitor is offered no MudBlazor control on any of these,
// so there is no `_bl_` for goto to wait on. What it waits for instead — the page to stop fetching
// — is past the point where a circuit would have negotiated, which is what makes an empty list of
// sockets an absence rather than a race.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { gotoRendered } from '../helpers.js';

test.use({ storageState: VISITOR_STATE });

/** Records every WebSocket the page opens, from before the first navigation. */
function watchSockets(page) {
  const sockets = [];
  page.on('websocket', ws => sockets.push(ws.url()));
  return sockets;
}

test('the season statistics open no circuit at all', async ({ page }) => {
  const sockets = watchSockets(page);

  await gotoRendered(page, '/stats');
  await expect(page.getByRole('heading', { name: 'Statistics', exact: false }).first()).toBeVisible();
  expect(sockets, 'the season statistics opened a circuit').toEqual([]);

  // Not a listener that never fires: /games is still interactive, so the same probe on the same
  // page object has to see one there. Without this the assertion above would keep passing after a
  // rename broke the listener entirely.
  await gotoRendered(page, '/games');
  await expect(page.getByRole('heading', { name: 'Games', exact: false }).first()).toBeVisible();
  expect(sockets.length, 'no circuit on /games either — is the probe working?').toBeGreaterThan(0);
});

test('a player page opens no circuit either', async ({ page }) => {
  const sockets = watchSockets(page);

  await gotoRendered(page, '/players');
  // The name, because that is the anchor — the row itself carries no handler. A row click used to,
  // and dispatching it re-rendered the table on the way out, which is what conjured MudTable's
  // small-devices sort select and left its popover reaching for a provider this page has not got.
  await page.locator('.players-table .player-name-cell').first().click();
  await expect(page).toHaveURL(/\/players\/\d+\/stats/);
  const playerPath = new URL(page.url()).pathname;

  // Reached cold, the way a shared link is. Arriving from /players would carry that page's circuit
  // into the count and prove nothing.
  const fresh = await page.context().newPage();
  const freshSockets = watchSockets(fresh);
  await gotoRendered(fresh, playerPath);
  await expect(fresh.getByRole('heading').first()).toBeVisible();
  expect(freshSockets, 'the player statistics opened a circuit').toEqual([]);
  await fresh.close();
});

test('a shared match report opens no circuit', async ({ page }) => {
  // The URL first, from the games list, because a match report is only ever reached by its link.
  await gotoRendered(page, '/games');
  await page.locator('.game-row .action-btn[title="Overview"]').first().click();
  await expect(page).toHaveURL(/\/games\/\d+\/overview/);
  const overviewPath = new URL(page.url()).pathname;

  // Then cold, which is how a link shared into a group chat is opened.
  const shared = await page.context().newPage();
  const sockets = watchSockets(shared);
  await gotoRendered(shared, overviewPath);
  await expect(shared.getByRole('button', { name: 'Save as image', exact: false })).toBeVisible();
  expect(sockets, 'the match report opened a circuit').toEqual([]);
  await shared.close();
});
