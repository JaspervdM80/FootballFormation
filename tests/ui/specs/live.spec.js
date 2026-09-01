// The live screen past kick-off — the part used one-handed, standing on a touchline, while the
// clock runs.
//
// match-day.spec.js runs a match end to end and checks that what happened reaches the result page.
// This file stays on the screen itself: the substitution dialog's other branch (match-day takes the
// position-swap one), undoing what it did, and the reading on the clock across the break.
//
// LiveMatchServiceTests covers the banking arithmetic at the service. What only a browser can say is
// that the screen driving it shows the banked figure rather than a clock that never stopped.
import { test, expect } from '../fixtures.js';
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import { chooseOption, clickFor, goto, gotoRendered, liveMatch, openDialog, submitDialog } from '../helpers.js';

// Shirt *and* short name, because a shirt number is not unique — nothing stops two players in a
// squad wearing the same one, and this file's arithmetic would then credit a substitution to the
// wrong chip. Both renderings carry both, the bench with a "#" the pitch leaves off.
const onPitch = (page) => page.locator('.live-lineup .pitch-player');
const onBench = (page) => page.locator('.live-bench .live-bench-chip');

const players = async (locator) => (await locator.allInnerTexts())
  .map(text => text.replace('#', '').replace(/\s+/g, ' ').trim())
  .sort();

/** "12:34" → 754. The reading is the whole assertion, so a format change should fail rather than pass. */
async function clockSeconds(page) {
  const reading = (await page.locator('.live-clock').innerText()).trim().split('\n')[0];
  const [minutes, seconds] = reading.match(/^(\d+):(\d{2})$/).slice(1).map(Number);
  return minutes * 60 + seconds;
}

test('a substitution swaps the two chips over, and undoing it puts them back', async ({ page }) => {
  await liveMatch(page, 'FC Wisselbank');

  const pitchBefore = await players(onPitch(page));
  // Empty to start with: a line-up saved without anyone dragged onto the substitutes' bench has
  // none, and "Comes on" then offers the rest of the roster instead — which is the ordinary case.
  expect(await players(onBench(page))).toEqual([]);

  await clickFor(page.locator('.live-lineup .pitch-player').first(),
    () => expect(page.locator('.mud-dialog')).toBeVisible());
  const dialog = await openDialog(page);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');

  const event = page.locator('.live-event');
  await expect(event).toHaveCount(1);
  // Stamped with the minute it happened, which is what makes the timeline a record rather than a list.
  await expect(event.locator('.live-event-min')).toHaveText(/^\d+'$/);

  // As many on the pitch as before, one of them somebody else, and the shirt that left it now on the
  // bench — the half of a substitution that a chip appearing on the pitch does not prove.
  const pitchAfter = await players(onPitch(page));
  const cameOn = pitchAfter.filter(player => !pitchBefore.includes(player));
  const wentOff = pitchBefore.filter(player => !pitchAfter.includes(player));
  expect(pitchAfter).toHaveLength(pitchBefore.length);
  expect(cameOn, 'nobody actually came on').toHaveLength(1);
  expect(await players(onBench(page))).toEqual(wentOff);

  await clickFor(event.getByRole('button'), () => expect(page.locator('.live-event')).toHaveCount(0));

  // Undo is the coach's answer to a mis-tap under time pressure, so it has to be the whole way back
  // and not merely the timeline entry going away. The two swap over rather than the incoming player
  // leaving the squad sheet — she is on the bench now, which is where a substitute belongs.
  expect(await players(onPitch(page))).toEqual(pitchBefore);
  expect(await players(onBench(page))).toEqual(cameOn);
});

test('the clock stops at half time and the second half picks up from the banked total', async ({ page }) => {
  const id = await liveMatch(page, 'FC Klokstand');

  // Past the first tick, so the readings below are telling apart a stopped clock from a running one
  // rather than two zeroes. Waited for on the app's own tick, not slept through.
  await expect.poll(() => clockSeconds(page), { timeout: 20_000 }).toBeGreaterThan(1);

  const controls = page.locator('.live-controls');
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );
  const banked = await clockSeconds(page);

  // A reload rather than a wait: it takes seconds of real time, which is exactly what a clock that
  // never stopped would spend, and the reading is served fresh from the stored anchor either way.
  await goto(page, `/games/${id}/live`);
  expect(await clockSeconds(page), 'the clock kept running through half time').toBe(banked);

  await clickFor(
    controls.getByRole('button', { name: 'Start 2nd Half' }),
    () => expect(controls.getByRole('button', { name: 'Half time' })).toHaveCount(0),
  );

  // Forwards from the banked total. A second half starting at nought reads lower, not higher, so
  // this is the assertion that tells the two apart.
  await expect.poll(() => clockSeconds(page), { timeout: 20_000 }).toBeGreaterThan(banked);
});

test('a spectator watching the same match is given a pitch that does nothing', async ({ page, browser }) => {
  const id = await liveMatch(page, 'FC Toeschouwer');

  const visitor = await browser.newContext({ storageState: VISITOR_STATE, baseURL: BASE_URL });
  try {
    const watching = await visitor.newPage();
    await gotoRendered(watching, `/games/${id}/live`);

    // The scoreboard is the point of the page for a parent, so it has to be there before anything
    // is asserted to be missing.
    await expect(watching.locator('.live-score-value').first()).toBeVisible();
    await expect(watching.locator('.live-lineup .pitch-player')).not.toHaveCount(0);

    for (const name of ['Goal', 'Goal against', 'Finish match', 'Half time']) {
      await expect(watching.getByRole('button', { name, exact: true })).toHaveCount(0);
    }

    // OnPlayerClicked is left unset for a spectator, so the chip is inert rather than guarded —
    // a tap on it has to open nothing at all.
    await watching.locator('.live-lineup .pitch-player').first().click();
    await expect(watching.locator('.mud-dialog')).toHaveCount(0);
  } finally {
    await visitor.close();
  }
});
