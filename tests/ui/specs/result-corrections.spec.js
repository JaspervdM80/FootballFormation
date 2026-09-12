// Correcting a match after the final whistle, from /result.
//
// Two things only a browser can say. The half-lengths row is derived from the period timings rather
// than stored, so it has to survive a round trip through the dialog and the service and come back
// reading what was typed. And both corrections are admin-only: MatchClockService and MatchGoalService
// refuse a visitor at the service boundary, but a visitor seeing an edit button they cannot use is a
// bug of its own.
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
import { ADMIN_STATE, VISITOR_STATE } from '../playwright.config.js';
import { clickFor, goto, gotoRendered, liveMatch, openDialog, submitDialog } from '../helpers.js';

const halfLengths = (page) => page.locator('.half-lengths');
const goalEvent = (page) => page.locator('.live-event', { has: page.locator('.live-event-score') });

/** A match played through both halves and whistled off, with one opponent goal in the first. */
async function playedMatch(page, opponent) {
  const id = await liveMatch(page, opponent);
  const controls = page.locator('.live-controls');

  await clickFor(
    page.getByRole('button', { name: 'Goal against' }),
    () => expect(page.locator('.live-event')).toHaveCount(1),
  );
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );
  await clickFor(
    controls.getByRole('button', { name: 'Start 2nd Half' }),
    () => expect(controls.getByRole('button', { name: 'Half time' })).toHaveCount(0),
  );
  await clickFor(
    controls.getByRole('button', { name: 'Finish match' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  await submitDialog(page, 'Finish match');

  await goto(page, `/games/${id}/result`);
  return id;
}

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

test('a half whistled off late is corrected from the result page', async ({ page }) => {
  await playedMatch(page, 'FC Fluitje');

  await expect(halfLengths(page)).toBeVisible();
  await correctHalfLengths(page, 37, 38);

  await expect(halfLengths(page).locator('.half-lengths-value')).toHaveText('1st Half 37′ · 2nd Half 38′');

  // Derived from the periods, not held in the page: a reload has to read the same figures back.
  await goto(page, page.url().replace(/^https?:\/\/[^/]+/, ''));
  await expect(halfLengths(page).locator('.half-lengths-value')).toHaveText('1st Half 37′ · 2nd Half 38′');
});

test('an opponent goal is re-timed rather than removed and retyped', async ({ page }) => {
  await playedMatch(page, 'FC Doelpuntje');

  // The halves here ran for seconds, and a goal is kept inside its own half — so the whistle has to
  // be moved out to a real length before a 12th minute exists to move the goal to.
  await correctHalfLengths(page, 37, 38);

  const event = goalEvent(page);
  await expect(event).toHaveCount(1);

  await clickFor(
    event.getByRole('button', { name: 'Edit' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const dialog = await openDialog(page);

  // Their scorers are not tracked, so the minute is the whole dialog.
  await expect(dialog.getByText('Goal for FC Doelpuntje')).toBeVisible();
  await expect(dialog.getByLabel('Scorer')).toHaveCount(0);
  await dialog.getByLabel('Minute').fill('12');
  await submitDialog(page, 'Save');

  await expect(page.getByText('Goal updated')).toBeVisible();
  await expect(goalEvent(page)).toHaveCount(1);
  await expect(goalEvent(page).locator('.live-event-min')).toHaveText("12'");
});

test('the corrections are offered to an admin and to nobody else', async ({ page, browser }) => {
  const id = await playedMatch(page, 'FC Bezoeker');

  await expect(halfLengths(page)).toBeVisible();
  await expect(goalEvent(page).getByRole('button', { name: 'Edit' })).toBeVisible();

  const visitor = await browser.newContext({ storageState: VISITOR_STATE });
  const visitorPage = await visitor.newPage();
  await gotoRendered(visitorPage, `/games/${id}/result`);

  await expect(visitorPage.locator('.live-event')).toHaveCount(1);
  await expect(visitorPage.locator('.half-lengths')).toHaveCount(0);
  await expect(visitorPage.getByRole('button', { name: 'Edit' })).toHaveCount(0);

  await visitor.close();
});

// scripts/visual-check.sh measures touch targets, but its seeded match is still under way and this
// row only appears after the final whistle — so the floor is checked here instead, on a context that
// reports a coarse pointer the way a phone does.
test('the half-lengths row clears a thumb on a phone without pushing the page sideways', async ({ page, browser }) => {
  const id = await playedMatch(page, 'FC Smalletjes');

  const phone = await browser.newContext({
    ...devices['Pixel 7'],
    storageState: ADMIN_STATE,
  });
  const phonePage = await phone.newPage();
  await goto(phonePage, `/games/${id}/result`);
  await expect(phonePage.locator('.half-lengths')).toBeVisible();

  const overflows = await phonePage.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(overflows, 'the result page scrolls sideways on a phone').toBe(false);

  const box = await phonePage.evaluate(() => {
    const row = document.querySelector('.half-lengths');
    const value = row.querySelector('.half-lengths-value').getBoundingClientRect();
    const button = row.querySelector('button').getBoundingClientRect();
    return { width: button.width, height: button.height, gap: button.left - value.right };
  });

  expect(box.width).toBeGreaterThanOrEqual(44);
  expect(box.height).toBeGreaterThanOrEqual(44);
  // Nothing, or at least 8px — a 2px gutter between two targets is a dead one.
  expect(box.gap).toBeGreaterThanOrEqual(8);

  await phone.close();
});
