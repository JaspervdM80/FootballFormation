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
import { VISITOR_STATE } from '../playwright.config.js';
import {
  clickFor, finishMatch, goto, halfTime, liveMatch, logGoal, openDialog, pickPlayer,
  startSecondHalf, submitDialog, substitute,
} from '../helpers.js';

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

// Stands in for the phone's motor: records the patterns rather than needing a tap first, as Chrome's real one does.
const fakeVibration = () => {
  window.buzzes = [];
  navigator.vibrate = (pattern) => { window.buzzes.push(pattern); return true; };
};

test('a substitution swaps the two chips over, and undoing it puts them back', async ({ page }) => {
  await liveMatch(page, 'FC Wisselbank');

  const pitchBefore = await players(onPitch(page));
  // Empty to start with: a line-up saved without anyone dragged onto the substitutes' bench has
  // none, and "Comes on" then offers the rest of the roster instead — which is the ordinary case.
  expect(await players(onBench(page))).toEqual([]);

  await substitute(page);

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

  // Named, because a substitution now carries an edit beside its undo.
  await clickFor(event.getByRole('button', { name: 'Undo' }),
    () => expect(page.locator('.live-event')).toHaveCount(0));

  // Undo is the coach's answer to a mis-tap under time pressure, so it has to be the whole way back
  // and not merely the timeline entry going away. The two swap over rather than the incoming player
  // leaving the squad sheet — she is on the bench now, which is where a substitute belongs.
  expect(await players(onPitch(page))).toEqual(pitchBefore);
  expect(await players(onBench(page))).toEqual(cameOn);
});

