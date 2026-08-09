// Creating, editing and deleting a match — the app's longest form, and the one filled in on a phone
// at a touchline.
import { test, expect } from '../fixtures.js';
import {
  clickFor, confirmDialog, createMatch, fillField, gameRow, goto, openDialog, pickEarlierThisMonth, submitDialog,
} from '../helpers.js';

test('a new match appears under Fixtures with its venue and formation', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Nieuwkomer', venue: 'Away' });

  const row = gameRow(page, 'FC Nieuwkomer');
  await expect(row).toBeVisible();
  // An away match reads "@ opponent"; a home one reads "vs".
  await expect(row.locator('.opp-prefix')).toHaveText('@ ');

  // A match with no score yet belongs to Fixtures, not Results.
  const fixtures = page.locator('.game-section', { hasText: 'Fixtures' });
  await expect(fixtures.locator('.game-row', { hasText: 'FC Nieuwkomer' })).toHaveCount(1);
});

test('the form remembers the season defaults, so only the opponent is required', async ({ page }) => {
  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());

  // Everything below the opponent already has a value: the venue, the formation, the duration and
  // the split all come from the season's preferences. A MudSelect keeps its value in the text it
  // shows, not in an input, so read the control rather than a form value.
  const shows = (label) => panel.locator('.mud-input-control', { hasText: label }).first();
  await expect(shows('Venue')).toContainText(/Home|Away/);
  await expect(shows('Formation')).toContainText(/\d-\d/);
  await expect(shows('Game Split')).toContainText(/Halves|Quarters/);
  await expect(panel.getByLabel('Game Duration', { exact: false })).not.toHaveValue('');

  await fillField(panel, 'Opponent', 'FC Standaardwaarden');
  await submitDialog(page);
  await expect(gameRow(page, 'FC Standaardwaarden')).toBeVisible();
});

test('an edit is visible on the card it came from', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Typefout' });

  await gameRow(page, 'FC Typefout').getByTitle('Edit', { exact: false }).click();
  const panel = await openDialog(page);
  await fillField(panel, 'Opponent', 'FC Gecorrigeerd');
  await submitDialog(page);

  await expect(gameRow(page, 'FC Gecorrigeerd')).toBeVisible();
  await expect(page.locator('.game-row', { hasText: 'FC Typefout' })).toHaveCount(0);
});

test('deleting a match asks first, and the match survives a cancel', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Bedenk Je' });

  await gameRow(page, 'FC Bedenk Je').getByTitle('Delete', { exact: false }).click();
  const panel = await openDialog(page);
  await panel.getByRole('button', { name: 'Cancel' }).click();
  await expect(gameRow(page, 'FC Bedenk Je')).toBeVisible();

  await gameRow(page, 'FC Bedenk Je').getByTitle('Delete', { exact: false }).click();
  await openDialog(page);
  await confirmDialog(page, 'Delete');
  await expect(page.locator('.game-row', { hasText: 'FC Bedenk Je' })).toHaveCount(0);
});

test('only a match already played is flagged for its missing lineup', async ({ page }) => {
  // On the first of the month there is no earlier day to pick, and stepping back a month would risk
  // crossing the season boundary that the date decides the season from. Skipping one day in thirty
  // beats a test that fails on the 1st and passes on the 2nd.
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  // A future fixture is legitimately empty — the lineup is built on the day — so the warning is
  // about a match that has been played and whose playing time can therefore never be recovered.
  await createMatch(page, { opponent: 'FC Toekomst' });
  const upcoming = gameRow(page, 'FC Toekomst');
  await expect(upcoming.locator('.action-needs-lineup')).toHaveCount(0);
  await expect(upcoming.locator('.nolineup-icon')).toHaveCount(0);

  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());
  await fillField(panel, 'Opponent', 'FC Gespeeld');
  const day = await pickEarlierThisMonth(page, panel);
  await submitDialog(page);

  const played = gameRow(page, 'FC Gespeeld');
  // The card's own date, which the app formats as "dd MMM" whatever the culture is doing to the
  // input above — so this is the unambiguous check that the match really is in the past. The month
  // is part of it on purpose: day 8 of the wrong month reads the same and is not in the past.
  const month = new Date().toLocaleString('en-US', { month: 'short' });
  await expect(played.locator('.game-date')).toHaveText(new RegExp(`^0?${day} ${month}$`, 'i'));
  await expect(played.locator('.nolineup-icon')).toBeVisible();
  // The action button changes shape rather than hiding: an empty grid means "this one needs you".
  await expect(played.getByTitle('Add lineup', { exact: false })).toBeVisible();
});
