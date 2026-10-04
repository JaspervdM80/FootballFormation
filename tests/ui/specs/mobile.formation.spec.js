// The formation builder on a phone. Nothing else renders this page at a phone viewport — the visual
// harness only passes through it to seed — so the rules below are markup and CSS with no other cover.
//
// One match for all of them, in order: every spec here shares one database, and a file that
// adds a match per test pushes the last card on /games under the install banner — which is what the
// neighbouring touchline spec taps.
import { test, expect } from '../fixtures.js';
import { goto, matchWithId, saveLineup } from '../helpers.js';

// The install banner is fixed to the bottom of a phone screen and covers the last card on /games —
// which is the match this file just added. Playwright then clicks the banner instead of the card's
// formation button. pwa.js reads this flag before it ever shows the banner.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('pwa-install-dismissed', 'true'));
});

/**
 * Drags by dispatching the events rather than by moving the mouse.
 *
 * `dragTo` presses at the source's coordinates and then scrolls to the target, and on a page this
 * tall the scroll puts a pitch chip under the press — a trace of a failing run shows `dragstart`
 * firing on `.pitch-player`, and the drag that was asked for never happening. These are the same
 * synthetic events `UI/wwwroot/js/drag-drop-touch.js` raises from a real finger, `DataTransfer` and
 * all, which is what a phone does here anyway.
 */
const dragFromList = page => page.evaluate(() => {
  window.__dt = new DataTransfer();
  document.querySelector('.draggable-player')
    .dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: window.__dt }));
});

const dropOn = (page, selector) => page.evaluate(sel => {
  const target = document.querySelector(sel);
  for (const type of ['dragenter', 'dragover', 'drop']) {
    target.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: window.__dt }));
  }
}, selector);

