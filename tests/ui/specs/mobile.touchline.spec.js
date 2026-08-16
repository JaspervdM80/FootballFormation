// The app on a phone, which is where it is actually used — a coach at a touchline in portrait.
//
// This runs in the `mobile` project (a Pixel 7 with touch), and it is a separate spec rather than
// the desktop journeys at a narrow width because the phone genuinely has different controls: the
// app-bar sections become a drawer, the squad table becomes a list of cards, and the match form
// becomes a full-screen sheet.
import { test, expect } from '../fixtures.js';
import { clickFor, createMatch, fillField, gameRow, goto, submitDialog } from '../helpers.js';

test('the sections are behind the drawer, not the app bar', async ({ page }) => {
  await goto(page, '/');

  // The horizontal nav is hidden at this width; the hamburger is the way through.
  await expect(page.locator('.topbar-nav')).toBeHidden();

  // A closed MudDrawer is not hidden — it is parked off the side of the screen, with a bounding box
  // and everything — so "closed" means out of the viewport, not out of the DOM.
  const gamesLink = page.locator('.mud-drawer').getByText('Games', { exact: false }).first();
  await expect(gamesLink).not.toBeInViewport();

  await clickFor(page.getByLabel('Menu'), () => expect(gamesLink).toBeInViewport());
  await gamesLink.click();

  await expect(page).toHaveURL(/\/games$/);
  await expect(page.getByRole('heading', { name: 'Games', exact: false }).first()).toBeVisible();
});

test('a match can be added from a phone, through the full-screen sheet', async ({ page }) => {
  await goto(page, '/games');

  const sheet = page.locator('.mud-dialog.dialog-sheet');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(sheet).toBeVisible());

  // The sheet is the whole viewport below 600px — that is what keeps the action row clear of the
  // last field, and it is the fix docs/known_issues.md records for the "Annuleren" bug.
  // Polled, not measured once: MudBlazor scales a dialog in, so an immediate reading catches it
  // mid-animation at about 86% of its final width.
  const viewport = page.viewportSize();
  await expect
    .poll(async () => Math.round((await sheet.boundingBox()).width), { timeout: 10_000 })
    .toBe(viewport.width);

  await fillField(sheet, 'Opponent', 'FC Telefoon');
  await submitDialog(page);

  await expect(gameRow(page, 'FC Telefoon')).toBeVisible();
});

test('the squad reads as cards rather than a table squeezed sideways', async ({ page }) => {
  await goto(page, '/players');

  await expect(page.locator('.stacked-table')).toBeVisible();
  // The give-away of an unstacked table on a phone: the page scrolls sideways.
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, 'the page should not scroll horizontally on a phone').toBeLessThanOrEqual(1);
});

test('a match card says its venue in words, since the "vs" prefix has no room', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Thuisploeg', venue: 'Home' });

  const row = gameRow(page, 'FC Thuisploeg');
  await expect(row.locator('.opp-prefix')).toBeHidden();
  await expect(row.locator('.game-venue')).toHaveText('(Home)');
});

test("the action buttons keep one width and end on the card's right edge", async ({ page }) => {
  await goto(page, '/games');

  // Read from a real card rather than a contrived one: what matters is that the buttons do not
  // change size with the row's length, and the list has rows of different lengths in it.
  const row = page.locator('.game-cards .game-row').first();
  const geometry = await row.evaluate(el => {
    const actions = el.querySelector('.game-actions');
    return {
      buttons: [...actions.querySelectorAll('.action-btn')]
        .map(b => ({ width: b.getBoundingClientRect().width, right: b.getBoundingClientRect().right })),
      right: actions.getBoundingClientRect().right,
    };
  });

  expect(geometry.buttons.length, 'a card should carry action buttons to measure').toBeGreaterThan(1);
  // 44 is the touch floor, and it is the *same* 44 whether the card carries four buttons or six —
  // splitting the row evenly made a button's width a function of how many there were.
  const widths = [...new Set(geometry.buttons.map(b => Math.round(b.width)))];
  expect(widths, 'every action button should be the same fixed width').toEqual([44]);

  const last = geometry.buttons.at(-1);
  expect(Math.abs(last.right - geometry.right),
    'the last button should end where the row does, not short of it').toBeLessThanOrEqual(1);
});
