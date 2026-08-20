// Injury, in both the senses the app knows. The standing squad status: an injured player is never
// offered a slot, and is shown separately from a player who is merely unavailable for one fixture.
// And the one that happens on the day: a player marked injured mid-match leaves the pitch, and the
// rest of the match stops counting towards her.
import { test, expect } from '../fixtures.js';
import {
  addPlayer, clickFor, createMatch, gameRow, goto, openDialog, playerMenuItem, playerRow, submitDialog,
} from '../helpers.js';

/** Toggles the Injured switch in the open Edit Player dialog and saves. */
async function setInjured(panel, injured) {
  // Scoped to the switch that carries the "Injured" label, not by position — Guest player sits
  // above it, and this must keep pointing at Injured however many switches join them later.
  // MudSwitch puts the "mud-switch" class on both the root <label> and the inner text <span>, so
  // this has to say <label> explicitly or it matches both and Playwright refuses the ambiguity.
  const injuredSwitch = panel.locator('label.mud-switch', { hasText: 'Injured' });
  const input = injuredSwitch.locator('input[type="checkbox"]');
  if ((await input.isChecked()) !== injured) await injuredSwitch.click();
  await submitDialog(panel.page());
}

test('marking a player injured marks the row, and clearing it removes the mark', async ({ page }) => {
  await addPlayer(page, { firstName: 'Injury', surname: 'Badge', shirt: 81 });

  await playerMenuItem(page, 'Injury Badge', 'Edit Player');
  await setInjured(await openDialog(page), true);

  const row = playerRow(page, 'Injury Badge');
  await expect(row.locator('.injured-mark')).toBeVisible();

  await playerMenuItem(page, 'Injury Badge', 'Edit Player');
  await setInjured(await openDialog(page), false);
  await expect(row.locator('.injured-mark')).toHaveCount(0);
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

/**
 * A match with players on the pitch and the clock running, which is the only state an injury can
 * be recorded from. Two players, so taking one off still leaves the pitch non-empty.
 */
async function liveMatchWithLineup(page, opponent) {
  await createMatch(page, { opponent });
  await gameRow(page, opponent).getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  const available = page.locator('.draggable-player');
  const chips = page.locator('.pitch .pitch-player');
  await expect(available.first()).toBeVisible();
  for (let i = 0; i < 2; i++) {
    await available.first().dragTo(page.locator('.pitch .pitch-empty').first());
    await expect(chips).toHaveCount(i + 1);
  }
  await clickFor(
    page.getByRole('button', { name: /^Save( All Lineups)?$/ }).first(),
    () => expect(page.getByText('All lineups saved', { exact: false })).toBeVisible(),
    { settle: 10_000 },
  );

  await goto(page, `/games/${id}/live`);
  await clickFor(
    page.getByRole('button', { name: 'Start match' }),
    () => expect(page.getByRole('button', { name: 'Finish match' })).toBeVisible(),
  );
  return id;
}

test('a player marked injured mid-match leaves the pitch and can be put back', async ({ page }) => {
  await liveMatchWithLineup(page, 'FC Blessurewissel');

  const chips = page.locator('.live-lineup .pitch-player');
  await expect(chips).toHaveCount(2);

  await clickFor(chips.first(), () => expect(page.locator('.mud-dialog')).toBeVisible());
  const dialog = await openDialog(page);

  // The switch alone is a complete answer: nobody has to come on, which is what the button says
  // before anything is picked from the "Comes on" list.
  await dialog.locator('label.mud-switch', { hasText: 'Injured' }).click();
  await submitDialog(page, 'Off injured');
  // Scoped to the snackbar: the timeline entry below says "Off injured" too.
  await expect(page.locator('.mud-snackbar-content-message', { hasText: 'off injured' })).toBeVisible();

  // She is off, nobody replaced her, and the timeline carries the cross rather than a swap.
  await expect(chips).toHaveCount(1);
  const event = page.locator('.live-event', { hasText: 'not replaced' });
  await expect(event).toHaveCount(1);
  await expect(event.locator('.live-event-injury')).toBeVisible();

  await clickFor(event.getByRole('button'), () => expect(chips).toHaveCount(2));
  await expect(page.locator('.live-event')).toHaveCount(0);
});
