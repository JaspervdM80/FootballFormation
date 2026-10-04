// Correcting a match after the final whistle, from /result.
//
// Two things only a browser can say. The half-lengths row is derived from the period timings rather
// than stored, so it has to survive a round trip through the dialog and the service and come back
// reading what was typed. And both corrections are admin-only: MatchClockService and MatchGoalService
// refuse a visitor at the service boundary, but a visitor seeing an edit button they cannot use is a
// bug of its own — result.spec.js holds that half, with everything else a finished match keeps from one.
//
// MatchClockServiceTests and MatchGoalServiceTests own the arithmetic — what an overrun costs the
// minutes, and where a corrected goal lands in its half.
//
// The file name has to keep sorting after games.spec.js. One worker, one database and alphabetical
// file order, so the matches this file finishes today would otherwise lead the Results list that
// spec asserts the head of — the same reason match-day.spec.js, which also plays a match out, is
// safe where it is.
import { devices } from '@playwright/test';
import { test, expect } from '../fixtures.js';
import { ADMIN_STATE } from '../playwright.config.js';
import { clickFor, goto, openDialog, playedMatch, submitDialog } from '../helpers.js';

const halfLengths = (page) => page.locator('.half-lengths');
const goalEvent = (page) => page.locator('.live-event', { has: page.locator('.live-event-score') });

async function correctHalfLengths(page, first, second) {
  await clickFor(
    halfLengths(page).getByRole('button', { name: 'Correct half lengths' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const dialog = await openDialog(page);
  await dialog.getByLabel('1st Half').fill(String(first));
  await dialog.getByLabel('2nd Half').fill(String(second));
  await submitDialog(page, 'Save');
  await expect(page.getByText('Half lengths corrected')).toBeVisible();
}

// One match for the three corrections, in order: the later two need the halves stretched to real
// lengths, which is what the first one does.
test.describe.serial('a finished match corrected from its result page', () => {
  let id;

  /** The first test's match on its result page — played and corrected here instead when a later test runs on its own. */
  const correctedMatch = async (page) => {
    if (id) return goto(page, `/games/${id}/result`);
    id = await playedMatch(page, 'FC Fluitje');
    await correctHalfLengths(page, 37, 38);
  };

  test('a half whistled off late is corrected from the result page', async ({ page }) => {
    id = await playedMatch(page, 'FC Fluitje');

    await expect(halfLengths(page)).toBeVisible();
    await correctHalfLengths(page, 37, 38);

    await expect(halfLengths(page).locator('.half-lengths-value')).toHaveText('1st Half 37′ · 2nd Half 38′');

    // Derived from the periods, not held in the page: a reload has to read the same figures back.
    await goto(page, `/games/${id}/result`);
    await expect(halfLengths(page).locator('.half-lengths-value')).toHaveText('1st Half 37′ · 2nd Half 38′');
  });

  test('an opponent goal is re-timed rather than removed and retyped', async ({ page }) => {
    // The halves ran for seconds until the test above stretched them, and a goal is kept inside its
    // own half — so only now is there a 12th minute to move it to.
    await correctedMatch(page);

    const event = goalEvent(page);
    await expect(event).toHaveCount(1);

    await clickFor(
      event.getByRole('button', { name: 'Edit' }),
      () => expect(page.locator('.mud-dialog')).toBeVisible(),
    );
    const dialog = await openDialog(page);

    // Their scorers are not tracked, so the minute is the whole dialog.
    await expect(dialog.getByText('Goal for FC Fluitje')).toBeVisible();
    await expect(dialog.getByLabel('Scorer')).toHaveCount(0);
    await dialog.getByLabel('Minute').fill('12');
    await submitDialog(page, 'Save');

    await expect(page.getByText('Goal updated')).toBeVisible();
    await expect(goalEvent(page)).toHaveCount(1);
    await expect(goalEvent(page).locator('.live-event-min')).toHaveText("12'");
  });

  test('positions are swapped at a minute on the result page, and only from that minute on', async ({ page }) => {
    // The halves ran for seconds; real lengths give the line-up minutes to step through.
    await correctedMatch(page);

    const card = page.locator('.lineup-minute');
    const chips = card.locator('.pitch-player');
    const range = card.locator('.lineup-minute-range');
    await expect(chips).toHaveCount(2);

    await range.fill('10');
    await expect(card.locator('.lineup-minute-label')).toHaveText("10'");
    const before = await chips.allTextContents();

    await chips.nth(0).click();
    await expect(chips.nth(0)).toHaveClass(/pitch-selected/);
    await chips.nth(1).click();
    await expect(page.getByText('Positions swapped')).toBeVisible();

    // The two have traded places from the 10th minute, and the 9th is as it was.
    await expect(chips).toHaveText([before[1], before[0]]);
    await range.fill('9');
    await expect(chips).toHaveText(before);

    // The change list jumps to the first minute the swap shows at.
    await card.locator('.lineup-minute-change').click();
    await expect(card.locator('.lineup-minute-label')).toHaveText("10'");
    await expect(chips).toHaveText([before[1], before[0]]);

    // Folded away with the substitutions on the timeline, and undone from there.
    const events = page.locator('.live-event', { hasText: 'Swapped positions' });
    await clickFor(page.locator('.live-timeline-toggle input[type=checkbox]'), () => expect(events).toHaveCount(1));
    await clickFor(events.getByRole('button', { name: 'Undo' }), () => expect(events).toHaveCount(0));
    await expect(card.locator('.lineup-minute-change')).toHaveCount(0);
    await range.fill('10');
    await expect(chips).toHaveText(before);
  });
});

// scripts/visual-check.sh measures touch targets, but its seeded match is still under way and this
// row only appears after the final whistle — so the floor is checked here instead, on a context that
// reports a coarse pointer the way a phone does.
test('the half-lengths row clears a thumb on a phone without pushing the page sideways', async ({ page, openPage }) => {
  const id = await playedMatch(page, 'FC Smalletjes');

  const phone = await openPage({ ...devices['Pixel 7'], storageState: ADMIN_STATE });
  await goto(phone, `/games/${id}/result`);
  await expect(phone.locator('.half-lengths')).toBeVisible();

  const overflows = await phone.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(overflows, 'the result page scrolls sideways on a phone').toBe(false);

  const box = await phone.evaluate(() => {
    const row = document.querySelector('.half-lengths');
    const value = row.querySelector('.half-lengths-value').getBoundingClientRect();
    const button = row.querySelector('button').getBoundingClientRect();
    return { width: button.width, height: button.height, gap: button.left - value.right };
  });

  expect(box.width).toBeGreaterThanOrEqual(44);
  expect(box.height).toBeGreaterThanOrEqual(44);
  // Nothing, or at least 8px — a 2px gutter between two targets is a dead one.
  expect(box.gap).toBeGreaterThanOrEqual(8);
});
