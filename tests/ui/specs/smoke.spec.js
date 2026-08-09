// Every page renders, is interactive, and says what it is.
//
// The cheapest test in the suite and the one most likely to catch a real breakage: a component that
// throws on first render takes its whole page down, and nothing else in the repo notices.
import { test, expect } from '../fixtures.js';
import { goto } from '../helpers.js';

const PAGES = [
  { path: '/', heading: 'GJS Meiden' },
  { path: '/players', heading: 'Squad' },
  { path: '/games', heading: 'Games' },
  { path: '/stats', heading: 'Statistics' },
  { path: '/users', heading: 'Users' },
  { path: '/settings', heading: 'Match Preferences' },
];

for (const { path, heading } of PAGES) {
  test(`${path} renders and is interactive`, async ({ page }) => {
    await goto(page, path);

    await expect(page.getByRole('heading', { name: heading, exact: false }).first()).toBeVisible();
    // A spinner still on screen after the circuit connected means the page never finished loading.
    await expect(page.locator('.mud-progress-circular')).toHaveCount(0);
  });
}

test('the app bar offers every section to an admin', async ({ page }) => {
  await goto(page, '/');

  for (const section of ['Squad', 'Games', 'Season', 'Preferences', 'Users']) {
    await expect(page.locator('.mud-appbar').getByText(section, { exact: false }).first()).toBeVisible();
  }
});
