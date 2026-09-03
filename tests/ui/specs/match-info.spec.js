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
  // Our own side is named from TeamState, which is the club the app was seeded with — an away game,
  // so the opponent leads and we follow.
  expect(message).toContain('FC Wedstrijdinfo vs GJS MO15-2');
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

test('the time fields settle on a 24-hour clock, whatever language the browser is in', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Klok' });

  await gameAction(page, 'FC Klok', 'Edit');
  const panel = await openDialog(page);

  // A native time input would be drawn in the browser's own UI language — en-US here, so "12:00 PM".
  const kickOff = panel.getByLabel('Kick-off Time', { exact: true });
  expect(await kickOff.getAttribute('type')).not.toBe('time');

  // Bare digits, the way a numeric keypad hands them over; the colon arrives on blur.
  await kickOff.fill('1200');
  await kickOff.blur();
  await expect(kickOff).toHaveValue('12:00');

  const assemble = panel.getByLabel('Assemble time', { exact: true });
  await assemble.fill('0930');
  await assemble.blur();
  await expect(assemble).toHaveValue('09:30');
  await submitDialog(page);

  await gameAction(page, 'FC Klok', 'Edit');
  const reopened = await openDialog(page);
  await expect(reopened.getByLabel('Kick-off Time', { exact: true })).toHaveValue('12:00');
  await expect(reopened.getByLabel('Assemble time', { exact: true })).toHaveValue('09:30');

  // 25:00 is a real TimeSpan and not a real time of day, so the strict parse has to refuse it.
  const bad = reopened.getByLabel('Warm-up time', { exact: true });
  await bad.fill('2500');
  await bad.blur();
  await expect(reopened.getByText('24-hour clock', { exact: false })).toBeVisible();

  // Half-typed, and it carries its own separator: reading a shape off "104" would settle this
  // silently on 01:04 rather than saying it is not a time yet.
  await bad.fill('10:4');
  await bad.blur();
  await expect(bad).toHaveValue('10:4');
  await expect(reopened.getByText('24-hour clock', { exact: false })).toBeVisible();

  // A long form scrolls the field at fault out of sight, so Save has to say why it did nothing.
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('24-hour clock', { exact: false }).last()).toBeVisible();
  await expect(page.locator('.mud-dialog')).toBeVisible();
  await submitDialog(page, 'Cancel');

  await gameAction(page, 'FC Klok', 'Overview');
  await page.waitForURL(/\/games\/(\d+)\/overview/);
  const id = Number(page.url().match(/\/games\/(\d+)\//)[1]);

  await gotoRendered(page, `/games/${id}/overview`);
  const message = await page.locator('#match-info-text').textContent();
  expect(message).toContain('09:30 assemble');
  expect(message).toContain('12:00 kick-off');
  expect(message).not.toMatch(/\bAM\b|\bPM\b/);
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
