// The PNG the touchline shares out of /games/{id}/overview. html2canvas draws it from the live DOM,
// so a CSS colour it cannot parse breaks the export and nothing else — the page still looks right,
// which is how a broken button went unnoticed. This spec clicks it and waits for the file.
import { test, expect } from '../fixtures.js';
import { fillLineup, gotoRendered, matchWithId } from '../helpers.js';

test('the overview exports the line-up as an image', async ({ page }) => {
  const id = await matchWithId(page, 'FC Schermafdruk');
  await fillLineup(page, 2);

  await gotoRendered(page, `/games/${id}/overview`);
  await expect(page.locator('.overview-period-card .pitch-player').first()).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Save as image' }).click();
  expect((await download).suggestedFilename()).toBe('formation.png');

  // The page reports a failed capture on itself rather than throwing, so this is the line that says
  // the export failed for a reason of its own rather than never having started.
  await expect(page.locator('#screenshot-error')).toBeHidden();
});
