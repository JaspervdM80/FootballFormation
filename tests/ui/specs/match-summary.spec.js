// The copyable match summary (#107): a button on the result page and on the shareable formation
// overview that hands the scoreline, the goals and any public comment to the clipboard as plain
// text — the thing someone actually pastes into the group chat, as opposed to the screenshot the
// overview page already offered. That a visitor is offered neither is in result.spec.js.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import {
  clickFor, createMatch, fileScore, fillField, fillLineup, finishMatch, gameAction, goto, halfTime,
  logGoal, matchWithId, noEarlierDayThisSeason, openDialog, openOverview, startMatch, startSecondHalf,
  submitDialog,
} from '../helpers.js';

test('the result page copies a scoreline, a goal and a public comment to the clipboard', async ({ page, context }) => {
  test.skip(noEarlierDayThisSeason(), 'the season opened today — no earlier day in it to date a match to');

  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });

  // The score can only be typed in on a fixture already played, and `fileScore` opens the result
  // page cleanly rather than filling the render a click-navigation left behind.
  const id = await matchWithId(page, 'FC Samenvatting', { past: true });
  await fileScore(page, id, 1, 0);

  const addRow = page.locator('.add-row');
  await addRow.locator('input[type=number]').fill('12');
  // Shirt numbers are the fixture squad's own — see global-setup's SQUAD — so the label is exact.
  await addRow.locator('select').first().selectOption({ label: '#90 Fixture Keeper' });
  await addRow.locator('select').nth(1).selectOption({ label: '#91 Fixture Defender' });
  await clickFor(
    addRow.locator('.btn-add-goal'),
    () => expect(page.getByText('Goal added', { exact: false })).toBeVisible(),
  );
  // Scoped to a goal row — a substitution shares .live-event, but only a goal carries a running scoreline.
  await expect(page.locator('.live-event', { has: page.locator('.live-event-score') })).toHaveCount(1);

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
  // A goal typed in by hand has no half to cross, so no half-time break belongs in the text.
  expect(clipboard).not.toContain('———');

  // The shareable overview composes the same text server-side and offers it through a plain
  // onclick, since that page renders with no circuit to hand a string to a script through.
  await goto(page, `/games/${id}/overview`);
  const summaryText = await page.locator('#match-summary-text').textContent();
  expect(summaryText).toContain('Fixture Defender');
  expect(summaryText).toContain('Great team performance');

  await clickFor(
    page.getByRole('button', { name: 'Copy match result' }),
    () => expect(page.locator('#copy-success')).toBeVisible(),
  );
});

test('a goal in each half puts a dashed break between them in the copied text', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });

  const id = await matchWithId(page, 'FC Rust Samenvatting');
  await fillLineup(page, 1);

  await goto(page, `/games/${id}/live`);
  await startMatch(page);

  const ourScore = page.locator('.live-score-value:not(.live-score-away)');
  await logGoal(page, 'Fixture');
  await expect(ourScore).toHaveText('1');

  await halfTime(page);
  await startSecondHalf(page);

  await logGoal(page, 'Fixture');
  await expect(ourScore).toHaveText('2');

  await finishMatch(page);
  await clickFor(
    page.getByRole('button', { name: 'Edit result' }),
    () => expect(page).toHaveURL(/\/games\/\d+\/result/),
  );

  await clickFor(
    page.getByRole('button', { name: 'Copy match result' }),
    () => expect(page.getByText('Copied to clipboard', { exact: false })).toBeVisible(),
  );

  const clipboard = await page.evaluate(() => navigator.clipboard.readText());
  // Two goals, one in each half, so the break sits between them — never before the first or
  // after the last, and never doubled between goals that share a half.
  // Split on either ending: the app joins with \n, but the Windows clipboard hands the text back as CRLF.
  const lines = clipboard.split(/\r?\n/).filter(line => line.length > 0);
  const goalLines = lines.filter(line => line.startsWith('⚽'));
  expect(goalLines).toHaveLength(2);
  const breakIndex = lines.indexOf('———————————');
  expect(breakIndex).toBeGreaterThan(lines.indexOf(goalLines[0]));
  expect(breakIndex).toBeLessThan(lines.indexOf(goalLines[1]));
});

test('a kick-off time set on the game dialog shows up on the result page', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Aftrap' });

  await gameAction(page, 'FC Aftrap', 'Edit');
  const panel = await openDialog(page);
  await fillField(panel, 'Kick-off Time', '19:30');
  await submitDialog(page);

  // A newly created fixture is dated for the next match day, so the row offers no Result button
  // yet — Overview is the one action every game gets, whatever the calendar says.
  const id = await openOverview(page, 'FC Aftrap');

  await goto(page, `/games/${id}/result`);
  await expect(page.locator('.result-subtitle')).toContainText('19:30');
});
