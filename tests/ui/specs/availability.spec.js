// The availability switch on the season statistics: the playing-time card shows either each
// player's share of her own minutes, or everybody's season split against one common maximum.
//
// /stats has no circuit — rendermode.spec.js holds it to that — so the switch is a checkbox and CSS
// picks the view. These assertions are about what a visitor sees change, which is what the missing
// circuit makes worth checking: nothing here would fail loudly if the sibling selectors stopped
// matching, the page would simply never switch.
import { test, expect } from '../fixtures.js';
import { addPlayer, gotoRendered } from '../helpers.js';

test('the availability switch swaps the fairness bar for the four-colour split', async ({ page }) => {
  // The card lists full squad members, so this spec brings its own rather than depending on
  // whatever the specs before it left behind.
  await addPlayer(page, { firstName: 'Beschikbaar', surname: 'Balk', shirt: 84 });

  await gotoRendered(page, '/stats');

  const row = page.locator('.pt-row', { hasText: 'Beschikbaar' }).first();
  const legend = page.locator('.pt-legend');
  await expect(row).toBeVisible();

  await expect(row.locator('.position-fill')).toBeVisible();
  await expect(row.locator('.pt-split')).toBeHidden();
  await expect(legend).toBeHidden();

  await page.locator('label.availability-switch').click();

  await expect(row.locator('.position-fill')).toBeHidden();
  await expect(row.locator('.pt-split')).toBeVisible();
  await expect(legend).toBeVisible();

  // Four segments whatever the figures are — a zero-minute one collapses rather than going missing.
  await expect(row.locator('.pt-seg')).toHaveCount(4);
  await expect(row.locator('.pt-played')).toHaveAttribute('title', /^Played: \d+'$/);

  // The two readings have different denominators, so the meta line swaps with the bar.
  await expect(row.locator('.pt-meta-share')).toBeHidden();
  await expect(row.locator('.pt-meta-max')).toBeVisible();
});
