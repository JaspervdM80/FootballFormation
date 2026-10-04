// What happens between losing the circuit and getting it back.
//
// This is the touchline case: a phone that switched to another app suspends the tab, the WebSocket
// dies, and Blazor's overlay is the first thing the coach sees on the way back to a match in
// progress. How long it sits there is decided by a retry schedule and nothing else — so these
// assertions read the schedule Blazor is actually applying, rather than timing a rejoin with a
// stopwatch pointed at CI's mood.
//
// Playwright's `test`, not the app's: this is the one spec that breaks the connection on purpose,
// and the fixture's console-error guard would fail it for the refused requests that are the point.
// Nothing here reads a page the app rendered, so there is no quietly-broken page to miss.
import { test, expect } from '@playwright/test';
import { goto } from '../helpers.js';

const MODAL = '#components-reconnect-modal';

// Every rejoin starts by negotiating a new connection, so refusing that is a phone whose network
// has not come back yet — the state the stock schedule spends its ten free attempts in.
const NEGOTIATE = '**/_blazor/negotiate**';

/**
 * Records Blazor's own reconnect events, which it dispatches on the dialog element as
 * `components-reconnect-state-changed`: `show`, then one per second of waiting carrying the attempt
 * number and `secondsToNextAttempt`, then `hide`. That last field is the schedule, as applied.
 */
async function recordReconnectEvents(page) {
  await page.evaluate((selector) => {
    window.__reconnectEvents = [];
    document.querySelector(selector).addEventListener(
      'components-reconnect-state-changed',
      (event) => window.__reconnectEvents.push(event.detail));
  }, MODAL);
}

const recorded = (page) => page.evaluate(() => window.__reconnectEvents ?? []);

/** Drops the circuit's connection the way Blazor's own end-to-end tests do. */
const dropConnection = (page) => page.evaluate(() => Blazor._internal.forceCloseConnection());

test('a dropped circuit is retried every second, not every five', async ({ page }) => {
  await goto(page, '/games');
  await recordReconnectEvents(page);

  await page.route(NEGOTIATE, route => route.abort());
  await dropConnection(page);
  await expect(page.locator(MODAL)).toBeVisible();

  const waits = async () => (await recorded(page))
    .filter(e => e.state === 'retrying' && e.secondsToNextAttempt > 0)
    .map(e => e.secondsToNextAttempt);

  // Three announced waits, not a duration: Blazor's default fires ten attempts back-to-back, then counts a five-second wait down
  // as 5, 4, 3. A reload empties the record, which is why the length is checked again before every() — of nothing, it is true.
  await expect.poll(async () => (await waits()).length, { timeout: 30_000 }).toBeGreaterThanOrEqual(3);
  const announced = await waits();
  expect(announced.length, 'the recorded events went missing — did the page reload?').toBeGreaterThanOrEqual(3);
  expect(announced.every(seconds => seconds === 1), `waits between attempts, in seconds: ${announced}`).toBe(true);

  await page.unroute(NEGOTIATE);
  await expect(page.locator(MODAL)).toBeHidden({ timeout: 20_000 });
});

test('the page is live again after a rejoin, not merely repainted', async ({ page }) => {
  await goto(page, '/games');
  await recordReconnectEvents(page);

  // Waiting on the overlay would be a race worth losing: with the network right there, the rejoin
  // lands on the first immediate attempt and the overlay can come and go between two polls. The
  // event stream is the same story without the flicker, and `hide` is specifically not `rejected`
  // — the circuit was rejoined rather than given up on and reloaded.
  await dropConnection(page);
  await expect
    .poll(async () => (await recorded(page)).map(e => e.state), { timeout: 20_000 })
    .toContain('hide');

  // A dialog is rendered by the server over the circuit, so it opening at all is proof that the
  // connection carries interaction again — which an overlay disappearing does not prove.
  await page.getByRole('button', { name: 'Add' }).first().click();
  await expect(page.locator('.mud-dialog')).toBeVisible();
});
