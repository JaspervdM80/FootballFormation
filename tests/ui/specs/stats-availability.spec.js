// The availability switch on the season statistics: the playing-time card shows either each
// player's share of her own minutes, or everybody's season split against one common maximum.
//
// /stats has no circuit — rendermode.spec.js holds it to that — so the switch is a checkbox and CSS
// picks the view. These assertions are about what a visitor sees change, which is what the missing
// circuit makes worth checking: nothing here would fail loudly if the sibling selectors stopped
// matching, the page would simply never switch.
import { test, expect } from '../fixtures.js';
import {
  addPlayer, clickFor, createMatch, gameRow, goto, gotoRendered, openDialog, playerMenuItem,
  submitDialog,
} from '../helpers.js';

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

test('a match missed injured is coloured injured once the result is in', async ({ page }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  await addPlayer(page, { firstName: 'Blessure', surname: 'Balk', shirt: 85 });
  await playerMenuItem(page, 'Blessure Balk', 'Edit Player');
  const dialog = await openDialog(page);
  await dialog.locator('label.mud-switch', { hasText: 'Injured' }).click();
  await submitDialog(page);

  // A match on paper: she is injured, so no line-up will offer her, and the typed score is what
  // settles it — which is where the squad's flag gets copied onto the match.
  await createMatch(page, { opponent: 'FC Blessureseizoen', past: true });
  await gameRow(page, 'FC Blessureseizoen').getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  const chips = page.locator('.pitch .pitch-player');
  await expect(page.locator('.draggable-player').first()).toBeVisible();
  await expect(page.locator('.draggable-player', { hasText: 'Blessure Balk' })).toHaveCount(0);
  for (let i = 0; i < 2; i++) {
    await page.locator('.draggable-player').first().dragTo(page.locator('.pitch .pitch-empty').first());
    await expect(chips).toHaveCount(i + 1);
  }
  await clickFor(
    page.getByRole('button', { name: /^Save( All Lineups)?$/ }).first(),
    () => expect(page.getByText('All lineups saved', { exact: false })).toBeVisible(),
    { settle: 10_000 },
  );

  await goto(page, `/games/${id}/result`);
  await page.locator('.score-big-input').first().fill('2');
  await page.locator('.score-big-input.score-away').fill('1');
  await clickFor(
    page.getByRole('button', { name: 'Save Score' }),
    () => expect(page.getByText('saved', { exact: false }).first()).toBeVisible(),
  );

  await gotoRendered(page, '/stats');
  await page.locator('label.availability-switch').click();
  const row = page.locator('.pt-row', { hasText: 'Blessure Balk' }).first();

  // Injured minutes rather than a number: the specs before this one leave matches of their own in
  // the database, and those were settled before she was flagged, so they land in "not played".
  // What is hers alone is that the injured segment exists at all.
  const injured = await row.locator('.pt-injured').getAttribute('title');
  expect(Number(injured.match(/^Injured: (\d+)'$/)[1])).toBeGreaterThan(0);

  await expect(row.locator('.pt-played')).toHaveAttribute('title', "Played: 0'");
  // Nobody marked her unavailable — the same absence, told apart by the reason on the record.
  await expect(row.locator('.pt-unavailable')).toHaveAttribute('title', "Unavailable: 0'");
});
