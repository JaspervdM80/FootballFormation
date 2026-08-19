// The copyable match summary (#107): a button on the result page and on the shareable formation
// overview that hands the scoreline, the goals and any public comment to the clipboard as plain
// text — the thing someone actually pastes into the group chat, as opposed to the screenshot the
// overview page already offered.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import {
  clickFor, createMatch, fillField, gameAction, goto, gotoRendered, openDialog, submitDialog,
} from '../helpers.js';

/**
 * Creates a match dated in the past — a score can only be typed in on a fixture already played —
 * and returns its id and a fresh load of its result page. The id comes off a click-navigation, but
 * the actual page under test comes from a clean `goto()` rather than that navigation's own render:
 * a plain `<input @onchange>` never carries the `_bl_` marker `goto` waits for elsewhere (Blazor
 * delegates that particular kind of handler rather than attaching it per element), so filling one
 * right after a click-navigation risks landing in the still-inert prerender.
 */
async function pastMatchWithId(page, opponent) {
  await createMatch(page, { opponent, past: true });
  await gameAction(page, opponent, 'Result');
  await page.waitForURL(/\/games\/\d+\/result/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);
  await goto(page, `/games/${id}/result`);
  return id;
}

test('the result page copies a scoreline, a goal and a public comment to the clipboard', async ({ page, context }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });

  const id = await pastMatchWithId(page, 'FC Samenvatting');

  await page.locator('.score-big-input').first().fill('1');
  await page.locator('.score-big-input.score-away').fill('0');
  await clickFor(
    page.getByRole('button', { name: 'Save Score' }),
    () => expect(page.getByText('saved', { exact: false }).first()).toBeVisible(),
  );

  const addRow = page.locator('.add-row');
  await addRow.locator('input[type=number]').fill('12');
  // Shirt numbers are the fixture squad's own — see global-setup's SQUAD — so the label is exact.
  await addRow.locator('select').first().selectOption({ label: '#90 Fixture Keeper' });
  await addRow.locator('select').nth(1).selectOption({ label: '#91 Fixture Defender' });
  await clickFor(
    addRow.locator('.btn-add-goal'),
    () => expect(page.getByText('Goal added', { exact: false })).toBeVisible(),
  );
  await expect(page.locator('.goal-entry')).toHaveCount(1);

  await fillField(page, 'Add comment', 'Great team performance');
  await page.locator('.comment-add-row input[type=checkbox]').check();
  await clickFor(
    page.locator('.result-comments .btn-add-goal'),
    () => expect(page.getByText('Comment added', { exact: false })).toBeVisible(),
  );

  await clickFor(
    page.getByRole('button', { name: 'Copy match result' }),
    () => expect(page.getByText('Copied to clipboard', { exact: false })).toBeVisible(),
  );

  const clipboard = await page.evaluate(() => navigator.clipboard.readText());
  expect(clipboard).toContain('1 – 0');
  expect(clipboard).toContain('Fixture Keeper');
  expect(clipboard).toContain('Fixture Defender');
  expect(clipboard).toContain("(12')");
  expect(clipboard).toContain('Great team performance');

  // The shareable overview composes the same text server-side and offers it through a plain
  // onclick, since that page renders with no circuit to hand a string to a script through.
  await gotoRendered(page, `/games/${id}/overview`);
  const summaryText = await page.locator('#match-summary-text').textContent();
  expect(summaryText).toContain('Fixture Defender');
  expect(summaryText).toContain('Great team performance');

  await clickFor(
    page.getByRole('button', { name: 'Copy match result' }),
    () => expect(page.locator('#copy-success')).toBeVisible(),
  );
});

test('a kick-off time set on the game dialog shows up on the result page', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Aftrap' });

  await gameAction(page, 'FC Aftrap', 'Edit');
  const panel = await openDialog(page);
  await fillField(panel, 'Kick-off Time', '19:30');
  await submitDialog(page);

  // A newly created fixture is dated for the next match day, so the row offers no Result button
  // yet — Overview is the one action every game gets, whatever the calendar says.
  await gameAction(page, 'FC Aftrap', 'Overview');
  await page.waitForURL(/\/games\/(\d+)\/overview/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  // A future fixture's result page shows no score form and no copy button — nothing MudBlazor of
  // its own to wait a handler on — so this is the one page here that needs gotoRendered.
  await gotoRendered(page, `/games/${id}/result`);
  await expect(page.locator('.result-subtitle')).toContainText('19:30');
});
