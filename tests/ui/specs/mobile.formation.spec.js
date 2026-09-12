// The formation builder on a phone. Nothing else renders this page at a phone viewport — the visual
// harness only passes through it to seed — so the rules below are markup and CSS with no other
// cover: a legend that fits, a header that stacks, a table with no sort control, and a bench whose
// drop target is where a thumb actually lets go.
//
// One match for the four of them, in order: every spec here shares one database, and a file that
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
 * synthetic events `wwwroot/js/drag-drop-touch.js` raises from a real finger, `DataTransfer` and
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
    // in the DOM, because the rule that takes it away is a `display: none` in app.css.
    await expect(page.locator('.playtime-table .mud-table-smalldevices-sortselect')).toBeHidden();
    await expect(page.locator('.playtime-table .mud-table-head .mud-table-row')).toBeHidden();
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
});
