// Registering a training session and who was not at it.
//
// The section is admin-only, unlike the squad and the fixtures — an absence is a personal fact and
// the note beside it usually says why. authorization.spec.js covers what a visitor is shown; this
// covers what the coach does with it.
import { test, expect } from '../fixtures.js';
import { clickFor, confirmDialog, fillField, goto, openDialog, pickEarlierThisMonth, submitDialog } from '../helpers.js';
import { SQUAD } from '../global-setup.js';

const ABSENTEE = `${SQUAD[0].firstName} ${SQUAD[0].surname}`;
const PRESENT = `${SQUAD[1].firstName} ${SQUAD[1].surname}`;

/** The card for one session, found by its note — the date is whatever the dialog offered. */
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

/**
 * Creates a session through the real dialog. The date comes pre-filled with the next training date,
 * so a note is all it needs — except for the attendance tests, which pass `past` to put the session
 * behind us, because only a session that has been and gone counts towards the figure.
 */
async function addTraining(page, { note, absentee, cancelled, past } = {}) {
  await goto(page, '/trainings');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  await openDialog(page);
  // Before the absentees: changing the date reloads the season's squad behind the picker.
  if (past) await pickEarlierThisMonth(page, panel, 1, { allowUnchanged: true });
  if (absentee) await markUnavailable(page, panel, absentee);
  if (note) await fillField(panel, 'Notes', note);
  if (cancelled) await cancelledSwitch(panel).click();
  await submitDialog(page);

  await expect(trainingRow(page, note)).toBeVisible();
}

/** The unavailable-players select in the open dialog, and the options it is offering. */
const absenteeField = (page, panel) =>
  panel.locator('.mud-input-control', { has: page.getByText('Unavailable Players', { exact: false }) }).first();

/**
 * Moves the open dialog's date to 15 August of next year, through the picker's year and month lists
 * rather than by walking twelve headers. The field itself is readonly, so the calendar is the only
 * way in.
 *
 * August of next year rather than any date a year out: a season runs 1 July – 30 June, so August is
 * always past the boundary and the date always lands in a season that is not today's, whatever month
 * the suite happens to run in.
 */
async function pickMidAugustNextYear(page, panel) {
  const popover = page.locator('.mud-picker-popover.mud-popover-open');
  await clickFor(panel.locator('.mud-input-adornment button').first(), () => expect(popover).toBeVisible());

  const nextYear = String(new Date().getFullYear() + 1);
  await clickFor(popover.locator('.mud-picker-datepicker-toolbar .mud-button-root').first(),
    () => expect(popover.locator('.mud-picker-year').first()).toBeVisible());
  await popover.locator('.mud-picker-year').filter({ hasText: new RegExp(`^${nextYear}$`) }).first().click();

  // The year list hands over to the month grid, and that to the days.
  await expect(popover.locator('.mud-picker-month').first()).toBeVisible();
  await popover.locator('.mud-picker-month').filter({ hasText: /^aug/i }).first().click();
  await popover.locator('.mud-picker-calendar .mud-day:not(.mud-hidden)')
    .filter({ hasText: /^15$/ }).first().click();
  await expect(popover).toBeHidden();
}

/**
 * Switches the season picker to the season that is not the current one.
 *
 * Found by the absence of the "Current" badge rather than by name: the summary shows the short name
 * ("26/27") and the menu the full one ("2026/27"), so matching one against the other excludes both.
 */
async function chooseSeason(page, { current }) {
  // A page load first: the app bar renders statically, so a season created through the circuit is
  // not in the picker the chrome around it is still showing.
  await goto(page, '/trainings');
  await page.locator('.season-picker > summary').first().click();

  const badge = page.locator('.badge-teal');
  const entries = page.locator('.season-picker .season-menu-item:not(.season-menu-all)');
  const entry = (current ? entries.filter({ has: badge }) : entries.filter({ hasNot: badge })).first();
  await expect(entry).toBeVisible();
  await entry.click();

  // A link, not a handler: choosing a season is a navigation through /season/set and back.
  await page.waitForURL(url => !url.pathname.startsWith('/season/set'));
}

