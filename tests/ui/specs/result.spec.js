// The result page, and the one read in this app that is not simply open.
//
// Everything else a visitor can reach is public by design. A comment is not: GameService
// re-confirms `includePrivate` against ICurrentUser rather than trusting its caller, and nothing
// checked in a browser that a note the coach wrote for herself stays off a parent's screen.
//
// match-summary.spec.js already covers the copyable text this page composes. This is the page's own
// arithmetic — whose goal counts for whom, and a scorer's figures reaching her statistics — and what
// of a finished match stays the coach's.
import { test, expect } from '../fixtures.js';
import {
  clickFor, fileScore, fillField, fillLineup, gameRow, goto, matchWithId, noEarlierDayThisSeason,
  playedMatch,
} from '../helpers.js';

const PUBLIC_NOTE = 'Sterk gespeeld, complimenten';
const PRIVATE_NOTE = 'Intern: opstelling volgende keer omgooien';

/** The row under the goal list. It is only offered while our scoreline still has a goal unaccounted for. */
const addGoalRow = (page) => page.locator('.add-row');

/** Timeline rows that are goals: substitutions share the .live-event class, but only a goal carries a running scoreline. */
const goalEvents = (page) => page.locator('.live-event', { has: page.locator('.live-event-score') });

/**
 * The players the form will accept a goal from, in the order it lists them: MatchResult narrows the
 * squad to whoever the line-up actually involved, so who those are is a fact about the match this
 * test just built rather than something to write down. Index 0 is the "Select player…" placeholder.
 */
const scorerOptions = (page) => addGoalRow(page).locator('select').first().locator('option');

