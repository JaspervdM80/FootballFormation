// The PNG the touchline shares out of /games/{id}/overview. html2canvas draws it from a clone of the
// live DOM, so a CSS colour it cannot parse throws and the download never starts — while the page
// itself still looks right, which is how a broken button went unnoticed. Waiting for the file is
// therefore the whole test.
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
});
