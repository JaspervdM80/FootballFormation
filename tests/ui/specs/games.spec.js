// Creating, editing and deleting a match — the app's longest form, and the one filled in on a phone
// at a touchline.
import { test, expect } from '../fixtures.js';
import {
  clickFor, confirmDialog, createMatch, fillField, gameRow, goto, openDialog, submitDialog,
} from '../helpers.js';

/** Creates a match and returns its id, read from the URL its own formation button navigates to. */
async function matchWithId(page, opponent, options = {}) {
  await createMatch(page, { opponent, ...options });
  await gameRow(page, opponent).getByTitle(/Formation|Add lineup/).click();
  await page.waitForURL(/\/games\/\d+\/formation/);
  return Number(page.url().match(/\/games\/(\d+)\//)[1]);
}

/** Files a score for a match already dated in the past, turning it from a fixture into a result. */
async function fileScore(page, id, home, away) {
  await goto(page, `/games/${id}/result`);
  await page.locator('.score-big-input').first().fill(String(home));
  await page.locator('.score-big-input.score-away').fill(String(away));
  await clickFor(
    page.getByRole('button', { name: 'Save Score' }),
    () => expect(page.getByText('saved', { exact: false }).first()).toBeVisible(),
  );
}

test('a new match appears under Fixtures with its venue and formation', async ({ page }) => {
  await createMatch(page, { opponent: 'FC Nieuwkomer', venue: 'Away' });

  const row = gameRow(page, 'FC Nieuwkomer');
  await expect(row).toBeVisible();
  // The venue is a badge trailing the opponent's name, at every width, and it carries the venue in
  // its class as well as its text — the colour is half of what it says, and a badge that read
  // "AWAY" in the home green would be worse than no badge.
  const badge = row.locator('.badge-venue');
  await expect(badge).toHaveText('AWAY');
  await expect(badge).toHaveClass(/badge-venue-away/);

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

  const day = await createMatch(page, { opponent: 'FC Gespeeld', past: true });

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

test('the Results section leads with the most recent match, not the oldest', async ({ page }) => {
  // Two distinct past dates, not just two rows: the tie-break on equal dates sorts by id, which
  // would pass a test that only checked insertion order and hide a regression to oldest-first.
  test.skip(new Date().getDate() <= 2, 'not enough earlier days in the current month for two dates');

  const olderId = await matchWithId(page, 'FC Eerder', { past: 2 });
  await fileScore(page, olderId, 1, 0);

  const newerId = await matchWithId(page, 'FC Later', { past: 1 });
  await fileScore(page, newerId, 2, 0);

  await goto(page, '/games');
  const results = page.locator('.game-section', { hasText: 'Results' }).locator('.game-row');
  await expect(results.first()).toContainText('FC Later');
  await expect(results.nth(1)).toContainText('FC Eerder');
});
