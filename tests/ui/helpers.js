// Driving a Blazor Server app from a browser, and the app's own flows on top of that.
//
// The thing that makes this different from testing a normal web page: every page renders twice.
// Blazor serves a static prerender first, then re-renders it once the SignalR circuit connects, and
// only the second one has event handlers attached. Playwright's auto-waiting does not help, because
// the button it is waiting for is right there in the prerender — visible, enabled, and completely
// inert. A click that lands in that window is swallowed with no error anywhere.
//
// So the rule here is: never click and assume. Either wait for the circuit first, or click for an
// outcome and let it retry. There is not a single fixed sleep in this directory, and adding one is
// how the suite starts failing on a slow machine.
import { expect } from '@playwright/test';

// InteractiveShell's marker reads "pending" in the prerender and "live" once the circuit's first render, handlers and all, has landed.
// An interactive page without the shell has no marker, so its inert prerender would pass as ready.
const READY = () => !document.querySelector('[data-circuit="pending"]');

/** Waits for a page reached by a full load — goto, a form post, a redirect — to be interactive rather than merely painted. */
export async function settle(page) {
  await page.waitForLoadState('load');
  await page.waitForFunction(READY, null, { timeout: 30_000 });
  // A layout measured straight after the load would otherwise be measured in the fallback font.
  await page.evaluate(() => document.fonts.ready.then(() => true));
}

/** Navigates, and waits for the page to be interactive. A page with no circuit is ready once loaded. */
export async function goto(page, path) {
  await page.goto(path);
  await settle(page);
}

/**
 * Clicks until it takes. `expectation` is an async assertion describing what the click should have
 * caused; if it has not happened yet the click is repeated, up to `tries`.
 *
 * This is the whole answer to the prerender window, and it is also honest about what a coach does:
 * a button that appears to do nothing gets pressed again.
 */
export async function clickFor(locator, expectation, { tries = 3, settle = 2_000 } = {}) {
  let last;
  for (let attempt = 0; attempt < tries; attempt++) {
    await locator.click({ timeout: 15_000 });
    try {
      await expectation({ timeout: settle });
      return;
    } catch (error) {
      last = error;
    }
  }
  throw last;
}

const dialog = (page) => page.locator('.mud-dialog').last();

/** The open dialog, once it is on screen. Every form in this app is one. */
export async function openDialog(page) {
  const panel = dialog(page);
  await expect(panel).toBeVisible();
  return panel;
}

/**
 * Fills a MudBlazor text or numeric field by its label.
 *
 * MudBlazor associates the two properly, so getByLabel is the right tool — but it matches the
 * label's full text and a required field's label carries a trailing asterisk, hence the substring
 * match rather than an exact one.
 */
export async function fillField(scope, label, value) {
  await scope.getByLabel(label, { exact: false }).first().fill(String(value));
}

/**
 * Picks an option from a MudSelect.
 *
 * A MudSelect is not a <select>: it is a div that opens a popover of list items, so this has to
 * open the one and click the other. The popover is portalled to the end of the body, outside the
 * dialog, which is why the option is looked up on the page rather than in `scope`.
 */
export async function chooseOption(page, scope, label, optionText) {
  const field = scope.locator('.mud-input-control', { has: page.getByText(label, { exact: false }) }).first();
  const option = page.locator('.mud-popover-open .mud-list-item', { hasText: optionText }).first();
  await clickFor(field, () => expect(option).toBeVisible());
  await option.click();
  await expect(page.locator('.mud-popover-open')).toHaveCount(0);
}

/** Submits the open dialog and waits for it to close. */
export async function submitDialog(page, buttonName = 'Save') {
  const panel = dialog(page);
  await clickFor(
    panel.getByRole('button', { name: buttonName, exact: false }),
    () => expect(page.locator('.mud-dialog')).toHaveCount(0),
    { settle: 10_000 },
  );
}

/**
 * Taps a player in a dialog's PlayerPicker, which is the choice and the submit in one. `name` is
 * matched against the start of the full name, which the button carries as its title — it shows only
 * the shirt and the short name.
 */