/** "#92 Fixture Midfielder" → "Fixture Midfielder", which is how the fairness table lists her. */
const nameOf = (label) => label.replace(/^\s*#\S+/, '').trim();

/**
 * `scorer` and `assist` are indexes into that list; the assist select drops whoever was picked as the
 * scorer, so its index 1 is the next player along rather than the same one.
 *
 * The retry checks for *this* goal rather than the snackbar: MudBlazor suppresses a duplicate
 * message, so a second "Goal added!" is the first one still on screen — which would report a
 * swallowed click as a success, and, once it expired, retry a write that is not idempotent.
 */
async function addGoal(page, { minute, scorer, assist, ownGoal = false }) {
  const row = addGoalRow(page);
  await row.locator('input[type=number]').fill(String(minute));
  await row.locator('select').first().selectOption({ index: scorer });
  if (assist) await row.locator('select').nth(1).selectOption({ index: assist });
  if (ownGoal) await row.locator('.og-check input[type=checkbox]').check();

  await clickFor(
    row.locator('.btn-add-goal'),
    () => expect(page.locator('.live-event', { hasText: `${minute}'` })).toHaveCount(1),
  );
}

/**
 * Writes a comment. The switch is off by default, because publishing is always a deliberate act.
 * The retry checks for this comment's own body, not the snackbar — see addGoal.
 */
async function addComment(page, body, { isPublic = false } = {}) {
  await fillField(page, 'Add comment', body);
  const visible = page.locator('.comment-add-row input[type=checkbox]');
  if ((await visible.isChecked()) !== isPublic) await visible.setChecked(isPublic);

  await clickFor(
    page.locator('.result-comments .btn-add-goal'),
    () => expect(page.locator('.comment-entry', { hasText: body })).toHaveCount(1),
  );
}

/** One of the tiles on a player's statistics page, by the label under the number. */
const statTile = (page, label) => page
  .locator('.stat-tile', { has: page.locator('.stat-label', { hasText: new RegExp(`^${label}$`) }) })
  .first();

/**
 * Opens a player's own page from the fairness table and reports one figure off it. Only meaningful
 * for a player with a completed game on file: the page falls back to "No games recorded yet" for
 * everyone else, tiles and all.
 */
async function statFor(page, playerName, label) {
  await goto(page, '/stats');
  await page.locator('.pt-row', { hasText: playerName }).first().click();
  await page.waitForURL(/\/players\/\d+\/stats/);
  return {
    path: new URL(page.url()).pathname,
    value: Number((await statTile(page, label).locator('.stat-value').innerText()).trim()),
  };
}

test('a finished match shows a visitor the score and the public note, and nothing that is the coach\'s', async ({ page, visitor }) => {
  // Run from the touchline, because only that match carries every admin-only part at once: the
  // minutes, the half lengths and the line-up per minute all read its clock.
  const id = await playedMatch(page, 'FC Ouderavond');
  await addComment(page, PUBLIC_NOTE, { isPublic: true });
  await addComment(page, PRIVATE_NOTE);

  // The admin's own copy first, so each absence below is a rule rather than something never drawn.
  const entries = page.locator('.result-comments .comment-entry');
  await expect(entries).toHaveCount(2);
  await expect(entries.filter({ hasText: PRIVATE_NOTE }).locator('.comment-visibility'))
    .toHaveText('Admin only');
  await expect(entries.filter({ hasText: PUBLIC_NOTE }).locator('.comment-visibility'))
    .toHaveText('Public');

  const coachOnly = ['.live-minutes-row', '.half-lengths', '.lineup-minute', '#match-summary-text',
    '.result-comments .btn-add-goal'];
  for (const selector of coachOnly) await expect(page.locator(selector).first()).toBeAttached();
  await expect(goalEvents(page).getByRole('button', { name: 'Edit' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Copy match result' })).toBeVisible();

  await goto(visitor, `/games/${id}/result`);
  await expect(visitor.locator('.score-value:not(.score-away)')).toHaveText('0');
  await expect(visitor.locator('.score-value.score-away')).toHaveText('1');
  await expect(visitor.locator('.live-event')).toHaveCount(1);
  await expect(visitor.locator('.result-comments').getByText(PUBLIC_NOTE, { exact: false })).toBeVisible();
  await expect(visitor.getByText(PRIVATE_NOTE, { exact: false })).toHaveCount(0);
  // The card counts what it holds, so a private note reaching the visitor's copy and merely being
  // hidden by CSS would still say so here.
  await expect(visitor.locator('.result-comments .card-label')).toContainText('(1)');
  for (const selector of coachOnly) {
    await expect(visitor.locator(selector), `${selector} reached a visitor`).toHaveCount(0);
  }
  await expect(visitor.getByRole('button', { name: 'Edit' })).toHaveCount(0);
  await expect(visitor.getByRole('button', { name: 'Copy match result' })).toHaveCount(0);

  // The overview is the artefact that gets shared, so a visitor has to get the whole of it — the
  // line-up on the pitch included, which is the half a private comment must not travel with.
  await goto(visitor, `/games/${id}/overview`);
  await expect(visitor.locator('.overview-period-card .pitch-player').first()).toBeVisible();
  await expect(visitor.getByText(PRIVATE_NOTE, { exact: false })).toHaveCount(0);
  await expect(visitor.getByRole('button', { name: 'Copy match result' })).toHaveCount(0);
  await expect(visitor.locator('#match-summary-text')).toHaveCount(0);
});

test('an own goal is the opponent\'s, and does not tick one of ours off the list', async ({ page }) => {
  test.skip(noEarlierDayThisSeason(), 'the season opened today — no earlier day in it to date a match to');

  const id = await matchWithId(page, 'FC Eigen Doelpunt', { past: true });
  await fillLineup(page, 2);
  await fileScore(page, id, 1, 1);
  // Nobody kicked this one off, so the minutes are the line-up's plan and the heading says so.
  await expect(page.locator('.card-label', { hasText: 'Planned minutes' })).toBeVisible();

  // Both scored by our own squad, so the switch is the only thing telling the two goals apart.
  const [ourScorer, theirGift] = (await scorerOptions(page).allInnerTexts()).slice(1);
  await addGoal(page, { minute: 20, scorer: 2, ownGoal: true });

  const own = page.locator('.live-event', { hasText: nameOf(theirGift) });
  await expect(own).toHaveClass(/live-event-against/);
  await expect(own.locator('.live-event-tag')).toHaveText('(OG)');

  // The form is still asking, because our one goal is still unattributed — an own goal counted for
  // us would close it here and leave a scorer nobody could name.
  await expect(addGoalRow(page)).toBeVisible();

  await addGoal(page, { minute: 30, scorer: 1 });
  await expect(goalEvents(page)).toHaveCount(2);
  await expect(addGoalRow(page)).toHaveCount(0);

  // And the summary shared into the group chat lists ours only: the own goal is already in the
  // scoreline and needs no line of its own.
  await goto(page, `/games/${id}/overview`);
  const summary = await page.locator('#match-summary-text').textContent();
  expect(summary).toContain(nameOf(ourScorer));
  expect(summary).not.toContain(nameOf(theirGift));
});

test('a scorer and her assister both reach the statistics the squad reads', async ({ page }) => {
  test.skip(noEarlierDayThisSeason(), 'the season opened today — no earlier day in it to date a match to');

  const id = await matchWithId(page, 'FC Statistiek', { past: true });
  await fillLineup(page, 2);
  await fileScore(page, id, 1, 0);

  const [scorerLabel, assisterLabel] = (await scorerOptions(page).allInnerTexts()).slice(1);

  // Read before the goal is logged, because the season's totals hold whatever the specs before this
  // one filed — what belongs to this test is the step, not the number. Read after the score, too:
  // until the match is complete neither player has a tile to read.
  const scorer = await statFor(page, nameOf(scorerLabel), 'Goals');
  const assister = await statFor(page, nameOf(assisterLabel), 'Assists');

  await goto(page, `/games/${id}/result`);
  await addGoal(page, { minute: 15, scorer: 1, assist: 1 });

  await goto(page, scorer.path);
  await expect(statTile(page, 'Goals').locator('.stat-value')).toHaveText(String(scorer.value + 1));

  await goto(page, assister.path);
  await expect(statTile(page, 'Assists').locator('.stat-value')).toHaveText(String(assister.value + 1));
});

test('a filed score reads home side first on the card, whichever side we were', async ({ page }) => {
  test.skip(noEarlierDayThisSeason(), 'the season opened today — no earlier day in it to date a match to');

  // Away, because that is the case a scoreboard printed as "ours – theirs" gets wrong: ScoreHome and
  // ScoreAway always mean us and them, and only the display order follows the venue.
  const id = await matchWithId(page, 'FC Uitwedstrijd', { past: true, venue: 'Away' });
  await fillLineup(page, 2);
  await fileScore(page, id, 1, 4);

  await goto(page, '/games');
  const row = gameRow(page, 'FC Uitwedstrijd');
  await expect(row.locator('.badge-venue')).toHaveText('AWAY');
  await expect(row.locator('.game-score')).toHaveText('4 – 1');
});
