// The result page, and the one read in this app that is not simply open.
//
// Everything else a visitor can reach is public by design. A comment is not: GameService
// re-confirms `includePrivate` against ICurrentUser rather than trusting its caller, and nothing
// checked in a browser that a note the coach wrote for herself stays off a parent's screen.
//
// match-summary.spec.js already covers the copyable text this page composes. This is the page's own
// arithmetic — whose goal counts for whom, and a scorer's figures reaching her statistics — and the
// comment split.
import { test, expect } from '../fixtures.js';
import { BASE_URL, VISITOR_STATE } from '../playwright.config.js';
import {
  clickFor, fileScore, fillField, fillLineup, gameRow, goto, gotoRendered, matchWithId,
} from '../helpers.js';

const PUBLIC_NOTE = 'Sterk gespeeld, complimenten';
const PRIVATE_NOTE = 'Intern: opstelling volgende keer omgooien';

/** The row under the goal list. It is only offered while our scoreline still has a goal unaccounted for. */
const addGoalRow = (page) => page.locator('.add-row');

/**
 * The players the form will accept a goal from, in the order it lists them: MatchResult narrows the
 * squad to whoever the line-up actually involved, so who those are is a fact about the match this
 * test just built rather than something to write down. Index 0 is the "Select player…" placeholder.
 */
const scorerOptions = (page) => addGoalRow(page).locator('select').first().locator('option');

/** "#92 Fixture Midfielder" → "Fixture Midfielder", which is how the fairness table lists her. */
const nameOf = (label) => label.replace(/^\s*#\S+/, '').trim();

/** Indexes into that list. The assist select drops whoever was picked as scorer, so its index 1 is
 *  the next player along rather than the same one. */
async function addGoal(page, { minute, scorer, assist, ownGoal = false }) {
  const row = addGoalRow(page);
  await row.locator('input[type=number]').fill(String(minute));
  await row.locator('select').first().selectOption({ index: scorer });
  if (assist) await row.locator('select').nth(1).selectOption({ index: assist });
  if (ownGoal) await row.locator('.og-check input[type=checkbox]').check();

  await clickFor(
    row.locator('.btn-add-goal'),
    () => expect(page.getByText('Goal added', { exact: false })).toBeVisible(),
  );
}

/** Writes a comment. The switch is off by default, because publishing is always a deliberate act. */
async function addComment(page, body, { isPublic = false } = {}) {
  await fillField(page, 'Add comment', body);
  const visible = page.locator('.comment-add-row input[type=checkbox]');
  if ((await visible.isChecked()) !== isPublic) await visible.setChecked(isPublic);

  await clickFor(
    page.locator('.result-comments .btn-add-goal'),
    () => expect(page.getByText('Comment added', { exact: false })).toBeVisible(),
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
  await gotoRendered(page, '/stats');
  await page.locator('.pt-row', { hasText: playerName }).first().click();
  await page.waitForURL(/\/players\/\d+\/stats/);
  return {
    path: new URL(page.url()).pathname,
    value: Number((await statTile(page, label).locator('.stat-value').innerText()).trim()),
  };
}

test('a private comment is the coach\'s alone, and a public one is the parents\' too', async ({ page, browser }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  const id = await matchWithId(page, 'FC Opmerking', { past: true });
  await fillLineup(page, 2);
  await fileScore(page, id, 3, 1);

  await addComment(page, PUBLIC_NOTE, { isPublic: true });
  await addComment(page, PRIVATE_NOTE);

  // The admin's own copy first, so the absence below is a rule rather than a comment that was never
  // written — the same pairing selectors.spec.js exists to enforce.
  const entries = page.locator('.result-comments .comment-entry');
  await expect(entries).toHaveCount(2);
  await expect(entries.filter({ hasText: PRIVATE_NOTE }).locator('.comment-visibility'))
    .toHaveText('Admin only');
  await expect(entries.filter({ hasText: PUBLIC_NOTE }).locator('.comment-visibility'))
    .toHaveText('Public');

  const visitor = await browser.newContext({ storageState: VISITOR_STATE, baseURL: BASE_URL });
  try {
    const anon = await visitor.newPage();
    await gotoRendered(anon, `/games/${id}/result`);

    // Scoped to the card: the copyable summary is a hidden <pre> on this page and carries the public
    // note too, so an unscoped search matches twice.
    await expect(anon.locator('.result-comments').getByText(PUBLIC_NOTE, { exact: false })).toBeVisible();
    await expect(anon.getByText(PRIVATE_NOTE, { exact: false })).toHaveCount(0);
    // The card counts what it holds, so a private note reaching the visitor's copy and merely being
    // hidden by CSS would still say so here.
    await expect(anon.locator('.result-comments .card-label')).toContainText('(1)');
    await expect(anon.locator('.result-comments .btn-add-goal')).toHaveCount(0);

    // The overview is the artefact that gets shared, so a visitor has to get the whole of it — the
    // line-up on the pitch included, which is the half a private comment must not travel with.
    await gotoRendered(anon, `/games/${id}/overview`);
    await expect(anon.locator('.overview-period-card .pitch-player').first()).toBeVisible();
    await expect(anon.getByText(PRIVATE_NOTE, { exact: false })).toHaveCount(0);
  } finally {
    await visitor.close();
  }
});

test('an own goal is the opponent\'s, and does not tick one of ours off the list', async ({ page }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

  const id = await matchWithId(page, 'FC Eigen Doelpunt', { past: true });
  await fillLineup(page, 2);
  await fileScore(page, id, 1, 1);

  // Both scored by our own squad, so the switch is the only thing telling the two goals apart.
  const [ourScorer, theirGift] = (await scorerOptions(page).allInnerTexts()).slice(1);
  await addGoal(page, { minute: 20, scorer: 2, ownGoal: true });

  const own = page.locator('.goal-entry', { hasText: nameOf(theirGift) });
  await expect(own).toHaveClass(/own-goal/);
  await expect(own.locator('.own-goal-tag')).toHaveText('(OG)');

  // The form is still asking, because our one goal is still unattributed — an own goal counted for
  // us would close it here and leave a scorer nobody could name.
  await expect(addGoalRow(page)).toBeVisible();

  await addGoal(page, { minute: 30, scorer: 1 });
  await expect(page.locator('.goal-entry')).toHaveCount(2);
  await expect(addGoalRow(page)).toHaveCount(0);

  // And the summary shared into the group chat lists ours only: the own goal is already in the
  // scoreline and needs no line of its own.
  await gotoRendered(page, `/games/${id}/overview`);
  const summary = await page.locator('#match-summary-text').textContent();
  expect(summary).toContain(nameOf(ourScorer));
  expect(summary).not.toContain(nameOf(theirGift));
});

test('a scorer and her assister both reach the statistics the squad reads', async ({ page }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

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

  await gotoRendered(page, scorer.path);
  await expect(statTile(page, 'Goals').locator('.stat-value')).toHaveText(String(scorer.value + 1));

  await gotoRendered(page, assister.path);
  await expect(statTile(page, 'Assists').locator('.stat-value')).toHaveText(String(assister.value + 1));
});

test('a filed score reads home side first on the card, whichever side we were', async ({ page }) => {
  test.skip(new Date().getDate() === 1, 'no earlier day in the current month to date a match to');

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
