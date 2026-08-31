// The season ahead, and the squad that has to be carried into it.
//
// This is the half of squad management with the destructive edge in it. Copying last season's squad
// forward is one click and it *merges* into whatever is already there, which is why Players.razor
// only offers the button while `_squad.Members.Count == 0` — a guard nothing has checked from a
// browser. A button that came back would silently double a roster.
//
// Named to sort after trainings.spec.js on purpose, the way training-schedule.spec.js sorts before
// it: trainings' "editing a session offers the squad of its own season" asserts that the season
// ahead has an empty squad, and this file is what fills it.
import { test, expect } from '../fixtures.js';
import {
  addPlayer, chooseSeasonNamed, clickFor, confirmDialog, currentSeasonName, fillField, goto,
  nextSeasonName, openDialog, pickMidAugustNextYear, playerMenuItem, playerRow, submitDialog,
} from '../helpers.js';

const RETIRED = 'Gestopte Speler';
const CARRIED = 'Fixture Striker';

const squadRows = (page) => page.locator('.players-table .mud-table-body .mud-table-row');
const copyButton = (page) =>
  page.getByRole('button', { name: `Copy squad from ${currentSeasonName()}`, exact: false });

/**
 * Makes sure the season August of next year falls in exists, by filing a training on that date —
 * TrainingService creates the season it lands in. Idempotent: the second call finds it rather than
 * making a second one.
 */
async function ensureNextSeason(page) {
  await goto(page, '/trainings');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  await openDialog(page);
  await pickMidAugustNextYear(page, panel);
  await fillField(panel, 'Notes', 'Startdatum volgend seizoen');
  await submitDialog(page);
}

/** Opens the "Existing player" dialog and reports the names its picker offers, then cancels. */
async function offeredPlayers(page) {
  const existing = page.locator('.mud-popover-open').getByText('Existing player', { exact: true });
  await clickFor(page.getByRole('button', { name: 'Add Player' }), () => expect(existing).toBeVisible());
  await existing.click();

  const panel = await openDialog(page);
  const field = panel.locator('.mud-input-control', { has: page.getByText('Players', { exact: false }) }).first();
  await clickFor(field, () => expect(page.locator('.mud-popover-open .mud-list-item').first()).toBeVisible());

  const offered = await page.locator('.mud-popover-open .mud-list-item').allInnerTexts();
  return { offered, panel, field };
}

test('the season ahead starts empty, offers last season forward, and leaves the archived behind', async ({ page }) => {
  // Archived here rather than borrowed from squad.spec.js: this is the filter under test, and a
  // fixture another file happens to leave behind is not one to lean on.
  await addPlayer(page, { firstName: 'Gestopte', surname: 'Speler', shirt: 86 });
  await playerMenuItem(page, RETIRED, 'Archive player');
  await openDialog(page);
  await confirmDialog(page, 'Archive');
  await expect(playerRow(page, RETIRED).locator('.badge-archived')).toBeVisible();

  await ensureNextSeason(page);
  await chooseSeasonNamed(page, '/players', nextSeasonName());

  // A brand new season starts with nobody in it, and that is the only state the copy is offered in.
  await expect(squadRows(page)).toHaveCount(0);
  await expect(copyButton(page)).toBeVisible();

  const { offered, panel, field } = await offeredPlayers(page);
  expect(offered.some(name => name.includes('Fixture')), 'the picker offered nobody at all')
    .toBe(true);
  // PlayerService.GetAllAsync still returns the archived — only this picker and the copy filter them
  // out, so a regression here reads as a retired player quietly rejoining next season.
  expect(offered.some(name => name.includes(RETIRED)),
    'an archived player was offered to next season').toBe(false);
  await field.click();
  await panel.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('.mud-dialog')).toHaveCount(0);

  await clickFor(copyButton(page), () => expect(squadRows(page)).not.toHaveCount(0));
  await expect(playerRow(page, CARRIED)).toBeVisible();
  await expect(page.locator('.mud-table-row', { hasText: RETIRED })).toHaveCount(0);

  // The guard, and the assertion this spec is really for: with a squad in place the button is gone,
  // so nobody can merge a second roster into it by pressing it twice.
  await expect(copyButton(page)).toHaveCount(0);

  // Still gone after a reload, rather than only in the render that followed the copy.
  await goto(page, '/players');
  await expect(copyButton(page)).toHaveCount(0);
});

test('a member can be taken out of next season and added back as a guest', async ({ page }) => {
  await chooseSeasonNamed(page, '/players', nextSeasonName());
  await expect(playerRow(page, CARRIED)).toBeVisible();

  await playerRow(page, CARRIED).getByLabel('Remove from squad').click();
  await openDialog(page);
  // ConfirmDeleteAsync, so the button says Delete however gently the title puts it.
  await confirmDialog(page, 'Delete');
  await expect(page.locator('.mud-table-row', { hasText: CARRIED })).toHaveCount(0);

  // Out of the squad but not off the books — which is exactly what makes her a candidate again.
  const { offered, panel, field } = await offeredPlayers(page);
  expect(offered.some(name => name.includes('Striker'))).toBe(true);

  await page.locator('.mud-popover-open .mud-list-item', { hasText: 'Striker' }).first().click();
  // The field rather than a count of open popovers: the "Add Player" menu that opened this dialog
  // leaves its own popover behind, so zero is never the answer.
  await expect(field).toContainText('Striker');
  await panel.locator('label.mud-switch', { hasText: 'Guest player' }).click();
  await submitDialog(page, 'Add to squad');

  const row = playerRow(page, CARRIED);
  await expect(row).toBeVisible();
  await expect(row.locator('.badge-guest')).toHaveText('GUEST');
});

test('a season window that would leave a hole is refused, and the season keeps the one it had', async ({ page }) => {
  await goto(page, '/settings');

  // The newest season, which is the one with a neighbour behind it to leave a gap after. Its window
  // is read off the page first, because "unchanged" is the whole assertion.
  const row = page.locator('.list-row', { hasText: nextSeasonName() }).first();
  await expect(row).toBeVisible();
  const window = await row.locator('.list-row-meta').innerText();

  const panel = page.locator('.mud-dialog');
  await clickFor(row.getByTitle('Edit Season'), () => expect(panel).toBeVisible());

  // A season opens on the 1st, so any other day of the same month leaves the previous one ending a
  // fortnight before this one starts — and every date in between belonging to no season at all.
  const popover = page.locator('.mud-picker-popover.mud-popover-open');
  await clickFor(panel.locator('.mud-input-adornment button').first(), () => expect(popover).toBeVisible());
  await popover.locator('.mud-picker-calendar .mud-day:not(.mud-hidden)')
    .filter({ hasText: /^15$/ }).first().click();
  await expect(popover).toBeHidden();
  await submitDialog(page);

  // The service returns a Result failure and the page has to say so — a Result nobody reads looks
  // exactly like success.
  await expect(page.getByText('leaves a gap after', { exact: false })).toBeVisible();
  await goto(page, '/settings');
  await expect(page.locator('.list-row', { hasText: nextSeasonName() }).first().locator('.list-row-meta'))
    .toHaveText(window);
});
