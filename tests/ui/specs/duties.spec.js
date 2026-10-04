// The season's duties on one public page, so a parent can find their own turn without opening every
// fixture. Beside match-info.spec.js, which covers the same duties in a single game's message.
import { test, expect } from '../fixtures.js';
import { createMatch, gameAction, goto, noEarlierDayThisSeason, openDialog, submitDialog } from '../helpers.js';

async function giveDuty(page, opponent, label, value) {
  await gameAction(page, opponent, 'Edit');
  const panel = await openDialog(page);
  await panel.getByLabel(label, { exact: true }).first().fill(value);
  await submitDialog(page);
}

test('a visitor finds every duty on one page, opened on the next match', async ({ page, visitor: parent }) => {
  test.skip(noEarlierDayThisSeason(), 'the season opened today — no earlier day in it to date a match to');

  await createMatch(page, { opponent: 'FC Takenlijst' });
  await giveDuty(page, 'FC Takenlijst', 'Flag duty', 'Vader van Lotte');
  await createMatch(page, { opponent: 'FC Takenverleden', past: true });
  await giveDuty(page, 'FC Takenverleden', 'Kit wash duty', 'Moeder van Sanne');

  const sockets = [];
  parent.on('websocket', ws => sockets.push(ws.url()));

  await goto(parent, '/games/duties');
  // Past where a circuit would have negotiated, so an empty socket list is an absence rather than a race.
  await parent.waitForLoadState('networkidle');
  expect(sockets, 'the duties page opened a circuit').toEqual([]);

  const upcoming = parent.locator('.duty-row').filter({ hasText: 'FC Takenlijst' });
  await expect(upcoming).toContainText('Vader van Lotte');
  const played = parent.locator('.duty-row').filter({ hasText: 'FC Takenverleden' });
  await expect(played).toHaveClass(/duty-past/);
  await expect(played).toContainText('Moeder van Sanne');

  // Other specs add fixtures of their own, so which one is next is theirs to decide — but only one is.
  await expect(parent.locator('.duty-next')).toHaveCount(1);
  await expect(parent.locator('#next-game')).toHaveClass(/duty-next/);

  await goto(parent, '/games');
  await parent.getByRole('link', { name: 'Duties' }).click();
  await expect(parent).toHaveURL(/\/games\/duties#next-game$/);
});
