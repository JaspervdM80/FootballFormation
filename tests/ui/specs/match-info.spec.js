// The copyable match-day message: the times, the ground and the duties a coach sends round the team
// the days before a fixture. Beside match-summary.spec.js, which covers the message that replaces it
// once the game has been played.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import { clickFor, createMatch, gameAction, gotoRendered, openDialog, submitDialog } from '../helpers.js';

// "Dressing room" is a prefix of "Dressing room duty", so the substring match `fillField` uses would
// fill the wrong one of the two.
async function fill(panel, label, value) {
  await panel.getByLabel(label, { exact: true }).first().fill(value);
}

test('the match-day arrangements typed into the game dialog become a copyable message', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });

  await createMatch(page, { opponent: 'FC Wedstrijdinfo', venue: 'Away' });

  await gameAction(page, 'FC Wedstrijdinfo', 'Edit');
  const panel = await openDialog(page);
  await fill(panel, 'Kick-off Time', '12:00');
  await fill(panel, 'Departure time', '10:45');
  await fill(panel, 'Warm-up time', '11:15');
  await fill(panel, 'Field', 'Veld 3');
  await fill(panel, 'Dressing room', '12');
  await fill(panel, 'Sports park', 'Sportpark De Watertoren');
  await fill(panel, 'City', 'Zaltbommel');
  await fill(panel, 'Flag duty', 'Vader van Niels');
  await fill(panel, 'Kit wash duty', 'Ouder van Seb');
  await submitDialog(page);

  await gameAction(page, 'FC Wedstrijdinfo', 'Overview');
  await page.waitForURL(/\/games\/(\d+)\/overview/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  // The overview renders without a circuit, so the text is composed server-side into a hidden
  // element and copied from a plain onclick — same shape as the match summary.
  await gotoRendered(page, `/games/${id}/overview`);
  const message = await page.locator('#match-info-text').textContent();
  expect(message).toContain('FC Wedstrijdinfo');
  expect(message).toContain('10:45 depart');
  expect(message).toContain('11:15 briefing/warm-up');
  expect(message).toContain('12:00 kick-off');
  expect(message).toContain('Veld 3');
  expect(message).toContain('dressing room 12');
  expect(message).toContain('Sportpark De Watertoren');
  expect(message).toContain('Zaltbommel');
  expect(message).toContain('Flags: Vader van Niels');
  expect(message).toContain('Kit wash: Ouder van Seb');
  // Nobody was given it, so the line is left out rather than left blank.
  expect(message).not.toContain('Dressing room:');

  await clickFor(
    page.getByRole('button', { name: 'Copy match info' }),
    () => expect(page.locator('#copy-success')).toBeVisible(),
  );
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('Sportpark De Watertoren');
});

test('a fixture with no arrangements filled in still offers the message, without empty lines', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Kaal' });

  await gameAction(page, 'FC Kaal', 'Overview');
  await page.waitForURL(/\/games\/(\d+)\/overview/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  await gotoRendered(page, `/games/${id}/overview`);
  const message = await page.locator('#match-info-text').textContent();
  const lines = message.split(/\r?\n/).filter(line => line.length > 0);

  expect(lines[0]).toContain('FC Kaal');
  expect(lines).toHaveLength(2);
  expect(lines[1]).toMatch(/^📅 \d{2}-\d{2}-\d{4}$/);
});