test.describe.serial('the formation builder on a phone', () => {
  let gameId;

  const openBuilder = async (page) => {
    // goto rather than the click-through, which waits for the URL and not for the circuit: a drag
    // event dispatched into the prerender is swallowed.
    gameId ??= await matchWithId(page, 'FC Telefoonopstelling');
    await goto(page, `/games/${gameId}/formation`);
  };

  test('the fit legend keeps its five tiers on one row', async ({ page }) => {
    await openBuilder(page);

    const items = page.locator('.pitch-legend .legend-item');
    await expect(items).toHaveCount(5);

    // One row means one shared top edge. Read off the rendered boxes rather than trusting the rule —
    // a legend that wrapped would still be perfectly visible.
    const tops = await items.evaluateAll(els =>
      [...new Set(els.map(el => Math.round(el.getBoundingClientRect().top)))]);
    expect(tops, 'the five legend items should share a row').toHaveLength(1);

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'the legend should not push the page sideways').toBeLessThanOrEqual(1);
  });

  test('the match date sits under the opponent, not beside it', async ({ page }) => {
    await openBuilder(page);

    const name = page.locator('.builder-mobile .builder-opponent');
    const date = page.locator('.builder-mobile .builder-date');
    await expect(name).toBeVisible();
    await expect(date).toBeVisible();

    const [nameBox, dateBox] = [await name.boundingBox(), await date.boundingBox()];
    expect(dateBox.y, 'the date should start below the opponent')
      .toBeGreaterThanOrEqual(nameBox.y + nameBox.height - 1);
    expect(Math.abs(dateBox.x - nameBox.x), 'and line up with it').toBeLessThanOrEqual(2);
  });

  test('the playing-time table offers no sorting', async ({ page }) => {
    await openBuilder(page);

    await expect(page.locator('.playtime-table')).toBeVisible();
    // MudBlazor hides the header row at this width and offers a "Sort by" select in its place. That
    // select is the phone's only sort control, so no sorting means it is not on screen — it is still
    // in the DOM, because the rule that takes it away is a `display: none` in app.css. Attached first,
    // because a hidden check on a class MudBlazor renamed would pass on nothing.
    for (const hidden of ['.mud-table-smalldevices-sortselect', '.mud-table-head .mud-table-row']) {
      const control = page.locator(`.playtime-table ${hidden}`);
      await expect(control.first()).toBeAttached();
      await expect(control).toBeHidden();
    }
  });

  test('the bench offers its drop zone only while empty, and still takes a drop on a sub', async ({ page }) => {
    await openBuilder(page);
    const zone = page.locator('.sub-drop-zone');
    const subs = page.locator('.subs-panel .sub-item');
    await expect(page.locator('.draggable-player').first()).toBeVisible();

    await dragFromList(page);
    await expect(zone, 'an empty bench says where to drop').toHaveCount(1);
    await dropOn(page, '.subs-panel .mud-paper');
    await expect(subs).toHaveCount(1);

    await dragFromList(page);
    await expect(zone, 'a named sub is not pushed down for it').toHaveCount(0);

    // The row covers most of the panel and stops the drop from reaching it, so letting go there is
    // where a thumb most often ends up. It used to do nothing at all.
    await dropOn(page, '.sub-item');
    await expect(subs).toHaveCount(2);

    await saveLineup(page);
    await goto(page, `/games/${gameId}/formation`);
    await expect(subs).toHaveCount(2);
  });

  // The squad sits above the pitch on a phone, further than one screen from the keeper — a drag
  // cannot get there, so a tap has to be able to do everything a drag does.
  test('a tap picks a player up and a second tap puts them down', async ({ page }) => {
    await openBuilder(page);
    const chips = page.locator('.pitch .pitch-player');
    const subs = page.locator('.subs-panel .sub-item');
    const bar = page.locator('.selection-bar');
    const keeper = page.locator('.pitch .pitch-empty', { hasText: 'GK' });
    // An empty slot pulses while a player is in hand, so it never passes Playwright's stability
    // check — and forced, a click at the bottom of the screen lands on the selection bar instead.
    const tapKeeper = async () => {
      await keeper.evaluate(el => el.scrollIntoView({ block: 'center' }));
      await keeper.click({ force: true });
    };
    await expect(chips).toHaveCount(0);
    await expect(subs).toHaveCount(2);

    const first = page.locator('.draggable-player').first();
    await first.click();
    await expect(first).toHaveClass(/selected/);
    await expect(bar).toBeVisible();

    await tapKeeper();
    await expect(chips).toHaveCount(1);
    await expect(bar).toHaveCount(0);

    // A tap on a chip picks it up; the bar's first button sends a starter to the bench, and a sub
    // taken off the bench goes back to the squad.
    const name = (await first.locator('.player-name-text').innerText()).trim();
    await chips.first().click();
    await expect(chips.first()).toHaveClass(/pitch-selected/);
    await expect(chips).toHaveCount(1);
    await bar.locator('.mud-button-root').first().click();
    await expect(chips).toHaveCount(0);
    await expect(subs).toHaveCount(3);

    await subs.last().click();
    await bar.locator('.mud-button-root').first().click();
    await expect(subs).toHaveCount(2);
    await expect(page.locator('.draggable-player', { hasText: name })).toHaveCount(1);

    await page.locator('.draggable-player').first().click();
    await tapKeeper();
    await expect(chips).toHaveCount(1);
    await chips.first().click();
    await page.locator('.subs-panel .mud-typography-subtitle2').click();
    await expect(chips).toHaveCount(0);
    await expect(subs).toHaveCount(3);
  });

  test('the periods are buttons that each show how full their line-up is', async ({ page }) => {
    await openBuilder(page);
    const segments = page.locator('.builder-mobile .period-segment');
    const chips = page.locator('.pitch .pitch-player');
    // The match takes the default split, halves.
    await expect(segments).toHaveCount(2);

    const boxes = await segments.evaluateAll(els => els.map(el => el.getBoundingClientRect().toJSON()));
    expect(new Set(boxes.map(b => Math.round(b.top))).size, 'every period should share one row').toBe(1);
    for (const box of boxes) expect(box.height).toBeGreaterThanOrEqual(44);

    await expect(segments.first()).toHaveAttribute('aria-pressed', 'true');
    await expect(segments.first()).toHaveClass(/incomplete/);
    const fill = segments.first().locator('.period-segment-fill');
    const [, slots] = (await fill.innerText()).match(/^\d+\/(\d+)$/);

    await page.locator('.draggable-player').first().click();
    const keeper = page.locator('.pitch .pitch-empty', { hasText: 'GK' });
    await keeper.evaluate(el => el.scrollIntoView({ block: 'center' }));
    await keeper.click({ force: true });
    await expect(chips).toHaveCount(1);
    await expect(fill).toHaveText(`1/${slots}`);

    await segments.nth(1).click();
    await expect(segments.nth(1)).toHaveAttribute('aria-pressed', 'true');
    await expect(segments.first()).toHaveAttribute('aria-pressed', 'false');
  });
});