export async function pickPlayer(page, scope, name = '') {
  const button = scope.locator(name ? `.player-pick[title^="${name}"]` : '.player-pick').first();
  await clickFor(button, () => expect(page.locator('.mud-dialog')).toHaveCount(0), { settle: 10_000 });
}

/** Answers the app's ConfirmDialog. `action` is the confirming button's label. */
export async function confirmDialog(page, action) {
  await submitDialog(page, action);
}

// --- the app's own flows -----------------------------------------------------------------------

/** Adds a brand new player to the current season's squad, through the real dialog. */
export async function addPlayer(page, { firstName, surname = 'Testspeler', shirt }) {
  await goto(page, '/players');
  const addPlayer = page.getByRole('button', { name: 'Add Player' });
  const newPlayer = page.locator('.mud-popover-open').getByText('New player', { exact: true });
  await clickFor(addPlayer, () => expect(newPlayer).toBeVisible());
  await newPlayer.click();

  const panel = await openDialog(page);
  await fillField(panel, 'First Name', firstName);
  await fillField(panel, 'Surname', surname);
  if (shirt !== undefined) await fillField(panel, 'Shirt Number', shirt);
  await submitDialog(page);

  await expect(page.getByText(firstName, { exact: false }).first()).toBeVisible();
}

/** One player's row in the squad table. */
export function playerRow(page, name) {
  return page.locator('.mud-table-body .mud-table-row', { hasText: name }).first();
}

/**
 * Opens an item from a squad row's overflow menu. Editing a person, archiving them and deleting
 * them are rarer than squad changes, so they live behind the "More" button rather than on the row.
 */
export async function playerMenuItem(page, name, item) {
  // MudMenu items are not menuitem-role elements and the activator's aria-label does not survive
  // onto the button, so both ends are found by what they are: the menu's own button in the row, and
  // the item's text in the open popover.
  const activator = playerRow(page, name).locator('.mud-menu button').first();
  const entry = page.locator('.mud-popover-open').getByText(item, { exact: true });
  await clickFor(activator, () => expect(entry).toBeVisible());
  await entry.click();
  // No assertion that the menu closed: what the item opens — a dialog, or its own popover — is the
  // caller's business, and MudMenu leaves its provider element in place either way.
}

/**
 * Creates a match through GameDialog and returns the date it was filed under, or null when that is
 * the dialog's own default. Find it by opponent, which is what the list is keyed on visually. Only
 * the opponent is required; the rest of the form is already filled in from the season's preferences,
 * which is the point of those defaults.
 *
 * `past: true` dates it yesterday through the picker, which is what a match with a result needs —
 * the dialog defaults to the *next* match day, and the result page refuses a score on a fixture
 * still to be played. Callers using it need `test.skip(noEarlierDayThisSeason(), …)`. `past` also
 * takes a day count (e.g. `2`) for a caller that needs two past matches on two distinct dates.
 */
export async function createMatch(page, { opponent, venue, matchType, format, split, past } = {}) {
  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  // Exact, as authorization.spec.js asserts its absence for a visitor: this click is what proves an admin has it.
  await clickFor(page.getByRole('button', { name: 'Add', exact: true }).first(), () => expect(panel).toBeVisible());

  await fillField(panel, 'Opponent', opponent);
  if (format) await chooseOption(page, panel, 'Match Format', format);
  if (venue) await chooseOption(page, panel, 'Venue', venue);
  if (matchType) await chooseOption(page, panel, 'Match Type', matchType);
  // "Quarters" is the split that gives a half two line-ups, and so the only one whose live screen
  // has changes to list partway through a half.
  if (split) await chooseOption(page, panel, 'Game Split', split);
  const date = past ? await pickDaysAgo(page, panel, past === true ? 1 : past) : null;
  await submitDialog(page);

  await expect(gameRow(page, opponent)).toBeVisible();
  return date;
}

function daysBefore(days) {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() - days);
  return date;
}

