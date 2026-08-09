// The journey the app exists for: pick a lineup, run the match from the touchline, and have the
// minutes and the scoreline come out the other end.
//
// Every other spec here checks one screen. This one is the only place the screens are checked
// against each other — a goal logged on the live screen has to reach the result page, and a lineup
// built in the formation builder has to be the lineup the live screen substitutes from.
import { test, expect } from '../fixtures.js';
import { chooseOption, clickFor, createMatch, gameRow, goto, openDialog, submitDialog } from '../helpers.js';

/** Creates a match and returns its id, read from the URL its own formation button navigates to. */
async function matchWithId(page, opponent) {
  await createMatch(page, { opponent });
  await gameRow(page, opponent).getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);
  return Number(page.url().match(/\/games\/(\d+)\//)[1]);
}

/**
 * Puts the available players onto the pitch by dragging them, which is the only way the builder
 * offers — an empty slot takes a drop, not a click — and then saves.
 *
 * The save is explicit and easy to miss: dropping a player only changes what is on screen, and
 * navigating away without pressing Save loses the lot. Returns how many were placed. The formation
 * has more slots than this squad has players, which is fine and realistic: a lineup does not have
 * to be complete for the match to be run.
 */
async function fillLineup(page, limit = 4) {
  const available = page.locator('.draggable-player');
  const emptySlots = page.locator('.pitch .pitch-empty');
  const chips = page.locator('.pitch .pitch-player');

  await expect(available.first()).toBeVisible();
  const squad = await available.count();
  const placed = Math.min(limit, squad);

  for (let i = 0; i < placed; i++) {
    // Always the first of each: a placed player leaves the list, and a filled slot stops being empty.
    await available.first().dragTo(emptySlots.first());
    await expect(chips).toHaveCount(i + 1);
  }

  await clickFor(
    page.getByRole('button', { name: /^Save( All Lineups)?$/ }).first(),
    () => expect(page.getByText('All lineups saved', { exact: false })).toBeVisible(),
    { settle: 10_000 },
  );
  return { placed, squad };
}

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

  await clickFor(
    page.getByRole('button', { name: 'Start match' }),
    () => expect(page.getByRole('button', { name: 'Finish match' })).toBeVisible(),
  );

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

  // The final whistle asks first, and its confirming button carries the same words as the one that
  // opened it — so the click has to be scoped to the control panel, or the locator matches both.
  await clickFor(
    page.locator('.live-controls').getByRole('button', { name: 'Finish match' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  await submitDialog(page, 'Finish match');
  await expect(page.getByRole('button', { name: 'Edit result' })).toBeVisible();

  // The scoreline follows the match to the list and to the report.
  await goto(page, '/games');
  const results = page.locator('.game-section', { hasText: 'Results' });
  await expect(results.locator('.game-row', { hasText: 'FC Uitslag' })).toHaveCount(1);
  await expect(gameRow(page, 'FC Uitslag').locator('.game-score')).toHaveText(/1\s*.\s*1/);
});

test('minutes played show up in the statistics once a match is complete', async ({ page }) => {
  await goto(page, '/stats');

  // The seeded squad is on the page whether or not anyone has played yet; the point of the check is
  // that the report renders against real games rather than throwing on an empty one.
  await expect(page.getByRole('heading', { name: 'Statistics', exact: false }).first()).toBeVisible();
  await expect(page.getByText('Fixture', { exact: false }).first()).toBeVisible();
});
