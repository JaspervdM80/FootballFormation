// The season picker's choice is kept in a cookie, because the app is one Fly machine and every
// deploy drops every circuit. None of that is visible from a single page view, which is exactly why
// it needs tests: break the cookie and the app still works, it just quietly forgets again.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import { goto, clickFor } from '../helpers.js';

const COOKIE = 'ff.season';
const EIGHT_HOURS = 8 * 60 * 60;

/** The app-bar picker's button — the MudMenu wrapper it sits in has no size of its own. */
const picker = page => page.locator('.season-picker button').first();

async function chooseAllSeasons(page) {
  const menu = page.locator('.mud-popover-open');
  await clickFor(picker(page), () => expect(menu).toBeVisible());
  await clickFor(menu.getByText('All seasons'), () => expect(menu).toBeHidden());
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

  // Taken before as well as after, so this cannot quietly stop testing anything: the picker's menu
  // items are not in the prerendered markup today, but if MudBlazor ever renders them eagerly the
  // phrase would be in both responses and only the "before" assertion would notice.
  expect(await prerendered(), 'nothing should say "All seasons" before it is chosen')
    .not.toContain('All seasons');

  await chooseAllSeasons(page);

  expect(await prerendered(), 'the prerendered HTML should already carry the stored choice')
    .toContain('All seasons');
});