/** Whether `daysAgo` days back is last season, where a record dated by the date picker drops out of this season's lists. */
export const noEarlierDayThisSeason = (daysAgo = 1) =>
  daysBefore(daysAgo) < new Date(currentSeasonStartYear(), 6, 1);

/**
 * Moves the open dialog's date `daysAgo` days back, through the picker rather than by typing — the
 * field's format follows the culture, and the picker is what a coach uses anyway. Returns the date.
 */
export async function pickDaysAgo(page, scope, daysAgo = 1, { allowUnchanged = false } = {}) {
  const popover = page.locator('.mud-picker-popover.mud-popover-open');
  const field = scope.getByLabel('Date', { exact: false }).first();
  const before = await field.inputValue();
  await clickFor(scope.locator('.mud-input-adornment button').first(), () => expect(popover).toBeVisible());

  // The picker opens on the field's current date, which is the *next* match day and can be in
  // another month — so walk to the target's month first.
  // The header *slides* rather than swapping its text — the element is a
  // .mud-picker-slide-transition — so for a moment after a click it still reads the month just
  // left. Reading again straight away spends a second click on a month already stepped past, which
  // is how this walked to July while asking for August. Each step therefore waits for the text to
  // actually change before the next one reads it, and picks its direction from that settled value
  // so an overshoot walks back rather than spiralling away from the target.
  const target = daysBefore(daysAgo);
  const header = popover.locator('.mud-picker-calendar-header-transition');
  const targetMonth = target.toLocaleString('en-US', { month: 'long', year: 'numeric' });
  for (let step = 0; step < 24; step++) {
    const shown = (await header.innerText()).trim();
    if (shown.toLowerCase() === targetMonth.toLowerCase()) break;
    const goBack = new Date(`1 ${shown}`) > new Date(`1 ${targetMonth}`);
    await popover.getByLabel(goBack ? /^Previous month/ : /^Next month/).click();
    await expect(header).not.toHaveText(shown, { timeout: 5_000 });
  }
  await expect(header).toHaveText(targetMonth, { ignoreCase: true });

  // Days spilling in from the neighbouring months carry .mud-hidden and are not clickable.
  await popover.locator('.mud-picker-calendar .mud-day:not(.mud-hidden)')
    .filter({ hasText: new RegExp(`^${target.getDate()}$`) }).first().click();
  await expect(popover).toBeHidden();

  // Prove the pick landed in the field before anything is submitted — a picker that silently kept
  // its old value would otherwise surface as a confusing failure two assertions later. The check is
  // "it changed" rather than "it reads 8", because the field's format follows the culture and a
  // day number is indistinguishable from a month number in most of them.
  //
  // `allowUnchanged` is for a dialog that does not pre-fill with today: the training one opens on
  // GetNextTrainingDateAsync, which can already be the day being asked for, and clicking a day
  // already selected changes nothing. Its callers have a stronger check downstream — a session that
  // did not land in the past moves no attendance figure.
  if (!allowUnchanged) await expect(field).not.toHaveValue(before);

  return target;
}

/** The card for one match in the games list. */
export function gameRow(page, opponent) {
  return page.locator('.game-row', { hasText: opponent }).first();
}

/**
 * Runs one of a match card's actions: the row's one outright button when that is the action asked
 * for, otherwise the entry of that name in the row's ⋮ menu.
 */
export async function gameAction(page, opponent, name) {
  const row = gameRow(page, opponent);
  // The menu renders in the same pass as the outright button, so once it is up the count below is settled.
  await expect(row.locator('.game-more')).toBeVisible();
  const outright = row.locator('.action-labelled');
  const title = (await outright.count()) ? await outright.first().getAttribute('title') : null;
  if (title && (name instanceof RegExp ? name.test(title) : title.includes(name))) {
    await outright.first().click();
    return;
  }
  await row.locator('.game-more button').click();
  await page.locator('.mud-popover-open .mud-menu-item', { hasText: name }).first().click();
}

