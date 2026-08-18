// Which pages open a SignalR circuit, and which deliberately do not.
//
// This is the assertion the render-mode split exists for. A statistics page with no circuit can
// never show "Reconnecting…", never force a reload, and survives a phone suspending the app,
// because there is no socket to lose — see docs/known_issues.md. None of that is visible from
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
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import { clickFor, gotoRendered } from '../helpers.js';

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

  // clickFor, not click: gotoRendered deliberately does not wait for hydration, and this row is a
  // MudTable OnRowClick that needs the circuit.
  await gotoRendered(page, '/players');
  await clickFor(
    page.locator('.mud-table-body .mud-table-row').first(),
    options => expect(page).toHaveURL(/\/players\/\d+\/stats/, options));
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
  await clickFor(
    page.locator('.game-row .action-btn[title="Overview"]').first(),
    options => expect(page).toHaveURL(/\/games\/\d+\/overview/, options));
  const overviewPath = new URL(page.url()).pathname;

  // Then cold, which is how a link shared into a group chat is opened.
  const shared = await page.context().newPage();
  const sockets = watchSockets(shared);
  await gotoRendered(shared, overviewPath);
  await expect(shared.getByRole('button', { name: 'Save as image', exact: false })).toBeVisible();
  expect(sockets, 'the match report opened a circuit').toEqual([]);
  await shared.close();
});

test('the games list keeps its circuit, and so the probe is honest', async ({ page }) => {
  const sockets = watchSockets(page);

  await gotoRendered(page, '/games');
  await expect(page.getByRole('heading', { name: 'Games', exact: false }).first()).toBeVisible();

  expect(sockets.length, 'the games list is interactive and should open one').toBeGreaterThan(0);
});

// The back arrow was a circuit's list of navigations and is the Referer header now, which is a
// wholly different mechanism for the same promise: reaching a player from the season statistics
// goes back there, and reaching the same player from the squad goes back to the squad. Nothing
// else in the suite touches it, and a referrer-policy change would break it in silence.
test.describe('the back arrow follows where the visitor actually came from', () => {
  const backArrow = page => page.locator('a.back-button').first();

  /** Walks the squad to a player page, and answers its path. */
  async function openAPlayer(page) {
    await gotoRendered(page, '/players');
    await clickFor(
      page.locator('.mud-table-body .mud-table-row').first(),
      options => expect(page).toHaveURL(/\/players\/\d+\/stats/, options));
    return new URL(page.url()).pathname;
  }

  test('walking there from the squad, back goes to the squad', async ({ page }) => {
    await openAPlayer(page);

    await expect(backArrow(page)).toHaveAttribute('href', '/players');
    await expect(backArrow(page)).toHaveAttribute('title', /Squad/);
  });

  test('arriving from the season statistics, back goes there instead of to the fallback', async ({ page }) => {
    const playerPath = await openAPlayer(page);

    // The header set by hand, because /stats only grows a link to a player once someone has scored,
    // and this is the assertion that separates the referrer from BackFallback — /players is both
    // the fallback and where the walk above came from, so that test alone would pass either way.
    const arriving = await page.context().newPage();
    await arriving.setExtraHTTPHeaders({ Referer: `${BASE_URL}/stats` });
    await gotoRendered(arriving, playerPath);

    await expect(backArrow(arriving)).toHaveAttribute('href', '/stats');
    await expect(backArrow(arriving)).toHaveAttribute('title', /Season/);
    await arriving.close();
  });

  test('a referrer from another site is ignored, and so is one this app cannot name', async ({ page }) => {
    const playerPath = await openAPlayer(page);

    for (const referer of ['https://evil.example/stats', `${BASE_URL}/login`]) {
      const spoofed = await page.context().newPage();
      await spoofed.setExtraHTTPHeaders({ Referer: referer });
      await gotoRendered(spoofed, playerPath);

      await expect(backArrow(spoofed), `back followed ${referer}`)
        .toHaveAttribute('href', '/players');
      await spoofed.close();
    }
  });

  test('opened cold, back takes the page its own fallback', async ({ page }) => {
    const playerPath = await openAPlayer(page);

    // A shared link has no referrer to follow, which is what BackFallback is for.
    const cold = await page.context().newPage();
    await gotoRendered(cold, playerPath);
    await expect(backArrow(cold)).toHaveAttribute('href', '/players');
    await cold.close();
  });
});
