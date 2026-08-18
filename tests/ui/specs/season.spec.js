// The season picker's choice is kept in a cookie, because the app is one Fly machine and every
// deploy drops every circuit. None of that is visible from a single page view, which is exactly why
// it needs tests: break the cookie and the app still works, it just quietly forgets again.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import { goto } from '../helpers.js';

const COOKIE = 'ff.season';
const EIGHT_HOURS = 8 * 60 * 60;

/** The app-bar picker's summary — the <details> it sits in wraps the open menu too. */
const picker = page => page.locator('.season-picker > summary').first();

// The entries are links to /season/set, so choosing one is a navigation and not a click to retry:
// clickFor would press a second time while the first was still in flight.
async function chooseAllSeasons(page) {
  await picker(page).click();
  await page.locator('.season-picker .season-menu-all').first().click();
  await page.waitForURL(url => !url.pathname.startsWith('/season/set'));
  await expect(picker(page)).toContainText('All seasons');
}

test('the season chosen is still the season after a reload', async ({ page }) => {
  await goto(page, '/games');
  // Proves the reload is telling us something: the picker does not start on "All seasons".
  await expect(picker(page)).not.toContainText('All seasons');

  await chooseAllSeasons(page);

  await goto(page, '/games');
  await expect(picker(page)).toContainText('All seasons');
});

test('the choice is written as a cookie that lapses after eight hours', async ({ page }) => {
  await goto(page, '/games');
  await chooseAllSeasons(page);

  const [cookie] = (await page.context().cookies()).filter(c => c.name === COOKIE);
  expect(cookie, `no ${COOKIE} cookie — the choice is not being stored at all`).toBeTruthy();
  expect(cookie.path).toBe('/');

  // Eight hours, give or take the seconds spent getting here. A wrong lifetime is invisible in a
  // browser — the choice sticks either way — so the number itself is what gets asserted.
  const hours = (cookie.expires - Date.now() / 1000) / 3600;
  expect(hours, 'the cookie should expire in about eight hours').toBeGreaterThan(7.9);
  expect(hours).toBeLessThanOrEqual(8);
  expect(cookie.expires - Date.now() / 1000).toBeLessThanOrEqual(EIGHT_HOURS);
});

test('the server renders the stored season, without waiting to be told by the browser', async ({ page }) => {
  // Fetched over HTTP rather than looked at in the page: this is the response the server writes
  // before any circuit exists, so what it already says is what the visitor sees on first paint.
  // Reading the cookie over JS interop instead would leave this saying the current season and only
  // correct it once the circuit connected — a flash, and a round trip in front of the first
  // interactive render. The request carries the context's cookies, the same as a real reload.
  const prerendered = async () =>
    (await page.context().request.get(`${BASE_URL}/games`)).text();

  await goto(page, '/games');

  // The picker's own label, not the phrase anywhere on the page: every entry is a plain link now,
  // so "All seasons" is in the markup whether or not it is the choice. Taken before as well as
  // after, so this cannot quietly stop testing anything.
  const LABEL = '<span class="season-picker-label">All seasons</span>';

  expect(await prerendered(), 'the picker should not read "All seasons" before it is chosen')
    .not.toContain(LABEL);

  await chooseAllSeasons(page);

  expect(await prerendered(), 'the rendered HTML should already carry the stored choice')
    .toContain(LABEL);
});

// /players offers its own way into a season when "All seasons" is chosen, and it is the one link to
// /season/set that is not in the picker. It went dead once already: enhanced navigation kept the
// page's circuit on the old season while the app bar showed the new one, so the button visibly did
// nothing. Target="_self" is not an opt-out — only data-enhance-nav="false" is.
test('the squad page offers a way out of "All seasons", and taking it works', async ({ page, context }) => {
  await context.addCookies([{ name: COOKIE, value: 'all', url: BASE_URL }]);
  await goto(page, '/players');
  await expect(page.getByText('Select a season to manage its squad.')).toBeVisible();

  await page.getByRole('link', { name: /Show/ }).click();
  await page.waitForURL(url => !url.pathname.startsWith('/season/set'));

  // The squad itself, not just the picker's label: the label is rendered by the static chrome and
  // would have flipped either way, which is exactly how this hid.
  await expect(page.locator('.players-table')).toBeVisible();
});
