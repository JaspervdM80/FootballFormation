// Registering a training session and who was not at it.
//
// The section is admin-only, unlike the squad and the fixtures — an absence is a personal fact and
// the note beside it usually says why. authorization.spec.js covers what a visitor is shown; this
// covers what the coach does with it.
import { test, expect } from '../fixtures.js';
import { clickFor, confirmDialog, fillField, goto, openDialog, submitDialog } from '../helpers.js';
import { SQUAD } from '../global-setup.js';

const ABSENTEE = `${SQUAD[0].firstName} ${SQUAD[0].surname}`;

/** The card for one session, found by its note — the date is whatever today happens to be. */
const trainingRow = (page, note) => page.locator('.training-row', { hasText: note }).first();

/**
 * Marks one player unavailable in the open dialog.
 *
 * Not `chooseOption`: that waits for the popover to close, and a multi-selection MudSelect keeps
 * its list open so more can be picked. The field is clicked a second time to close it instead —
 * Escape would reach the dialog behind it and cancel the whole form.
 */
async function markUnavailable(page, panel, playerName) {
  const field = panel.locator('.mud-input-control', { has: page.getByText('Unavailable Players', { exact: false }) }).first();
  const option = page.locator('.mud-popover-open .mud-list-item', { hasText: playerName }).first();

  await clickFor(field, () => expect(option).toBeVisible());
  await option.click();
  await field.click();
  await expect(page.locator('.mud-popover-open')).toHaveCount(0);
}

/** The switch is not a checkbox to Playwright — MudBlazor renders its own — so it is found by its label. */
const cancelledSwitch = (panel) => panel.locator('.mud-switch', { hasText: 'Did not take place' }).first();

/** Creates a session through the real dialog. The date comes pre-filled, so a note is all it needs. */
async function addTraining(page, { note, absentee, cancelled } = {}) {
  await goto(page, '/trainings');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  await openDialog(page);
  if (absentee) await markUnavailable(page, panel, absentee);
  if (note) await fillField(panel, 'Notes', note);
  if (cancelled) await cancelledSwitch(panel).click();
  await submitDialog(page);

  await expect(trainingRow(page, note)).toBeVisible();
}

test('a session is listed under the week it falls in', async ({ page }) => {
  await addTraining(page, { note: 'Positiespel in vieren' });

  // The heading is the week, not the month: "per week" is how the team plans, and the group has to
  // say which one rather than just separating the rows.
  const week = page.locator('.training-week', { has: trainingRow(page, 'Positiespel in vieren') });
  await expect(week.locator('.card-label')).toContainText(/Week \d+/);
});

test('a player marked unavailable is counted on the row', async ({ page }) => {
  await addTraining(page, { note: 'Afwerken op doel', absentee: ABSENTEE });

  // The row says how many were out, not who — the names are in the dialog, and a card listing four
  // of them would be unreadable at a glance.
  await expect(trainingRow(page, 'Afwerken op doel').locator('.badge-unavailable')).toHaveText('1 out');
});

test('a note can be corrected afterwards', async ({ page }) => {
  await addTraining(page, { note: 'Verkeerd genoteerd' });

  const panel = page.locator('.mud-dialog');
  await clickFor(trainingRow(page, 'Verkeerd genoteerd').getByTitle('Edit'), () => expect(panel).toBeVisible());
  await fillField(panel, 'Notes', 'Toch conditie gedaan');
  await submitDialog(page);

  await expect(trainingRow(page, 'Toch conditie gedaan')).toBeVisible();
  await expect(page.locator('.training-row', { hasText: 'Verkeerd genoteerd' })).toHaveCount(0);
});

test('a session that did not go ahead is marked, not deleted', async ({ page }) => {
  await addTraining(page, { note: 'Vorst, veld dicht', cancelled: true });

  const row = trainingRow(page, 'Vorst, veld dicht');
  await expect(row.locator('.badge-warning')).toHaveText('Cancelled');
  // The absence count is what the badge replaces: a cancelled evening is not one everybody missed.
  await expect(row.locator('.badge-unavailable')).toHaveCount(0);

  // Re-opened, the form offers no absentees to pick — there is nobody to be absent from a training nobody had.
  const panel = page.locator('.mud-dialog');
  await clickFor(row.getByTitle('Edit'), () => expect(panel).toBeVisible());
  await expect(panel.getByText('Unavailable Players', { exact: false })).toHaveCount(0);
});

test('marking a session cancelled drops the absences it was carrying', async ({ page }) => {
  await addTraining(page, { note: 'Toch afgelast', absentee: ABSENTEE });
  await expect(trainingRow(page, 'Toch afgelast').locator('.badge-unavailable')).toHaveText('1 out');

  const panel = page.locator('.mud-dialog');
  await clickFor(trainingRow(page, 'Toch afgelast').getByTitle('Edit'), () => expect(panel).toBeVisible());
  await cancelledSwitch(panel).click();
  await submitDialog(page);

  const row = trainingRow(page, 'Toch afgelast');
  await expect(row.locator('.badge-warning')).toHaveText('Cancelled');
  await expect(row.locator('.badge-unavailable')).toHaveCount(0);
});

test('a session entered by mistake can be deleted', async ({ page }) => {
  await addTraining(page, { note: 'Ging niet door' });

  await trainingRow(page, 'Ging niet door').getByTitle('Delete').click();
  await confirmDialog(page, 'Delete');

  await expect(page.locator('.training-row', { hasText: 'Ging niet door' })).toHaveCount(0);
});