const chooseNonCurrentSeason = (page) => chooseSeason(page, { current: false });
const chooseCurrentSeason = (page) => chooseSeason(page, { current: true });

/** Opens a session for editing and reports the players its absentee picker offers, then closes it. */
async function offeredAbsentees(page, note) {
  const panel = page.locator('.mud-dialog');
  await clickFor(trainingRow(page, note).getByTitle('Edit'), () => expect(panel).toBeVisible());
  await absenteeField(page, panel).click();

  const offered = await page.locator('.mud-popover-open .mud-list-item').allInnerTexts();

  // The select's own popover is still up, and its overlay swallows the click on Cancel behind it.
  await absenteeField(page, panel).click();
  await expect(page.locator('.mud-popover-open')).toHaveCount(0);

  await panel.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('.mud-dialog')).toHaveCount(0);
  return offered;
}

test('editing a session offers the squad of its own season, not of today', async ({ page }) => {
  await addTraining(page, { note: 'Dit seizoen' });

  // A date past the next season boundary lands in a season that does not exist yet, so
  // TrainingService creates it — and a brand new season starts with an empty squad, which is what
  // makes the two answers tell apart.
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());
  await pickMidAugustNextYear(page, panel);
  await fillField(panel, 'Notes', 'Volgend seizoen');
  await submitDialog(page);

  await chooseNonCurrentSeason(page);
  await expect(trainingRow(page, 'Volgend seizoen')).toBeVisible();
  const nextSeason = await offeredAbsentees(page, 'Volgend seizoen');

  // Both halves through the same helper, so the empty one cannot pass by a click that quietly missed:
  // this season's session has to offer somebody for next season's offering nobody to mean anything.
  await goto(page, '/trainings');
  await chooseCurrentSeason(page);
  const thisSeason = await offeredAbsentees(page, 'Dit seizoen');

  expect(thisSeason.length, 'this season offered nobody — is the picker being opened at all?')
    .toBeGreaterThan(0);
  // The dialog used to resolve the squad before the session's own season had been read off the
  // parameter, so it answered with whichever season today falls in.
  expect(nextSeason, 'next season has an empty squad, so its session must offer nobody').toEqual([]);
});

test('a session is listed under the week it falls in', async ({ page }) => {
  await addTraining(page, { note: 'Positiespel in vieren' });

  // The heading is the week, not the month: "per week" is how the team plans, and the group has to
  // say which one rather than just separating the rows.
  const week = page.locator('.training-week', { has: trainingRow(page, 'Positiespel in vieren') });
  await expect(week.locator('.card-label')).toContainText(/Week \d+/);
});

test('a player marked unavailable is counted on the row and named in its tooltip', async ({ page }) => {
  await addTraining(page, { note: 'Afwerken op doel', absentee: ABSENTEE });

  // The row says how many were out, not who — a card listing four names would be unreadable at a
  // glance — and the badge carries them in its tooltip, so who is one hover away rather than a
  // dialog away.
  const badge = trainingRow(page, 'Afwerken op doel').locator('.badge-unavailable');
  await expect(badge).toHaveText('1 out');
  await expect(badge).toHaveAttribute('title', ABSENTEE);
});

