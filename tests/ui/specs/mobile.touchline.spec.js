// The app on a phone, which is where it is actually used — a coach at a touchline in portrait.
//
// This runs in the `mobile` project (a Pixel 7 with touch), and it is a separate spec rather than
// the desktop journeys at a narrow width because the phone genuinely has different controls: the
// app-bar sections become a drawer, the squad table becomes a list of cards, and the match form
// becomes a full-screen sheet.
import { test, expect } from '../fixtures.js';
import { clickFor, createMatch, fillField, gameRow, goto, gotoRendered, submitDialog } from '../helpers.js';

test('the sections are behind the drawer, not the app bar', async ({ page }) => {
  // The drawer needs no circuit, and neither does anything else this test touches.
  await gotoRendered(page, '/');

  // The horizontal nav is hidden at this width; the hamburger is the way through.
  await expect(page.locator('.topbar-nav')).toBeHidden();

  // A closed drawer is not hidden — it is parked off the side of the screen, with a bounding box
  // and everything — so "closed" means out of the viewport, not out of the DOM.
  const gamesLink = page.locator('.app-drawer').getByText('Games', { exact: false }).first();
  await expect(gamesLink).not.toBeInViewport();

  // The hamburger is a <label> for a visually-hidden checkbox — that checkbox is the drawer's open
  // state, so no circuit and no script are involved and the label is what a thumb hits.
  await clickFor(page.locator('label.nav-hamburger'), () => expect(gamesLink).toBeInViewport());
  await gamesLink.click();

  await expect(page).toHaveURL(/\/games$/);
  await expect(page.getByRole('heading', { name: 'Games', exact: false }).first()).toBeVisible();
});

test('a match can be added from a phone, through the full-screen sheet', async ({ page }) => {
  await goto(page, '/games');

  const sheet = page.locator('.mud-dialog.dialog-sheet');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(sheet).toBeVisible());

  // The sheet is the whole viewport below 600px — that is what keeps the action row clear of the
  // last field, and it is the fix docs/known_issues/touch-pwa.md records for the "Annuleren" bug.
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

test('a tap beside the action buttons opens the match, rather than landing in nothing', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Duimbreedte' });

  const row = gameRow(page, 'FC Duimbreedte');
  const actions = row.locator('.game-actions');
  // A tap is dispatched at viewport coordinates with none of a click's actionability checks, so the
  // card has to be brought into the middle of the screen before anything is measured off it.
  await row.evaluate(card => card.scrollIntoView({ block: 'center' }));
  const strip = await actions.boundingBox();
  const firstButton = await actions.locator('.action-btn').first().boundingBox();

  // Below 600px that row is a line of its own at the full width of the card, so the stretch to the
  // left of the buttons is most of it — and while the row stopped the click for all of them, every
  // tap landing there was swallowed.
  expect(firstButton.x - strip.x, 'no empty stretch left of the buttons to tap in').toBeGreaterThan(20);
  await page.touchscreen.tap((strip.x + firstButton.x) / 2, strip.y + strip.height / 2);

  await expect(page).toHaveURL(/\/games\/\d+\/formation/);
});
