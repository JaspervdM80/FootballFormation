// Creating, editing and deleting a match — the app's longest form, and the one filled in on a phone
// at a touchline.
import { test, expect } from '../fixtures.js';
import {
  clickFor, confirmDialog, createMatch, fillField, gameRow, goto, openDialog, pickEarlierThisMonth,
  pickLaterThisMonth, submitDialog,
} from '../helpers.js';

/** The current month as the card writes it, for reading a match's date back off its card. */
const cardMonth = () => new Date().toLocaleString('en-US', { month: 'short' });

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
  await expect(played.locator('.game-date')).toHaveText(new RegExp(`^0?${day} ${cardMonth()}$`, 'i'));
  await expect(played.locator('.nolineup-icon')).toBeVisible();
  // The action button changes shape rather than hiding: an empty grid means "this one needs you".
  await expect(played.getByTitle('Add lineup', { exact: false })).toBeVisible();
  // And it can be scored, which is the half of the rule below that has to keep working.
  await expect(played.getByTitle('Result', { exact: false })).toBeVisible();
});

test('a match still to be played offers no way to score it', async ({ page }) => {
  // The mirror of the skip above: on the last day of the month there is no later day to pick, and
  // stepping into the next one risks the season boundary the date decides the season from.
  const now = new Date();
  const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();
  test.skip(now.getDate() === lastDay, 'no later day in the current month to date a match to');

  await goto(page, '/games');
  const panel = page.locator('.mud-dialog');
  await clickFor(page.getByRole('button', { name: 'Add' }).first(), () => expect(panel).toBeVisible());
  await fillField(panel, 'Opponent', 'FC Nog Te Spelen');
  const day = await pickLaterThisMonth(page, panel);
  await submitDialog(page);

  const upcoming = gameRow(page, 'FC Nog Te Spelen');
  await expect(upcoming.locator('.game-date')).toHaveText(new RegExp(`^0?${day} ${cardMonth()}$`, 'i'));
  await expect(upcoming.getByTitle('Result', { exact: false })).toHaveCount(0);

  // The card is not the enforcement. Take the id off the formation link and go to the result page
  // the way an admin with a bookmark would, where the same rule has to hold.
  await upcoming.getByTitle('Formation', { exact: false }).click();
  await page.waitForURL(/\/games\/\d+\/formation/);
  await goto(page, `/games/${page.url().match(/\/games\/(\d+)\//)[1]}/result`);

  await expect(page.getByText("This match hasn't been played yet.")).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save Score' })).toHaveCount(0);
  // Both routes to a scoreline: the boxes themselves, and a goal, which recounts it. Scoped to
  // .add-row because the comments card below reuses .btn-add-goal for its own Add button, and a
  // comment on a match still to be played is perfectly reasonable.
  await expect(page.locator('input.score-big-input')).toHaveCount(0);
  await expect(page.locator('.add-row .btn-add-goal')).toHaveCount(0);
});
