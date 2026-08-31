// A test for the tests.
//
// The failure mode that matters in a UI suite is not a test that breaks — it is a test that stops
// testing. Almost every assertion here that proves an *absence* is written as a count of zero, and
// a count of zero is exactly what a selector returns when the class it names no longer exists. So a
// rename in the app turns "an anonymous visitor is offered no Delete button" into a sentence that is
// true because nothing is called that any more, and the suite stays green while the check is gone.
//
// Most of those assertions are already paired with a positive one — the same spec proves the class
// matches something when it should, a page or two earlier — but pairing is a convention, and a
// convention is not a guard. This is the guard: every class name the suite reaches for has to still
// exist in the app's own markup or stylesheets.
//
// It reads the source rather than the browser on purpose. Checking in a browser would mean putting
// the app into the exact state each class appears in, which is most of the rest of this directory;
// reading the source catches the rename, which is the thing that actually happens, and costs
// milliseconds. What it deliberately does not prove is that the class still renders where the test
// looks for it — that is what the specs themselves are for.
import { readdirSync, readFileSync } from 'node:fs';
import { extname, join } from 'node:path';
import { test, expect } from '../fixtures.js';

const SOURCE = join(import.meta.dirname, '../../../src');

// Only the app's own classes. A MudBlazor class (.mud-dialog, .mud-table-row) is not in here: it is
// not ours to rename, and a MudBlazor upgrade that drops one is caught by the specs going red for
// real rather than by this list.
const SELECTORS = {
  'the games list': ['game-row', 'game-section', 'game-date', 'game-score', 'game-opponent',
                     'badge-venue-inline', 'badge-venue-home', 'badge-venue-away',
                     'action-live', 'action-live-now'],
  'a match still missing its lineup': ['nolineup-icon', 'action-needs-lineup'],
  'the formation builder': ['pitch', 'pitch-empty', 'pitch-player', 'pitch-number', 'draggable-player'],
  'the playing-time table': ['playtime-table', 'pt-total', 'playtime-note'],
  'the live screen': ['live-lineup', 'live-controls', 'live-score-value', 'live-score-away',
                      'live-event', 'live-event-score', 'live-event-break', 'live-event-min',
                      'live-bench', 'live-bench-number', 'live-clock', 'live-actions',
                      'live-timeline-toggle', 'live-timeline',
                      'live-minutes-card', 'card-label', 'planned-row'],
  'the result page': ['result-comments', 'comment-entry', 'comment-visibility', 'comment-add-row',
                      'goal-entry', 'own-goal', 'own-goal-tag', 'og-check', 'add-row',
                      'btn-add-goal', 'score-big-input', 'score-away', 'stat-tile', 'stat-value'],
  'season and squad management': ['list-row', 'list-row-meta', 'players-table', 'badge-guest',
                                  'season-menu-item', 'training-row'],
  // The minutes checks are counts of zero against a signed-out visitor, so a rename here is exactly
  // the case this spec exists for: the assertions would keep passing with nothing left to hide.
  'the statistics screens': ['stat-label', 'stat-tiles', 'stat-tiles-3', 'game-head', 'g-num',
                             'game-list', 'game-list-no-minutes', 'game-note', 'g-opp-name',
                             'position-meta', 'pt-row', 'badge-venue'],
  // The availability view is two sets of markup with CSS choosing between them, so a rename here
  // shows up as a switch that appears to do nothing rather than as a test going red.
  'the availability bar': ['availability-switch', 'availability-toggle', 'pt-legend', 'pt-split',
                           'pt-seg', 'pt-played', 'pt-meta-share', 'pt-meta-max', 'position-fill'],
  'the phone layout': ['dialog-sheet', 'stacked-table', 'topbar-nav'],
  // The chrome the specs drive without a circuit: the drawer is a checkbox and the two pickers are
  // <details> disclosures, so these names are the only handle the tests have on them.
  'the chrome': ['app-drawer', 'nav-hamburger', 'season-picker', 'season-menu-all',
                 'season-picker-label', 'language-picker', 'language-picker-menu'],
  // The markup that replaced a handler with a link, or a snackbar with a line on the page.
  'the pages without a circuit': ['inline-notice', 'home-tile-link', 'overview-capture', 'overview-period-card',
                                  'pd-name-cell', 'player-name-cell', 'rank-row', 'action-btn'],
  'the squad': ['badge-archived', 'injured-mark'],
};

/** Every .razor, .razor.css and .css file in the app, read once. */
function appSource() {
  const wanted = new Set(['.razor', '.css']);
  return readdirSync(SOURCE, { recursive: true, withFileTypes: true })
    .filter(entry => entry.isFile() && wanted.has(extname(entry.name)))
    .map(entry => readFileSync(join(entry.parentPath ?? entry.path, entry.name), 'utf8'))
    .join('\n');
}

test('every class name these tests rely on still exists in the app', () => {
  const source = appSource();
  expect(source.length, 'the app source should have been found and read').toBeGreaterThan(1000);

  const missing = [];
  for (const [area, classes] of Object.entries(SELECTORS)) {
    for (const name of classes) {
      // Word-bounded, so `pitch` does not match `pitch-empty` and call itself present.
      if (!new RegExp(`\\b${name}\\b`).test(source)) missing.push(`${name} (${area})`);
    }
  }

  expect(missing, 'renamed or removed in the app — the tests naming them now assert nothing')
    .toEqual([]);
});
