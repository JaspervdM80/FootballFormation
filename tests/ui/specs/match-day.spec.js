// The journey the app exists for: pick a lineup, run the match from the touchline, and have the
// minutes and the scoreline come out the other end.
//
// Every other spec here checks one screen. This one is the only place the screens are checked
// against each other — a goal logged on the live screen has to reach the result page, and a lineup
// built in the formation builder has to be the lineup the live screen substitutes from.
import { test, expect } from '../fixtures.js';
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import {
  chooseOption, clickFor, fillLineup, finishMatch, gameRow, goto, gotoRendered, liveMatch,
  matchWithId, openDialog, saveLineup, startMatch, submitDialog,
} from '../helpers.js';

test('a lineup dragged onto the pitch is still there after a reload', async ({ page }) => {
  const id = await matchWithId(page, 'FC Wedstrijddag');

  const { placed, squad } = await fillLineup(page);
  expect(placed, 'the seeded squad should be draggable onto the pitch').toBeGreaterThan(0);

  // A reload is the only proof that Save reached the database, not just the circuit's memory.
  await goto(page, `/games/${id}/formation`);
  await expect(page.locator('.pitch .pitch-player')).toHaveCount(placed);

  // And a player on the pitch is no longer offered in the list beside it. Counted as a difference
  // rather than as zero, so adding a fixture player does not quietly break this.
  await expect(page.locator('.draggable-player')).toHaveCount(squad - placed);
});

