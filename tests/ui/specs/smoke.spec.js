// Every page renders, is interactive, and says what it is.
//
// The cheapest test in the suite and the one most likely to catch a real breakage: a component that
// throws on first render takes its whole page down, and nothing else in the repo notices.
import { test, expect } from '../fixtures.js';
import { goto, gotoRendered } from '../helpers.js';

// `bare` marks a page with no handler bound to an HTML element, so there is nothing for `goto` to
// wait on — see gotoRendered. It says nothing about whether the page has a circuit.
const PAGES = [
  { path: '/', heading: 'GJS Meiden', bare: true },
  { path: '/players', heading: 'Squad' },
  { path: '/games', heading: 'Games' },
  { path: '/stats', heading: 'Statistics' },
  { path: '/users', heading: 'Users' },
  { path: '/settings', heading: 'Match Preferences' },
  { path: '/stats/positions', heading: 'Position Development', bare: true },
];

for (const { path, heading, bare } of PAGES) {
  test(`${path} renders and is interactive`, async ({ page }) => {
    await (bare ? gotoRendered : goto)(page, path);

    await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    // A spinner still on screen after the circuit connected means the page never finished loading.
    await expect(page.locator('.mud-progress-circular')).toHaveCount(0);
  });
}

test('the app bar offers every section to an admin', async ({ page }) => {
  await gotoRendered(page, '/');

  for (const section of ['Squad', 'Games', 'Season', 'Preferences', 'Users']) {
    await expect(page.locator('.mud-appbar').getByText(section, { exact: false }).first()).toBeVisible();
  }
});
