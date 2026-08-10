// Managing a season's squad through the dialogs a coach actually uses.
import { test, expect } from '../fixtures.js';
import {
  addPlayer, clickFor, confirmDialog, fillField, goto, openDialog, playerMenuItem, playerRow, submitDialog,
} from '../helpers.js';

const squadRows = (page) => page.locator('.mud-table-body .mud-table-row');

test('a new player joins the squad and the count follows', async ({ page }) => {
  await goto(page, '/players');
  const before = await squadRows(page).count();

  await addPlayer(page, { firstName: 'Nieuwe', surname: 'Aanwinst', shirt: 77 });

  const row = playerRow(page, 'Nieuwe Aanwinst');
  await expect(row).toBeVisible();
  await expect(row).toContainText('77');
  await expect(squadRows(page)).toHaveCount(before + 1);
});

test('a player without a first name is refused, and the dialog stays open to say so', async ({ page }) => {
  await goto(page, '/players');
  const menu = page.locator('.mud-popover-open').getByText('New player', { exact: true });
  await clickFor(page.getByRole('button', { name: 'Add Player' }), () => expect(menu).toBeVisible());
  await menu.click();

  const panel = await openDialog(page);
  await fillField(panel, 'Surname', 'Zonder Voornaam');
  await panel.getByRole('button', { name: 'Save' }).click();

  // Still open, and saying why — not silently dropped.
  await expect(panel).toBeVisible();
  await expect(page.getByText('First name is required', { exact: false })).toBeVisible();

  await panel.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('.mud-dialog')).toHaveCount(0);
});

test('tapping a player opens their statistics', async ({ page }) => {
  await goto(page, '/players');
  await page.getByText('Fixture Striker', { exact: false }).first().click();

  await expect(page).toHaveURL(/\/players\/\d+\/stats/);
  await expect(page.getByText('Fixture Striker', { exact: false }).first()).toBeVisible();
});

test('an edit sticks after a reload, so it reached the database', async ({ page }) => {
  await addPlayer(page, { firstName: 'Bewerk', surname: 'Mij', shirt: 78 });

  await playerMenuItem(page, 'Bewerk Mij', 'Edit Player');
  const panel = await openDialog(page);
  await fillField(panel, 'Surname', 'Bewerkt');
  await submitDialog(page);

  await goto(page, '/players');
  await expect(playerRow(page, 'Bewerk Bewerkt')).toBeVisible();
});

test('archiving retires a player without erasing the seasons they played', async ({ page }) => {
  await addPlayer(page, { firstName: 'Vertrokken', surname: 'Speler', shirt: 79 });

  await playerMenuItem(page, 'Vertrokken Speler', 'Archive player');
  // Archiving is a decision about the future, so it always asks before taking someone off the
  // picker — `ToggleArchived` only skips the confirm when *restoring*, and this player is new.
  // Waited for rather than counted: `.count()` is the one query in this suite that does not
  // auto-wait, so on a loaded runner it read zero before the dialog had rendered, skipped the
  // confirm, and left the player un-archived — and the badge assertion below is what noticed.
  await openDialog(page);
  await confirmDialog(page, 'Archive');

  await goto(page, '/players');
  const row = playerRow(page, 'Vertrokken Speler');
  // Still listed — the point of archiving instead of deleting is that last season keeps its squad.
  await expect(row).toBeVisible();
  await expect(row.locator('.badge-archived')).toBeVisible();
});
