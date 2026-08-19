// Marking a player generally injured: the squad-page status, and what it does to a match's
// line-up — an injured player is never offered a slot, and is shown separately from a player who
// is merely unavailable for one fixture.
import { test, expect } from '../fixtures.js';
import {
  addPlayer, clickFor, createMatch, gameRow, goto, openDialog, playerMenuItem, playerRow, submitDialog,
} from '../helpers.js';

/** Toggles the Injured switch in the open Edit Player dialog and saves. */
async function setInjured(panel, injured) {
  const switchLabel = panel.getByText('Injured player', { exact: true });
  const input = panel.locator('.mud-switch input[type="checkbox"]').last();
  if ((await input.isChecked()) !== injured) await switchLabel.click();
  await submitDialog(panel.page());
}

test('marking a player injured shows the badge, and clearing it removes it', async ({ page }) => {
  await addPlayer(page, { firstName: 'Injury', surname: 'Badge', shirt: 81 });

  await playerMenuItem(page, 'Injury Badge', 'Edit Player');
  await setInjured(await openDialog(page), true);

  const row = playerRow(page, 'Injury Badge');
  await expect(row.locator('.badge-injured')).toBeVisible();

  await playerMenuItem(page, 'Injury Badge', 'Edit Player');
  await setInjured(await openDialog(page), false);
  await expect(row.locator('.badge-injured')).toHaveCount(0);
});

test('an injured player is left out of the line-up and shown in its own panel', async ({ page }) => {
  await addPlayer(page, { firstName: 'Injury', surname: 'Lineup', shirt: 82 });
  await playerMenuItem(page, 'Injury Lineup', 'Edit Player');
  await setInjured(await openDialog(page), true);

  await createMatch(page, { opponent: 'FC Blessuretest' });
  await gameRow(page, 'FC Blessuretest').getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);

  // Not offered as a draggable player...
  await expect(page.locator('.draggable-player', { hasText: 'Injury Lineup' })).toHaveCount(0);
  // ...but visible in the Injured panel, distinct from Unavailable.
  const injuredPanel = page.locator('.injured-player', { hasText: 'Injury Lineup' });
  await expect(injuredPanel).toBeVisible();
});

test('the unavailable-players picker leaves an injured player out, and says why', async ({ page }) => {
  await addPlayer(page, { firstName: 'Injury', surname: 'Picker', shirt: 83 });
  await playerMenuItem(page, 'Injury Picker', 'Edit Player');
  await setInjured(await openDialog(page), true);

  await createMatch(page, { opponent: 'FC Selectietest' });
  await gameRow(page, 'FC Selectietest').getByTitle('Edit', { exact: false }).click();
  const panel = await openDialog(page);

  const field = panel.locator('.mud-input-control', { has: page.getByText('Unavailable Players', { exact: false }) }).first();
  await clickFor(field, () => expect(page.locator('.mud-popover-open .mud-list-item')).not.toHaveCount(0));
  await expect(page.locator('.mud-popover-open').getByText('Injury Picker', { exact: false })).toHaveCount(0);
  await page.keyboard.press('Escape');

  await expect(panel.getByText('injured player(s) not listed', { exact: false })).toBeVisible();
  await panel.getByRole('button', { name: 'Cancel' }).click();
});
