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

// Blazor's client renderer writes a `_bl_<guid>` attribute onto every element it has wired an event
// handler to. Nothing else in the DOM distinguishes a hydrated page from its prerender, and the two
// obvious candidates are both wrong — measured on /settings in this app:
//
//   domcontentloaded   window.Blazor is already true, 0 of 12 buttons have handlers
//   first circuit frame   still 0 of 12 — that frame is the handshake, not a render
//   _bl_ attributes present   15 handlers bound, ~230ms in
//
// So `window.Blazor` says the script loaded, not that anything works. Waiting on it is why the
// seeded-password step filled three inputs the server never heard about and then submitted the
// form it was prerendered with.
//
// **What it does not see.** Blazor writes that attribute for handlers it has to register on the
// element itself, which in practice means MudBlazor's own controls — a plain `<button @onclick>`
// or a `<div @onclick>` of ours never gets one, measured on /games. So this is really "MudBlazor
// has rendered an interactive control", and a page that renders none for the current visitor
// satisfies it never. That used to be impossible, because the chrome carried a MudIconButton on
// every page; the chrome renders statically now, so it is the page's own controls or nothing —
// and for an anonymous visitor several pages have none. Those call sites use gotoRendered.
const HANDLERS_BOUND = () => [...document.querySelectorAll('button,a,input')]
  .some(el => el.getAttributeNames().some(name => name.startsWith('_bl_')));

/** Navigates, and waits for the page to be *interactive* rather than merely painted. */
export async function goto(page, path) {
  await page.goto(path, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(HANDLERS_BOUND, null, { timeout: 30_000 });
}

/**
 * Navigates and waits for the markup only, for a page with no handler to wait for.
 *
 * Two kinds of page qualify, and neither is broken:
 *   - a page rendered without a circuit at all;
 *   - a page that *is* interactive but whose only handlers are splatted onto MudBlazor components
 *     (`@onclick` on a MudPaper, `OnRowClick` on a MudTable). Those work — the click navigates —
 *     but Blazor stamps `_bl_` only on handlers declared on an HTML element, so `goto` would wait
 *     thirty seconds for a signal that is never coming.
 *
 * Do not reach for this to make a flaky click pass: on a page that does bind handlers, waiting for
 * them is the whole point, and skipping the wait puts the click back in the prerender window.
 */
export async function gotoRendered(page, path) {
  await page.goto(path, { waitUntil: 'domcontentloaded' });
  // Until the page stops fetching, not merely until it paints. A page that does open a circuit
  // starts negotiating during this window, and navigating away mid-handshake aborts it — which
  // surfaces as a "Failed to complete negotiation" console error and fails the run. WebSockets do
  // not count towards networkidle, so an established circuit does not hold this open.
  await page.waitForLoadState('networkidle');
}

/**
 * Waits for one element to have its handlers attached.
 *
 * Only needed for the first render of a page — anything the circuit draws afterwards (a dialog, a
 * popover) arrives with its handlers in the same batch, so its existence is proof enough.
 */
export async function waitForHandlers(locator) {
  await expect.poll(
    () => locator.evaluate(el => el.getAttributeNames().some(name => name.startsWith('_bl_'))),
    { timeout: 30_000 },
  ).toBe(true);
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
 * Creates a match through GameDialog and returns the day-of-month it was filed under. Find it by
 * opponent, which is what the list is keyed on visually. Only the opponent is required; the rest of
 * the form is already filled in from the season's preferences, which is the point of those defaults.
 *
 * `past: true` moves the date back through the picker, which is what a match with a result needs —
 * the dialog defaults to the *next* match day, and the result page refuses a score on a fixture
 * still to be played. Callers using it need `test.skip(new Date().getDate() === 1, …)`; see
 * pickEarlierThisMonth. `past` also takes a day count (e.g. `2`) for a caller that needs two past
 * matches on two distinct dates, ordered against each other.
 */
export async function createMatch(page, { opponent, venue, matchType, split, past } = {}) {
  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  await fillField(panel, 'Opponent', opponent);
  if (venue) await chooseOption(page, panel, 'Venue', venue);
  if (matchType) await chooseOption(page, panel, 'Match Type', matchType);
  // "Quarters" is the split that gives a half two line-ups, and so the only one whose live screen
  // has changes to list partway through a half.
  if (split) await chooseOption(page, panel, 'Game Split', split);
  const day = past ? await pickEarlierThisMonth(page, panel, past === true ? 1 : past) : null;
  await submitDialog(page);

  await expect(gameRow(page, opponent)).toBeVisible();
  return day;
}

/**
 * Moves the open match dialog's date to a day earlier in the current month, through the picker
 * rather than by typing — the field's format follows the culture, and the picker is what a coach
 * uses anyway.
 *
 * Staying inside the current month is deliberate: the season is chosen from the date ("Auto (by
 * date)"), so a jump to a previous month could file the match under last season and take it out of
 * the list the test is about to look at.
 */
export async function pickEarlierThisMonth(page, scope, daysAgo = 1) {
  const popover = page.locator('.mud-picker-popover.mud-popover-open');
  const field = scope.getByLabel('Date', { exact: false }).first();
  const before = await field.inputValue();
  await clickFor(scope.locator('.mud-input-adornment button').first(), () => expect(popover).toBeVisible());

  // The picker opens on the match's current date, which is the *next* match day and can be in a
  // later month — so walk back to this one first. Without this the helper picks day N of whatever
  // month it happened to open on, which is how a "match already played" ended up in the future
  // while every assertion about it still passed.
  // The header *slides* rather than swapping its text — the element is a
  // .mud-picker-slide-transition — so for a moment after a click it still reads the month just
  // left. Reading again straight away spends a second click on a month already stepped past, which
  // is how this walked to July while asking for August. Each step therefore waits for the text to
  // actually change before the next one reads it, and picks its direction from that settled value
  // so an overshoot walks back rather than spiralling away from the target.
  const header = popover.locator('.mud-picker-calendar-header-transition');
  const thisMonth = new Date().toLocaleString('en-US', { month: 'long', year: 'numeric' });
  for (let step = 0; step < 24; step++) {
    const shown = (await header.innerText()).trim();
    if (shown.toLowerCase() === thisMonth.toLowerCase()) break;
    const goBack = new Date(`1 ${shown}`) > new Date(`1 ${thisMonth}`);
    await popover.getByLabel(goBack ? /^Previous month/ : /^Next month/).click();
    await expect(header).not.toHaveText(shown, { timeout: 5_000 });
  }
  await expect(header).toHaveText(thisMonth, { ignoreCase: true });

  const today = new Date().getDate();
  const day = today > daysAgo ? today - daysAgo : 1;
  // Days spilling in from the neighbouring months carry .mud-hidden and are not clickable.
  await popover.locator('.mud-picker-calendar .mud-day:not(.mud-hidden)')
    .filter({ hasText: new RegExp(`^${day}$`) }).first().click();
  await expect(popover).toBeHidden();

  // Prove the pick landed in the field before anything is submitted — a picker that silently kept
  // its old value would otherwise surface as a confusing failure two assertions later. The check is
  // "it changed" rather than "it reads 8", because the field's format follows the culture and a
  // day number is indistinguishable from a month number in most of them.
  await expect(field).not.toHaveValue(before);

  return day;
}

/** The card for one match in the games list. */
export function gameRow(page, opponent) {
  return page.locator('.game-row', { hasText: opponent }).first();
}

/** Opens one of a match card's action buttons — they are titled, which is how a coach finds them. */
export async function gameAction(page, opponent, title) {
  await gameRow(page, opponent).getByTitle(title, { exact: false }).click();
}
