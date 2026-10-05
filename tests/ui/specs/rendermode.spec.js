// Which pages open a SignalR circuit, and which deliberately do not.
//
// This is the assertion the render-mode split exists for. A statistics page with no circuit can
// never show "Reconnecting…", never force a reload, and survives a phone suspending the app,
// because there is no socket to lose — see docs/known_issues/blazor-mudblazor.md. None of that is visible from
// looking at the page: it works either way, only worse.
//
// Signed out on purpose. The parents watching from the touchline are most of the traffic and never
// sign in, so theirs is the session that has to stay cheap.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { goto, openOverview } from '../helpers.js';
import { FIXTURE_MATCH } from '../global-setup.js';

test.use({ storageState: VISITOR_STATE });

/** Records every WebSocket the page opens, from before the first navigation. */
function watchSockets(page) {
  const sockets = [];
  page.on('websocket', ws => sockets.push(ws.url()));
  return sockets;
}

/** Opens a page and waits past where a circuit would have negotiated, so an empty socket list is an absence rather than a race. */
async function openSettled(page, path) {
  await goto(page, path);
  await page.waitForLoadState('networkidle');
}

test('the season statistics open no circuit at all', async ({ page }) => {
  const sockets = watchSockets(page);

  await openSettled(page, '/stats');
  await expect(page.getByRole('heading', { name: 'Statistics', exact: false }).first()).toBeVisible();
  expect(sockets, 'the season statistics opened a circuit').toEqual([]);

  // Not a listener that never fires: the home page is still interactive, so the same probe on the
  // same page object has to see one there. Without this the assertion above would keep passing
  // after a rename broke the listener entirely.
  await goto(page, '/');
  expect(sockets.length, 'no circuit on the home page either — is the probe working?').toBeGreaterThan(0);
});

// The fixture list and the squad are what a parent opens most; only an admin gets their island.
for (const [path, heading, content] of [['/games', 'Games', '.game-row'], ['/players', 'Squad', '.player-name-cell']]) {
  test(`${path} opens no circuit for a visitor`, async ({ page }) => {
    const sockets = watchSockets(page);

    await openSettled(page, path);
    await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    await expect(page.locator(content).first()).toBeVisible();
    expect(sockets, `${path} opened a circuit for a visitor`).toEqual([]);
  });
}

test('a player page opens no circuit either', async ({ page, visitor }) => {
  await goto(page, '/players');
  // The name, because that is the anchor — the row itself carries no handler. A row click used to,
  // and dispatching it re-rendered the table on the way out, which is what conjured MudTable's
  // small-devices sort select and left its popover reaching for a provider this page has not got.
  await page.locator('.players-table .player-name-cell').first().click();
  await expect(page).toHaveURL(/\/players\/\d+\/stats/);
  const playerPath = new URL(page.url()).pathname;

  // Reached cold, the way a shared link is, so nothing the list did can be counted against this page.
  const sockets = watchSockets(visitor);
  await openSettled(visitor, playerPath);
  await expect(visitor.getByRole('heading').first()).toBeVisible();
  expect(sockets, 'the player statistics opened a circuit').toEqual([]);
});

test('a shared match report opens no circuit', async ({ page, visitor }) => {
  // The URL first, from the games list, because a match report is only ever reached by its link.
  await goto(page, '/games');
  await openOverview(page, FIXTURE_MATCH);
  const overviewPath = new URL(page.url()).pathname;

  // Then cold, which is how a link shared into a group chat is opened.
  const sockets = watchSockets(visitor);
  await openSettled(visitor, overviewPath);
  await expect(visitor.locator('#formation-overview')).toBeVisible();
  await expect(visitor.getByText(FIXTURE_MATCH, { exact: false }).first()).toBeVisible();
  await expect(visitor.getByRole('button', { name: 'Save as image' })).toHaveCount(0);
  expect(sockets, 'the match report opened a circuit').toEqual([]);
});
