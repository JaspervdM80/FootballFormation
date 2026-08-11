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

  // Wait for an outcome rather than a duration: the eleventh attempt is the first one Blazor's
  // default schedule puts a wait in front of, so reaching it is what makes the assertion below
  // mean something. Under that default it arrives within milliseconds — ten attempts fired
  // back-to-back with no delay at all — and announces a five-second wait as it does.
  await expect
    .poll(async () => (await recorded(page)).some(e => e.currentAttempt >= 11 || e.secondsToNextAttempt > 1),
      { timeout: 30_000 })
    .toBe(true);

  await page.unroute(NEGOTIATE);
  await expect(page.locator(MODAL)).toBeHidden({ timeout: 20_000 });

  const waits = (await recorded(page))
    .filter(e => e.state === 'retrying')
    .map(e => e.secondsToNextAttempt);

  // Before reading the waits, prove there are some. A reload — pwa.js's, or Blazor's own on a
  // rejected circuit — takes `window.__reconnectEvents` with it, and `Math.max()` of nothing is
  // -Infinity, which would sail through the assertion below with nothing measured at all.
  expect(waits.length, 'no reconnect attempts were recorded — did the page reload?').toBeGreaterThan(0);
  expect(Math.max(...waits), `waits between attempts, in seconds: ${waits}`).toBeLessThanOrEqual(1);
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