/** Opens a match's report from its card and returns the match's id, which the card itself does not carry. */
export async function openOverview(page, opponent) {
  await gameAction(page, opponent, 'Overview');
  await page.waitForURL(/\/games\/\d+\/overview/);
  return Number(page.url().match(/\/games\/(\d+)\//)[1]);
}

/**
 * Moves the open dialog's date to 15 August of the season after this one, through the picker's year
 * and month lists. August because it is always past the 1 July boundary — and the season *after*
 * this one rather than "next calendar year", so the window it creates is contiguous with the current
 * one whichever half of the year the suite runs in. Saving a record on it creates that season.
 */
export async function pickNextSeasonAugust(page, scope) {
  const popover = page.locator('.mud-picker-popover.mud-popover-open');
  await clickFor(scope.locator('.mud-input-adornment button').first(), () => expect(popover).toBeVisible());

  const nextYear = String(nextSeasonStartYear());
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

/** A season's name, the way Season.NameForStartYear writes it: "2026/27". */
const seasonName = (startYear) => `${startYear}/${String((startYear + 1) % 100).padStart(2, '0')}`;

/** The opening year of the season today falls in. A season opens on 1 July, so January is last one's. */
function currentSeasonStartYear() {
  const now = new Date();
  return now.getMonth() >= 6 ? now.getFullYear() : now.getFullYear() - 1;
}

const nextSeasonStartYear = () => currentSeasonStartYear() + 1;

export const currentSeasonName = () => seasonName(currentSeasonStartYear());

/** The season immediately after this one — the one `pickNextSeasonAugust` creates. */
export const nextSeasonName = () => seasonName(nextSeasonStartYear());

/**
 * Switches the app-bar picker to the season named `name`, landing back on `path`. By name rather
 * than by position: the app creates a season as a side effect of dating a record into one, so how
 * many there are is whatever the specs before this one needed. `path` is loaded first because the
 * app bar renders statically — a season a circuit just created is not yet in the picker.
 */
export async function chooseSeasonNamed(page, path, name) {
  await goto(page, path);
  await page.locator('.season-picker > summary').first().click();

  const entry = page.locator('.season-picker .season-menu-item:not(.season-menu-all)')
    .filter({ has: page.getByText(name, { exact: true }) }).first();
  await expect(entry, `the season picker offers no "${name}"`).toBeVisible();
  await entry.click();

  await page.waitForURL(url => !url.pathname.startsWith('/season/set'));
}

// --- match day ----------------------------------------------------------------------------------

/** Creates a match and returns its id, read from the URL its own formation button navigates to. */
export async function matchWithId(page, opponent, options = {}) {
  await createMatch(page, { opponent, ...options });
  await gameAction(page, opponent, /Formation|Add lineup/);
  await page.waitForURL(/\/games\/\d+\/formation/);
  return Number(page.url().match(/\/games\/(\d+)\//)[1]);
}

/**
 * Drags players onto the pitch — an empty slot takes a drop, not a click — and saves, which is easy
 * to miss: a drop only changes what is on screen. Returns how many were placed, which is fewer than
 * the formation has slots and legitimately so.
 */
export async function fillLineup(page, limit = 4) {
  const available = page.locator('.draggable-player');
  const emptySlots = page.locator('.pitch .pitch-empty');
  const chips = page.locator('.pitch .pitch-player');

  await expect(available.first()).toBeVisible();
  const squad = await available.count();
  const placed = Math.min(limit, squad);

  for (let i = 0; i < placed; i++) {
    // Always the first of each: a placed player leaves the list, and a filled slot stops being empty.
    await available.first().dragTo(emptySlots.first());
    await expect(chips).toHaveCount(i + 1);
  }

  await saveLineup(page);
  return { placed, squad };
}

/** Saves the formation builder. The button is "Save All Lineups" whenever the split has more than one. */
export async function saveLineup(page) {
  await clickFor(
    page.getByRole('button', { name: /^Save( All Lineups)?$/ }).first(),
    () => expect(page.getByText('All lineups saved', { exact: false })).toBeVisible(),
    { settle: 10_000 },
  );
}

/** Blows the kick-off whistle on an open live screen. */
export async function startMatch(page) {
  await clickFor(
    page.getByRole('button', { name: 'Start match' }),
    () => expect(page.getByRole('button', { name: 'Finish match' })).toBeVisible(),
  );
}

/** Blows for half time; the controls then offer the second half. */
export async function halfTime(page) {
  const controls = page.locator('.live-controls');
  await clickFor(
    controls.getByRole('button', { name: 'Half time' }),
    () => expect(controls.getByRole('button', { name: 'Start 2nd Half' })).toBeVisible(),
  );
}

/** Kicks off the second half, after which it is no longer on offer. */
export async function startSecondHalf(page) {
  const start = page.locator('.live-controls').getByRole('button', { name: 'Start 2nd Half' });
  await clickFor(start, () => expect(start).toHaveCount(0));
}

/**
 * Blows the final whistle. The confirming button carries the same words as the one that opened it,
 * so the first click is scoped to the control panel or the locator matches both.
 */
export async function finishMatch(page) {
  await clickFor(
    page.locator('.live-controls').getByRole('button', { name: 'Finish match' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  await submitDialog(page, 'Finish match');
  await expect(page.getByRole('button', { name: 'Edit result' })).toBeVisible();
}

/** A match with a saved lineup, on its live screen, with the clock running. Returns its id. */
export async function liveMatch(page, opponent, { placed = 2, ...options } = {}) {
  const id = await matchWithId(page, opponent, options);
  await fillLineup(page, placed);

  await goto(page, `/games/${id}/live`);
  await startMatch(page);
  return id;
}

/** A match played through both halves with one opponent goal in the first, whistled off and left on its result page. */
export async function playedMatch(page, opponent) {
  const id = await liveMatch(page, opponent);
  await clickFor(
    page.getByRole('button', { name: 'Goal against' }),
    () => expect(page.locator('.live-event')).toHaveCount(1),
  );
  await halfTime(page);
  await startSecondHalf(page);
  await finishMatch(page);

  await goto(page, `/games/${id}/result`);
  return id;
}

/** Logs a goal of ours from the live screen, unassisted. `scorer` matches the start of the full name, as in pickPlayer. */
export async function logGoal(page, scorer = '') {
  await clickFor(
    page.getByRole('button', { name: 'Goal', exact: true }),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  const panel = await openDialog(page);
  const noAssist = panel.getByRole('button', { name: 'No assist' });
  const pick = panel.locator(scorer ? `.player-pick[title^="${scorer}"]` : '.player-pick').first();
  await clickFor(pick, () => expect(noAssist).toBeVisible(), { settle: 10_000 });
  await clickFor(noAssist, () => expect(page.locator('.mud-dialog')).toHaveCount(0), { settle: 10_000 });
}

/** Taps the first player on the live pitch and returns the dialog that opens for her. */
export async function tapOnPitch(page) {
  await clickFor(
    page.locator('.live-lineup .pitch-player').first(),
    () => expect(page.locator('.mud-dialog')).toBeVisible(),
  );
  return openDialog(page);
}

/** Takes the first player on the live pitch off for whoever the dialog offers first. */
export async function substitute(page) {
  await pickPlayer(page, await tapOnPitch(page));
}

/** Takes the first player on the live pitch off injured, with nobody coming on for her. */
export async function offInjured(page) {
  const panel = await tapOnPitch(page);
  await panel.locator('label.mud-switch', { hasText: 'Injured' }).click();
  await submitDialog(page, 'Off injured');
}

/**
 * Files a score for a match already dated in the past, turning it from a fixture into a result.
 * `us` and `them` in that order, whatever the venue does to the boxes: the scoreboard puts the home
 * side first, so on an away match the *opponent's* box is the one that renders first.
 */
export async function fileScore(page, id, us, them) {
  await goto(page, `/games/${id}/result`);
  await page.locator('.score-big-input:not(.score-away)').fill(String(us));
  await page.locator('.score-big-input.score-away').fill(String(them));
  await clickFor(
    page.getByRole('button', { name: 'Save Score' }),
    () => expect(page.getByText('saved', { exact: false }).first()).toBeVisible(),
  );
}
