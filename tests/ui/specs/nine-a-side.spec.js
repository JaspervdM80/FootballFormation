// Picking nine-a-side in the match dialog, and the shorter pitch that follows it.
import { test, expect } from '../fixtures.js';
import {
  chooseOption, clickFor, fillField, gameRow, goto, openDialog, submitDialog,
} from '../helpers.js';

/** Opens the match dialog on /games, already switched to the given format. */
async function openDialogInFormat(page, format) {
  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  await chooseOption(page, panel, 'Match Format', format);
  return panel;
}

test('the match format picks the shapes the formation picker offers', async ({ page }) => {
  const panel = await openDialogInFormat(page, '9 vs 9');

  // Switching format leaves a shape that fields nine, not the eleven-a-side one the season defaults to.
  const formation = panel.locator('.mud-input-control', { hasText: 'Formation' }).first();
  await expect(formation).toContainText('3-3-2');

  const options = page.locator('.mud-popover-open .mud-list-item');
  await clickFor(formation, () => expect(options.first()).toBeVisible());

  // Every shape offered adds up to eight outfield players, so 4-4-2 is not among them.
  const names = await options.allInnerTexts();
  expect(names.length).toBeGreaterThan(0);
  for (const name of names) {
    expect(name.split('-').map(Number).reduce((a, b) => a + b, 0)).toBe(8);
  }
});

test('a nine-a-side match is built on a pitch with nine slots', async ({ page }) => {
  const panel = await openDialogInFormat(page, '9 vs 9');
  await fillField(panel, 'Opponent', 'FC Negental');
  await submitDialog(page);

  await expect(gameRow(page, 'FC Negental')).toBeVisible();
  await expect(gameRow(page, 'FC Negental').locator('.badge-gold')).toHaveText('3-3-2');

  await gameRow(page, 'FC Negental').getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);

  await expect(page.locator('.pitch .pitch-slot')).toHaveCount(9);
  await expect(page.locator('.pitch .pitch-empty .pitch-label').first()).toHaveText('GK');
});

test('switching an eleven-a-side match to nine benches the starters it has no slot for', async ({ page }) => {
  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());
  await fillField(panel, 'Opponent', 'FC Omschakeling');
  await submitDialog(page);

  await gameRow(page, 'FC Omschakeling').getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);

  // Filled from the back of the pitch, so the first ones placed are the striker slots nine-a-side
  // drops — the point is that nobody ends up a starter with nowhere to stand. Capped at the slots
  // there are: the squad grows with whatever specs ran before this one.
  const available = page.locator('.draggable-player');
  const emptySlots = page.locator('.pitch .pitch-empty');
  await expect(available.first()).toBeVisible();

  const placed = Math.min(await available.count(), await emptySlots.count());
  for (let i = 0; i < placed; i++) {
    await available.first().dragTo(emptySlots.last());
    await expect(page.locator('.pitch .pitch-player')).toHaveCount(i + 1);
  }
  await clickFor(
    page.getByRole('button', { name: /^Save( All Lineups)?$/ }).first(),
    () => expect(page.getByText('All lineups saved', { exact: false })).toBeVisible(),
    { settle: 10_000 },
  );

  await goto(page, '/games');
  await gameRow(page, 'FC Omschakeling').getByTitle('Edit', { exact: false }).click();
  const edit = await openDialog(page);
  await chooseOption(page, edit, 'Match Format', '9 vs 9');
  await submitDialog(page);

  await gameRow(page, 'FC Omschakeling').getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);

  await expect(page.locator('.pitch .pitch-slot')).toHaveCount(9);
  // Nobody was dropped from the line-up: whoever the smaller pitch has no slot for is on the bench.
  const onPitch = await page.locator('.pitch .pitch-player').count();
  const onBench = await page.locator('.sub-list .sub-item').count();
  expect(onPitch + onBench).toBe(placed);
});

test('the season preferences pick a format for every match that follows', async ({ page }) => {
  await goto(page, '/preferences');
  // The page itself: there is no dialog to scope to here, and MudAppBar is a MudPaper too, so
  // `.mud-paper` first is the chrome rather than the preferences card.
  const prefs = page.locator('body');

  await chooseOption(page, prefs, 'Default Match Format', '9 vs 9');
  await expect(prefs.locator('.mud-input-control', { hasText: 'Default Formation' }).first()).toContainText('3-3-2');
});
