// The training period, and the sessions saving it writes.
//
// Deliberately its own file rather than more of trainings.spec.js, and named to sort ahead of it:
// these tests generate a month of evenings and then assert that some of them go away again, which
// only reads cleanly while the season's sessions are the ones this file put there. Every other spec
// that enters a training runs after it.
import { test, expect } from '../fixtures.js';
import { clickFor, goto } from '../helpers.js';

/** A Preferences field, found by its label the way `chooseOption` finds a select. */
const prefsField = (page, label) =>
  page.locator('.mud-input-control', { has: page.getByText(label, { exact: true }) }).first();

/** What a MudSelect or MudDatePicker is showing in its collapsed field. */
const fieldValue = (page, label) => prefsField(page, label).locator('input:not([type="hidden"])').first();

const snackbar = (page, text) => page.locator('.mud-snackbar').filter({ hasText: text });

const savePreferences = (page) => page.getByRole('button', { name: 'Save Preferences' });

/**
 * Ticks days in the training-days select and leaves it closed. Idempotent, because a second click on
 * a day already chosen would tick it off again and both tests here want the same two.
 *
 * Not `chooseOption`: that waits for the popover to close, and a multi-selection MudSelect keeps its
 * list open so more can be picked. The field is clicked a second time to close it instead.
 */
async function chooseTrainingDays(page, ...days) {
  const shown = await fieldValue(page, 'Training Days').inputValue();
  if (days.every(day => shown.includes(day))) return;

  const field = prefsField(page, 'Training Days');
  const optionFor = (day) => page.locator('.mud-popover-open .mud-list-item', { hasText: day }).first();

  await clickFor(field, () => expect(optionFor(days[0])).toBeVisible());
  for (const day of days) await optionFor(day).click();

  await field.click();
  await expect(page.locator('.mud-popover-open')).toHaveCount(0);
}

/**
 * Picks `day` of the current month in one of the two training-period pickers. The input is readonly,
 * as every date field in the app is, so the calendar is the only way in — and it opens on the month
 * it is already showing, which is this one whether or not the field has a value yet.
 */
async function pickThisMonth(page, label, day) {
  const popover = page.locator('.mud-picker-popover.mud-popover-open');

  await clickFor(prefsField(page, label).locator('.mud-input-adornment button').first(),
    () => expect(popover).toBeVisible());
  await popover.locator('.mud-picker-calendar .mud-day:not(.mud-hidden)')
    .filter({ hasText: new RegExp(`^${day}$`) }).first().click();
  await expect(popover).toBeHidden();
}

/**
 * "Mon 06 Apr" — the format the list prints, in the English the suite is pinned to.
 *
 * Built from parts rather than one en-GB call: the app formats under the neutral `en`, whose short
 * September is "Sep", where en-GB alone prints "Sept" and reddens this spec for the whole of September.
 */
const rowDate = (date) => {
  const part = (options) => date.toLocaleDateString('en-US', options);

  return `${part({ weekday: 'short' })} ${String(date.getDate()).padStart(2, '0')} ${part({ month: 'short' })}`;
};

/** The first or last date this month falling on `weekday` (1 = Monday), inside the 1–28 window. */
function mondayThisMonth({ last = false } = {}) {
  const now = new Date();
  const mondays = [];

  for (let day = 1; day <= 28; day++) {
    const date = new Date(now.getFullYear(), now.getMonth(), day);
    if (date.getDay() === 1) mondays.push(date);
  }

  return last ? mondays.at(-1) : mondays[0];
}

test('the training period fills its weeks in, and narrowing it takes the empty ones back out', async ({ page }) => {
  await goto(page, '/settings');
  await chooseTrainingDays(page, 'Monday', 'Wednesday');

  // Days 1–28: four whole weeks, so the run is the same length in whatever month this is run.
  await pickThisMonth(page, 'First Training', 1);
  await pickThisMonth(page, 'Last Training', 28);
  await clickFor(savePreferences(page), () => expect(snackbar(page, /\d+ trainings created/)).toBeVisible());

  const opening = rowDate(mondayThisMonth());
  const closing = rowDate(mondayThisMonth({ last: true }));

  await goto(page, '/trainings');
  await expect(page.locator('.training-row', { hasText: opening })).toHaveCount(1);
  await expect(page.locator('.training-row', { hasText: closing })).toHaveCount(1);

  // Moving the opening day is what takes the evenings before it away again — nothing has been
  // recorded against any of them, so there is nothing to lose.
  await goto(page, '/settings');
  await pickThisMonth(page, 'First Training', 15);
  await clickFor(savePreferences(page), () => expect(snackbar(page, /\d+ removed/)).toBeVisible());

  await goto(page, '/trainings');
  await expect(page.locator('.training-row', { hasText: opening })).toHaveCount(0);
  await expect(page.locator('.training-row', { hasText: closing })).toHaveCount(1);
});

test('the training days read in the language the app is in', async ({ page }) => {
  await goto(page, '/settings');
  await chooseTrainingDays(page, 'Monday', 'Wednesday');
  await clickFor(savePreferences(page), () => expect(snackbar(page, 'Preferences for')).toBeVisible());

  await expect(fieldValue(page, 'Training Days')).toHaveValue('Monday, Wednesday');

  // MudSelectItem's child content styles the open list only: the collapsed field is the converter's,
  // and its default for an enum is ToString(). Without ToStringFunc both of these read English to a
  // Dutch admin — see docs/known_issues/blazor-mudblazor.md.
  await page.goto(`/culture/set?culture=nl&redirectUri=${encodeURIComponent('/')}`);
  await goto(page, '/settings');

  await expect(fieldValue(page, 'Trainingsdagen')).toHaveValue('maandag, woensdag');
  await expect(fieldValue(page, 'Wedstrijddag')).toHaveValue('zaterdag');
});