test('a match is run from the live screen and its score reaches the result', async ({ page }) => {
  const id = await matchWithId(page, 'FC Uitslag');
  const { placed } = await fillLineup(page, 2);

  await goto(page, `/games/${id}/live`);
  // "Us" and "them" are always these two, whatever order the venue puts them in on screen.
  const ourScore = page.locator('.live-score-value:not(.live-score-away)');
  const theirScore = page.locator('.live-score-value.live-score-away');

  await startMatch(page);

  // The lineup only appears once there is a period being played — before kick-off there is nothing
  // to substitute from.
  await expect(page.locator('.live-lineup .pitch-player')).toHaveCount(placed);

  // One for us and one for them, so the scoreline is not symmetrical and a swapped side would show.
  await clickFor(
    page.getByRole('button', { name: 'Goal', exact: true }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const goalDialog = await openDialog(page);
  await chooseOption(page, goalDialog, 'Scorer', 'Fixture');
  await submitDialog(page, 'Add goal');
  await expect(ourScore).toHaveText('1');

  await clickFor(
    page.getByRole('button', { name: 'Goal against' }),
    () => expect(theirScore).toHaveText('1'),
  );

  // Each goal carries the score it made it, newest first — so the equaliser reads 1–1 above the
  // opener's 1–0. Counted forwards over the match, which is the only way to get that off a list
  // that runs backwards.
  await expect(page.locator('.live-event .live-event-score')).toHaveText(['1–1', '1–0']);

  await finishMatch(page);

  // The scoreline follows the match to the list and to the report.
  await goto(page, '/games');
  const results = page.locator('.game-section', { hasText: 'Results' });
  await expect(results.locator('.game-row', { hasText: 'FC Uitslag' })).toHaveCount(1);
  await expect(gameRow(page, 'FC Uitslag').locator('.game-score')).toHaveText(/1\s*.\s*1/);
});

test('a match being played leads its card, in a colour nothing else there uses', async ({ page }) => {
  await liveMatch(page, 'FC Bezig');

  await goto(page, '/games');
  const row = gameRow(page, 'FC Bezig');
  // Leading the row, and pulsing — .action-live is the red, .action-live-now is the match under way.
  await expect(row.locator('.action-btn').first()).toHaveClass(/action-live-now/);

  // The crest red, read back through the theme rather than as a hex: ClubTheme is the one place a
  // colour is chosen, and a test naming #e11d24 would be the second.
  const colours = await row.evaluate(el => {
    const probe = document.createElement('span');
    probe.style.backgroundColor =
      getComputedStyle(document.documentElement).getPropertyValue('--club-primary').trim();
    document.body.append(probe);
    const clubPrimary = getComputedStyle(probe).backgroundColor;
    probe.remove();
    return {
      live: getComputedStyle(el.querySelector('.action-live')).backgroundColor,
      clubPrimary,
    };
  });
  expect(colours.live, 'the live button should be painted the club primary').toBe(colours.clubPrimary);
});

test('tapping a player on the pitch offers a substitution and a position swap', async ({ page }) => {
  await liveMatch(page, 'FC Wisselen');

  const chips = page.locator('.live-lineup .pitch-player');
  const first = chips.first();
  const before = await first.textContent();

  await clickFor(first, () => expect(page.locator('.mud-dialog')).toBeVisible());
  const dialog = await openDialog(page);

  // Two lists, and the button names whichever one was used.
  await expect(dialog.getByRole('button', { name: 'Make substitution' })).toBeDisabled();
  await chooseOption(page, dialog, 'Swaps position with', '#');
  await submitDialog(page, 'Swap positions');
  await expect(page.getByText('Positions swapped', { exact: false })).toBeVisible();

  // Both are still on — a swap is not a substitution — and they have changed places, so the chip
  // that was first now names the other player. Nothing reaches the timeline.
  await expect(chips).toHaveCount(2);
  await expect(first).not.toHaveText(before);
  await expect(page.locator('.live-event')).toHaveCount(0);
});

test('a quarters half keeps its changes in a pop-up and is run as one half', async ({ page }) => {
  // Quarters, so the first half is planned as two line-ups and the plan has something in it.
  const id = await matchWithId(page, 'FC Kwarten', { split: 'Quarters' });

  const available = page.locator('.draggable-player');
  const emptySlots = page.locator('.pitch .pitch-empty');
  const chips = page.locator('.pitch .pitch-player');

  // Q1 takes the front of the squad list and Q2 the back, so the two line-ups genuinely differ and
  // the card has changes to list.
  await expect(available.first()).toBeVisible();
  for (let i = 0; i < 3; i++) {
    await available.first().dragTo(emptySlots.first());
    await expect(chips).toHaveCount(i + 1);
  }
  await clickFor(page.getByRole('tab').nth(1), () => expect(chips).toHaveCount(0));
  for (let i = 0; i < 3; i++) {
    await available.last().dragTo(emptySlots.first());
    await expect(chips).toHaveCount(i + 1);
  }
  await saveLineup(page);

  await goto(page, `/games/${id}/live`);

  // The plan is a reference, not part of the screen: nothing about the quarter boundary is on the
  // page until it is asked for, and there is no control that rolls the next line-up on either.
  await expect(page.locator('.planned-row')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Next line-up' })).toHaveCount(0);

  // Worth reading before kick-off too, so the button is there from the start.
  await clickFor(
    page.getByRole('button', { name: /^Changes \(\d+\)$/ }),
    () => expect(page.locator('.mud-dialog .planned-row').first()).toBeVisible(),
  );
  await clickFor(
    page.locator('.mud-dialog').getByRole('button', { name: 'Close' }),
    () => expect(page.locator('.mud-dialog')).toHaveCount(0),
  );

  await startMatch(page);

  // The clock runs in halves however the line-ups were planned, so the control on offer during the
  // first quarter is already half time — the second quarter is never a period the clock stops for.
  const controls = page.locator('.live-controls');
  await expect(controls.getByRole('button', { name: 'Next line-up' })).toHaveCount(0);
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );
});

test('the timeline can be narrowed to the goals', async ({ page }) => {
  await liveMatch(page, 'FC Tijdlijn');

  const events = page.locator('.live-event');

  // One of each kind, so the filter has something to keep and something to drop.
  await clickFor(page.getByRole('button', { name: 'Goal against' }), () => expect(events).toHaveCount(1));

  await clickFor(
    page.locator('.live-lineup .pitch-player').first(),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const dialog = await openDialog(page);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');
  await expect(events).toHaveCount(2);

  // Only a goal carries a scoreline, so what is left is the goal rather than the substitution.
  await clickFor(
    page.locator('.live-timeline-toggle input[type=checkbox]'),
    () => expect(events).toHaveCount(1),
  );
  await expect(page.locator('.live-event .live-event-score')).toHaveCount(1);

  // The bench is no longer what this checkbox folds away — it stays put.
  await expect(page.locator('.live-bench')).toBeVisible();
});

test('a substitution and an injury reach the result page, where the toggle folds only the sub away', async ({ page }) => {
  const id = await liveMatch(page, 'FC Naspel');

  const chips = page.locator('.live-lineup .pitch-player');

  // A substitution — somebody off the roster comes on for a player on the pitch.
  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  let dialog = await openDialog(page);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');
  await expect(page.locator('.live-event')).toHaveCount(1);

  // And an injury nobody came on for — the switch alone, no replacement, so it stands as its own row.
  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  dialog = await openDialog(page);
  await dialog.locator('label.mud-switch', { hasText: 'Injured' }).click();
  await submitDialog(page, 'Off injured');
  await expect(page.locator('.live-event', { hasText: 'not replaced' })).toHaveCount(1);

  await finishMatch(page);

  // The finished match reads back the touchline's own list — kick-off first, and the page a parent
  // opens to see what happened — so both the substitution and the injury have to be on it.
  await gotoRendered(page, `/games/${id}/result`);
  const events = page.locator('.live-event');
  await expect(events).toHaveCount(2);
  const injuryRow = events.filter({ hasText: 'not replaced' });
  await expect(injuryRow.locator('.live-event-injury')).toBeVisible();

  // The toggle folds the substitution away and leaves the injury — the one change nobody came on for.
  await clickFor(
    page.locator('.live-timeline-toggle input[type=checkbox]'),
    () => expect(events).toHaveCount(1),
  );
  await expect(injuryRow).toHaveCount(1);
});

test('a substitution and an injury entered wrong are undone from the result page', async ({ page }) => {
  const id = await liveMatch(page, 'FC Foutje');
  const chips = page.locator('.live-lineup .pitch-player');

  // A substitution and an injury nobody came on for, both entered live — the two to be undone later.
  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  let dialog = await openDialog(page);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');
  await expect(page.locator('.live-event')).toHaveCount(1);

  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  dialog = await openDialog(page);
  await dialog.locator('label.mud-switch', { hasText: 'Injured' }).click();
  await submitDialog(page, 'Off injured');
  await expect(page.locator('.live-event', { hasText: 'not replaced' })).toHaveCount(1);

  await finishMatch(page);

  // The result page is where a finished match is corrected, so the undo the live screen offers has
  // to be here too — a goal carries the × it always did, a change carries an undo.
  await gotoRendered(page, `/games/${id}/result`);
  const events = page.locator('.live-event');
  await expect(events).toHaveCount(2);
  await expect(events.getByRole('button', { name: 'Undo' })).toHaveCount(2);

  // The injury goes on its own; the substitution is untouched by it.
  const injuryRow = events.filter({ hasText: 'not replaced' });
  await clickFor(injuryRow.getByRole('button', { name: 'Undo' }), () => expect(events).toHaveCount(1));

  // And the substitution, the last thing left, goes too.
  await clickFor(events.getByRole('button', { name: 'Undo' }), () => expect(events).toHaveCount(0));
});

test('a substitution carries an edit that opens pre-filled and round-trips through the service', async ({ page }) => {
  const id = await liveMatch(page, 'FC Correctie');
  const chips = page.locator('.live-lineup .pitch-player');

  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  const dialog = await openDialog(page);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');
  const event = page.locator('.live-event');
  await expect(event).toHaveCount(1);
  const cameOn = (await event.locator('.live-event-main').textContent()).trim();

  await finishMatch(page);

  // The correction lives beside the undo the result page already offers.
  await gotoRendered(page, `/games/${id}/result`);
  await clickFor(
    event.getByRole('button', { name: 'Edit' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );

  // It opens knowing this change: who came off, and the roster to pick a replacement from.
  const edit = await openDialog(page);
  await expect(edit).toContainText('came off');
  await expect(edit.locator('.mud-input-control', { has: page.getByText('Comes on', { exact: false }) })).toBeVisible();

  // Saving runs the change back through the service — reversing the line-up and laying it down again —
  // and the timeline stands where it was.
  await submitDialog(page, 'Save');
  await expect(page.getByText('Substitution updated', { exact: false })).toBeVisible();
  await expect(event).toHaveCount(1);
  await expect(event.locator('.live-event-main')).toHaveText(cameOn);
});

test('the timeline draws half time between the two halves', async ({ page }) => {
  await liveMatch(page, 'FC Rust');

  const events = page.locator('.live-event');
  const halfTime = page.locator('.live-event-break');

  // One goal in each half. Until the second one there is only one half on the list, and a break
  // above the only thing on it would be a line drawn through nothing.
  await clickFor(page.getByRole('button', { name: 'Goal against' }), () => expect(events).toHaveCount(1));
  await expect(halfTime).toHaveCount(0);

  const controls = page.locator('.live-controls');
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );
  await clickFor(
    controls.getByRole('button', { name: 'Start 2nd Half' }),
    () => expect(controls.getByRole('button', { name: 'Half time' })).toHaveCount(0),
  );

  await clickFor(page.getByRole('button', { name: 'Goal against' }), () => expect(events).toHaveCount(2));

  // Exactly one break, and it sits between the two — the list runs newest first, so the second
  // half's goal is above it and the first half's below.
  await expect(halfTime).toHaveCount(1);
  await expect(halfTime).toHaveText('Half time');
  await expect(page.locator('.live-timeline > *')).toHaveCount(3);
  await expect(page.locator('.live-timeline > *').nth(1)).toHaveClass(/live-event-break/);
});

test('a half-time change is set on the pitch at the break and recorded when the half kicks off', async ({ page }) => {
  await liveMatch(page, 'FC Rustwissel');

  const controls = page.locator('.live-controls');
  const events = page.locator('.live-event');

  // End the first half — the break opens on the second half, carried over from whoever just finished.
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );

  // A tap at the break offers only who comes on: the half is not being played, so there is no position swap or injury to make.
  await clickFor(
    page.locator('.live-lineup .pitch-player').first(),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const dialog = await openDialog(page);
  await expect(dialog.getByText('Swaps position with')).toHaveCount(0);
  await expect(dialog.locator('label.mud-switch', { hasText: 'Injured' })).toHaveCount(0);
  await chooseOption(page, dialog, 'Comes on', '#');
  await submitDialog(page, 'Make substitution');
  await expect(page.getByText('Half-time change made', { exact: false })).toBeVisible();

  // A plan until the half starts: nothing on the timeline yet, but listed as what kick-off will record.
  await expect(events).toHaveCount(0);
  await expect(page.locator('.live-halftime-plan .planned-row')).toHaveCount(1);

  // Kicking off the second half turns the change into a real substitution on the timeline.
  await clickFor(
    controls.getByRole('button', { name: 'Start 2nd Half' }),
    () => expect(events).toHaveCount(1),
  );
});

test('the playing-time table drops its estimate once the match has been run', async ({ page }) => {
  const id = await matchWithId(page, 'FC Speeltijd');
  await fillLineup(page, 2);

  // Nothing has been played yet, so the totals are only what the lineup plans for. The table says
  // so with a "~" on every total and a footnote under it.
  const totals = page.locator('.playtime-table .pt-total');
  await expect(totals.first()).toContainText('~');
  await expect(page.locator('.playtime-note')).toBeVisible();

  // The live screen's own table says the same thing in its heading, because its numbers cannot:
  // before kick-off they are what the line-up plans for, not time anyone has played.
  await goto(page, `/games/${id}/live`);
  const minutesLabel = page.locator('.live-minutes-card .card-label');
  await expect(minutesLabel).toHaveText('Planned minutes');

  await startMatch(page);
  await expect(minutesLabel).toHaveText('Minutes played');

  await finishMatch(page);

  // The same table now reads the match clock instead — whistled off within seconds of kick-off, so
  // the honest answer is nought minutes rather than the half the lineup was planned for.
  await goto(page, `/games/${id}/formation`);
  await expect(totals.first()).not.toContainText('~');
  await expect(page.locator('.playtime-note')).toHaveCount(0);
});

/** The grid's actual track count, which is the half of a hidden column that markup cannot show. */
const trackCount = locator =>
  locator.evaluate(el => getComputedStyle(el).gridTemplateColumns.split(/\s+/).length);

test('the statistics give an admin the minutes and a visitor only the split', async ({ page, browser }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  // Played on paper rather than from the touchline: a match with a line-up and a final score that
  // nobody ran live is complete, so the report counts it, and its minutes are the line-up's
  // estimate. That is the case carrying the "~" and its footnote, which go with the numbers — a
  // match run live here is whistled off within seconds and rounds to nought minutes, which would
  // leave the player page on its empty state with nothing to hide.
  const id = await matchWithId(page, 'FC Schatting', { past: true });
  await fillLineup(page, 2);

  // Identified, not counted: the fairness table holds every player every spec in this file has left
  // behind, and "whoever is top" is not a player this test knows anything about. The shirt number
  // is what the pitch chip and the table row have in common — the chip abbreviates the name.
  const shirt = (await page.locator('.pitch .pitch-player .pitch-number').first().innerText()).trim();

  await goto(page, `/games/${id}/result`);
  await page.locator('.score-big-input').first().fill('2');
  await page.locator('.score-big-input.score-away').fill('1');
  await clickFor(
    page.getByRole('button', { name: 'Save Score' }),
    () => expect(page.getByText('saved', { exact: false }).first()).toBeVisible(),
  );

  await gotoRendered(page, '/stats');
  const row = page.locator('.pt-row', {
    has: page.locator('.r-shirt', { hasText: new RegExp(`^${shirt}$`) }),
  }).first();
  await expect(row).toBeVisible();
  await row.click();
  await page.waitForURL(/\/players\/\d+\/stats/);
  const playerPath = new URL(page.url()).pathname;

  await expect(page.locator('.stat-label', { hasText: /^Minutes$/ })).toBeVisible();
  await expect(page.locator('.game-head .g-num').first()).toHaveText('Min');
  await expect(page.locator('.game-note')).toBeVisible();
  expect(await trackCount(page.locator('.game-list .game-row').first())).toBe(4);
  // The venue reads as a badge now, on everyone's copy of the page.
  await expect(page.locator('.g-opp-name .badge-venue').first()).toHaveText(/HOME|AWAY/);

  // The position development grid pivots the same per-player figures — this player's whole match
  // was one line-up in one position, so their row carries the "single position" flag. Finding the
  // row at all is most of the assertion: the grid falls back to an empty-state card with no table
  // whenever it has nothing to pivot, so a broken pivot fails here before the badge check does.
  await gotoRendered(page, '/stats/positions');
  const gridRow = page.locator('.mud-table-body .mud-table-row', {
    has: page.locator('.pd-num', { hasText: new RegExp(`^${shirt}$`) }),
  }).first();
  await expect(gridRow).toBeVisible();
  await expect(gridRow.locator('.badge-warning')).toHaveText('Single position');

  const visitor = await browser.newContext({ storageState: VISITOR_STATE, baseURL: BASE_URL });
  try {
    const anon = await visitor.newPage();
    await gotoRendered(anon, playerPath);

    // Same page, same player — the positions card proves it rendered before anything is asserted
    // to be missing, and it still says how they divided the time they got.
    await expect(anon.getByText('Positions', { exact: false })).toBeVisible();
    await expect(anon.locator('.position-meta').first()).toHaveText(/^\d+%$/);

    await expect(anon.locator('.stat-label', { hasText: /^Minutes$/ })).toHaveCount(0);
    await expect(anon.locator('.game-head .g-num').first()).toHaveText('G');
    await expect(anon.locator('.game-note')).toHaveCount(0);
    await expect(anon.locator('.g-opp-name .badge-venue').first()).toHaveText(/HOME|AWAY/);

    // The cell is gone, and so is its track — a four-track grid with three cells slides goals and
    // assists out from under their headers, and every assertion above would still pass.
    expect(await trackCount(anon.locator('.game-list .game-row').first())).toBe(3);
    expect(await trackCount(anon.locator('.stat-tiles'))).toBe(3);
  } finally {
    await visitor.close();
  }
});

test('a half already played is no longer edited in the builder', async ({ page }) => {
  const id = await matchWithId(page, 'FC Gespeelde Helft');
  const { placed } = await fillLineup(page);

  await goto(page, `/games/${id}/live`);
  await startMatch(page);

  // The builder tab a coach left open before kick-off: it still holds the pre-kick-off plan, and
  // saving that would write it over the line-up the touchline recorded.
  await goto(page, `/games/${id}/formation`);
  await expect(page.getByText('This half was played from the touchline', { exact: false })).toBeVisible();

  // Still shown — it is the record of who was on the pitch — but nothing on it can be picked up.
  await expect(page.locator('.pitch .pitch-player')).toHaveCount(placed);
  await expect(page.locator('.pitch .pitch-player[draggable="true"]')).toHaveCount(0);

  // The second half is a plan, not a record, so it takes a drop as it always did.
  const notice = page.getByText('This half was played from the touchline', { exact: false });
  await clickFor(page.getByRole('tab').nth(1), () => expect(notice).toHaveCount(0));
  await page.locator('.draggable-player').first().dragTo(page.locator('.pitch .pitch-empty').first());
  await expect(page.locator('.pitch .pitch-player')).toHaveCount(1);
});