test('the form names the absentees it is only able to count', async ({ page }) => {
  await addTraining(page, { note: 'Wie was er niet', absentee: ABSENTEE });

  // The picker itself shows "1 player(s) unavailable" and nothing else, so who was away was a
  // dropdown away from a coach reading the evening back.
  const panel = page.locator('.mud-dialog');
  await clickFor(trainingRow(page, 'Wie was er niet').getByTitle('Edit'), () => expect(panel).toBeVisible());
  await expect(panel.locator('.mud-typography-caption', { hasText: ABSENTEE })).toBeVisible();
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

/**
 * The attendance disclosure above the weeks, opened.
 *
 * A <details> keeps its rows in the DOM while closed, so they have to be revealed before anything
 * can be read off them. Not `clickFor`: a second click would close it again.
 */
async function openAttendance(page) {
  const panel = page.locator('details.attendance');
  await expect(panel).toBeVisible();

  if (!(await panel.evaluate(details => details.open))) await panel.locator('summary').click();
  await expect(panel.locator('.attendance-rows')).toBeVisible();
  return panel;
}

/** "7 sessions held" → 7. */
async function sessionsHeld(page) {
  const summary = await page.locator('details.attendance .attendance-sub').innerText();
  return Number(summary.match(/\d+/)[0]);
}

/** "6 / 7 · 86%" → { attended: 6, held: 7 }. */
async function attendanceOf(page, playerName) {
  const panel = await openAttendance(page);
  const row = panel.locator('.attendance-row', { hasText: playerName }).first();
  const [attended, held] = (await row.locator('.a-meta').innerText()).match(/\d+/g).map(Number);
  return { attended, held };
}

// Every figure below is read as a difference rather than as a number: the specs share one database,
// so the season already holds whatever the tests before this one entered.
//
// Every session here is dated into the past. Only sessions that have already been held count, so one
// added on today's date would move nothing — and `pickEarlierThisMonth` stays inside the current
// month, because the season is derived from the date and a jump back could file it under last one.
// On the 1st there is no earlier day in the month to use, which is what `noPastDayThisMonth` skips.
//
// The dialog does not open on today — it opens on the next training date, which is already yesterday
// once a training period has been saved — so the pick is allowed to leave the field as it found it.
const noPastDayThisMonth = () => new Date().getDate() === 1;

test('a session that did not take place is left out of the attendance', async ({ page }) => {
  test.skip(noPastDayThisMonth(), 'no earlier day this month to hold a session on');

  await addTraining(page, { note: 'Opkomst: gehouden', past: true });
  const before = await sessionsHeld(page);

  await addTraining(page, { note: 'Opkomst: afgelast', cancelled: true, past: true });

  // Counting it would punish the whole squad for an evening nobody had — and an empty absence list
  // is all a cancelled session and a fully attended one have in common.
  expect(await sessionsHeld(page), 'a cancelled evening moved the denominator').toBe(before);

  await addTraining(page, { note: 'Opkomst: nog een', past: true });
  expect(await sessionsHeld(page)).toBe(before + 1);
});

test('a player opened from the register comes back to the register', async ({ page }) => {
  test.skip(noPastDayThisMonth(), 'no earlier day this month to hold a session on');

  await addTraining(page, { note: 'Opkomst: en terug', past: true });
  const panel = await openAttendance(page);
  await panel.locator('.attendance-row', { hasText: PRESENT }).first().click();
  await expect(page).toHaveURL(/\/players\/\d+\/stats/);

  // /players is this page's fallback, so the squad is also the answer a trail that knows nothing
  // gives — which is what it gave for every in-app link while the trail was read off the Referer.
  const back = page.locator('a.back-button').first();
  await expect(back).toHaveAttribute('href', '/trainings');
  await expect(back).toHaveAttribute('title', 'Back to Trainings');
});

test('a player marked unavailable loses that session from her attendance', async ({ page }) => {
  test.skip(noPastDayThisMonth(), 'no earlier day this month to hold a session on');

  await addTraining(page, { note: 'Opkomst: nulmeting', past: true });
  const absenteeBefore = await attendanceOf(page, ABSENTEE);
  const presentBefore = await attendanceOf(page, PRESENT);

  await addTraining(page, { note: 'Opkomst: eentje afwezig', absentee: ABSENTEE, past: true });

  // Attendance is the squad minus the absentees, so the session counts for everyone and is attended
  // by everyone the register did not name.
  expect(await attendanceOf(page, ABSENTEE)).toEqual({
    attended: absenteeBefore.attended,
    held: absenteeBefore.held + 1,
  });
  expect(await attendanceOf(page, PRESENT)).toEqual({
    attended: presentBefore.attended + 1,
    held: presentBefore.held + 1,
  });
});