test('the assist is asked for after the scorer, and a goal is corrected without leaving the touchline', async ({ page }) => {
  await liveMatch(page, 'FC Voorzet');

  await clickFor(
    page.getByRole('button', { name: 'Goal', exact: true }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const dialog = await openDialog(page);
  const picks = dialog.locator('.player-pick');
  const scorer = await picks.first().getAttribute('title');
  await clickFor(picks.first(), () => expect(dialog.getByRole('button', { name: 'No assist' })).toBeVisible(),
    { settle: 10_000 });

  await expect(dialog.locator(`.player-pick[title="${scorer}"]`)).toHaveCount(0);
  const assister = await picks.first().getAttribute('title');
  await pickPlayer(page, dialog, assister);

  const event = page.locator('.live-event');
  await expect(event.locator('.live-event-sub')).toHaveText(`↳ ${assister}`);
  const ourScore = page.locator('.live-score-value:not(.live-score-away)');
  const theirScore = page.locator('.live-score-value.live-score-away');
  await expect(ourScore).toHaveText('1');

  // An own goal changes sides, so the correction has to recount the scoreline the touchline shows.
  await clickFor(event.getByRole('button', { name: 'Edit' }), () => expect(page.locator('.mud-dialog')).toBeVisible());
  const edit = await openDialog(page);
  await edit.locator('label.mud-switch', { hasText: 'Own goal' }).click();
  await submitDialog(page, 'Save');

  await expect(page.getByText('Goal updated')).toBeVisible();
  await expect(ourScore).toHaveText('0');
  await expect(theirScore).toHaveText('1');
});

test('the clock stops at half time and the second half picks up from the banked total', async ({ page }) => {
  const id = await liveMatch(page, 'FC Klokstand');

  // Past the first tick, so the readings below are telling apart a stopped clock from a running one
  // rather than two zeroes. Waited for on the app's own tick, not slept through.
  await expect.poll(() => clockSeconds(page), { timeout: 20_000 }).toBeGreaterThan(1);

  await halfTime(page);
  const banked = await clockSeconds(page);

  // A reload rather than a wait: it takes seconds of real time, which is exactly what a clock that
  // never stopped would spend, and the reading is served fresh from the stored anchor either way.
  await goto(page, `/games/${id}/live`);
  expect(await clockSeconds(page), 'the clock kept running through half time').toBe(banked);

  await startSecondHalf(page);

  // Forwards from the banked total. A second half starting at nought reads lower, not higher, so
  // this is the assertion that tells the two apart.
  await expect.poll(() => clockSeconds(page), { timeout: 20_000 }).toBeGreaterThan(banked);
});

test('a spectator sees and feels our goal as it arrives, but not again on a reload', async ({ page, visitor }) => {
  const id = await liveMatch(page, 'FC Juichen');

  await visitor.addInitScript(fakeVibration);
  await goto(visitor, `/games/${id}/live`);

  const flash = visitor.locator('.live-goal-flash');
  await expect(visitor).toHaveTitle(/^0 – 0 · .*FC Juichen/);
  await expect(flash).toHaveCount(0);

  await page.evaluate(fakeVibration);

  await logGoal(page, 'Fixture');

  await expect(flash).toContainText('Fixture');
  await expect(visitor.locator('.live-event-fresh')).toHaveCount(1);
  await expect(visitor).toHaveTitle(/^(1 – 0|0 – 1) · /);
  await expect.poll(() => visitor.evaluate(() => window.buzzes.length)).toBe(1);
  // The coach tapped the button; the buzz is for the people who did not.
  expect(await page.evaluate(() => window.buzzes)).toEqual([]);

  // It stands down by itself, and a reload mid-match must not replay it.
  await expect(flash).toHaveCount(0, { timeout: 15_000 });
  await goto(visitor, `/games/${id}/live`);
  await expect(visitor.locator('.live-score-value').first()).toBeVisible();
  await expect(flash).toHaveCount(0);
  await expect(visitor.locator('.live-event-fresh')).toHaveCount(0);

  // A goal against is news, not a party: a buzz and the new row, no banner.
  await clickFor(
    page.getByRole('button', { name: 'Goal against' }),
    () => expect(visitor.locator('.live-event')).toHaveCount(2),
  );
  await expect(visitor.locator('.live-event-fresh')).toHaveCount(1);
  await expect.poll(() => visitor.evaluate(() => window.buzzes.length)).toBe(1);
  await expect(flash).toHaveCount(0);
});

test('a spectator who switched vibration off still sees our goal, but is not buzzed for it', async ({ page, visitor }) => {
  const id = await liveMatch(page, 'FC Stiltezone');

  await visitor.addInitScript(fakeVibration);

  // Drawn only once the browser has answered, so its button arrives with its handler already bound. Clicked once on purpose: a
  // retried click on a toggle switches it straight back.
  await goto(visitor, '/settings');
  const setting = visitor.locator('.notify-row', { hasText: 'Vibrate on match events' });
  await setting.getByRole('button', { name: 'Turn off' }).click();
  await expect(setting).toContainText('Off in this browser');

  // Kept by the browser, not the circuit.
  await goto(visitor, '/settings');
  await expect(setting.getByRole('button', { name: 'Turn on' })).toBeVisible();

  await goto(visitor, `/games/${id}/live`);
  await expect(visitor.locator('.live-score-value').first()).toBeVisible();

  await logGoal(page, 'Fixture');

  await expect(visitor.locator('.live-goal-flash')).toBeVisible();

  // The circuit asks for its buzz after drawing the banner, so the banner proves nothing about it. Asking the same function directly
  // and awaiting it does: whatever the page asked for, the switch has had to refuse.
  expect(await visitor.evaluate(async () => {
    await window.vibration.buzz([100]);
    return window.buzzes;
  })).toEqual([]);
});

test('an installed iPhone app is pointed at its own settings instead of offered a switch it would ignore', async ({ openPage }) => {
  const iphone = await openPage({
    storageState: VISITOR_STATE,
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  // Safari has no Vibration API at all, which is what the page reads — the user agent alone would leave Chromium's in place. And
  // installed, the only way the app has an entry in the iPhone's settings to point at.
  await iphone.addInitScript(() => {
    delete Navigator.prototype.vibrate;
    Object.defineProperty(Navigator.prototype, 'standalone', { get: () => true });
  });
  await goto(iphone, '/settings');

  const setting = iphone.locator('.notify-row', { hasText: 'Vibrate on match events' });
  await expect(setting).toContainText('Settings → Notifications');
  await expect(setting.getByRole('button')).toHaveCount(0);
});

test('a spectator watching the same match is given a pitch that does nothing', async ({ page, visitor }) => {
  const id = await liveMatch(page, 'FC Toeschouwer');

  await goto(visitor, `/games/${id}/live`);

  // The scoreboard is the point of the page for a parent, so it has to be there before anything
  // is asserted to be missing.
  await expect(visitor.locator('.live-score-value').first()).toBeVisible();
  await expect(visitor.locator('.live-lineup .pitch-player')).not.toHaveCount(0);

  for (const name of ['Goal', 'Goal against', 'Finish match', 'Half time']) {
    await expect(visitor.getByRole('button', { name, exact: true })).toHaveCount(0);
  }

  // OnPlayerClicked is left unset for a spectator, so the chip is inert rather than guarded —
  // a tap on it has to open nothing at all.
  await visitor.locator('.live-lineup .pitch-player').first().click();
  await expect(visitor.locator('.mud-dialog')).toHaveCount(0);
});

// Stands in for the Screen Wake Lock API, counting what is held: headless Chromium refuses the real one.
const fakeWakeLock = () => {
  window.wakeLocks = { held: [], requests: 0 };
  Object.defineProperty(navigator, 'wakeLock', {
    value: {
      request: async () => {
        window.wakeLocks.requests++;
        const sentinel = new EventTarget();
        sentinel.release = async () => {
          window.wakeLocks.held = window.wakeLocks.held.filter(held => held !== sentinel);
          sentinel.dispatchEvent(new Event('release'));
        };
        window.wakeLocks.held.push(sentinel);
        return sentinel;
      },
    },
  });
};

test('the coach\'s screen stays awake from kick-off to full time, and a spectator\'s is left alone', async ({ page, visitor }) => {
  await page.addInitScript(fakeWakeLock);
  const id = await liveMatch(page, 'FC Wakker');
  const held = (on) => on.evaluate(() => window.wakeLocks.held.length);

  await expect.poll(() => held(page)).toBe(1);

  // What the browser does to a hidden page: the lock goes, and coming back to it has to ask again.
  await page.evaluate(() => window.wakeLocks.held[0].release());
  expect(await held(page)).toBe(0);
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
  await expect.poll(() => held(page)).toBe(1);

  await visitor.addInitScript(fakeWakeLock);
  await goto(visitor, `/games/${id}/live`);
  await expect(visitor).toHaveTitle(/^0 – 0 · .*FC Wakker/);

  // Calls reach the browser in the order they were made, so once a later render's title lands, anything the first render sent has
  // already run — whichever order the page makes them in.
  await clickFor(
    page.getByRole('button', { name: 'Goal against' }),
    () => expect(visitor).toHaveTitle(/^(0 – 1|1 – 0) · /),
  );
  expect(await visitor.evaluate(() => window.wakeLocks.requests)).toBe(0);

  // Leaving in-app keeps the window, the listener and the lock with it, so only the page letting go releases it. The marker proves this
  // was not a full load, which would release it whatever the page did.
  await page.evaluate(() => { window.sameDocument = true; });
  await page.locator('.topbar-nav a[href="/games"]').first().click();
  await page.waitForURL(/\/games$/);
  expect(await page.evaluate(() => window.sameDocument)).toBe(true);
  await expect.poll(() => held(page)).toBe(0);

  await goto(page, `/games/${id}/live`);
  await expect.poll(() => held(page)).toBe(1);
  await finishMatch(page);
  await expect.poll(() => held(page)).toBe(0);
});
