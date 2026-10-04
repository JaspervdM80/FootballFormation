// The `test` every spec imports: Playwright's, plus one thing this app needs.
//
// A Blazor render failure does not fail a request or blank the page — it logs to the console and
// leaves the circuit in a state where the next interaction does nothing. So a test that only
// asserts on what it can see would pass a page that is quietly broken. Every test here fails if the
// browser logged an error, which is the same check scripts/visual-check.sh makes.
import { test as base, expect } from '@playwright/test';
import { VISITOR_STATE } from './playwright.config.js';

// Noise from the app's own progressive-web-app plumbing, none of it a render failure. Keep this
// list short and specific; a broad pattern here is how a real error gets missed.
const IGNORED = [
  /manifest\.webmanifest/i,
  /Failed to load resource.*favicon/i,
  /service ?worker/i,
];

function watchErrors(page) {
  const errors = [];
  const note = (text) => { if (!IGNORED.some(p => p.test(text))) errors.push(text); };

  page.on('console', m => { if (m.type() === 'error') note(`[console] ${m.text()}`); });
  page.on('pageerror', e => note(`[pageerror] ${e.message}`));
  return errors;
}

// Only when the test itself passed: a failing test has already said what went wrong, and a console
// error is usually a consequence of it rather than a second finding.
const passed = (testInfo) => testInfo.status === testInfo.expectedStatus;

export const test = base.extend({
  page: async ({ page }, use, testInfo) => {
    const errors = watchErrors(page);
    await use(page);
    if (passed(testInfo)) expect(errors, 'the browser logged errors').toEqual([]);
  },

  /** Opens another browser beside `page` — a parent watching, a phone — held to the same rule and closed afterwards. */
  openPage: async ({ browser }, use, testInfo) => {
    const opened = [];
    await use(async (options = {}) => {
      const context = await browser.newContext(options);
      const page = await context.newPage();
      opened.push({ context, errors: watchErrors(page) });
      return page;
    });

    for (const { context } of opened) await context.close();
    if (passed(testInfo)) expect(opened.flatMap(o => o.errors), 'a second browser logged errors').toEqual([]);
  },

  /** A signed-out browser in English, for the half of a rule that is about what a parent is shown. */
  visitor: async ({ openPage }, use) => {
    await use(await openPage({ storageState: VISITOR_STATE }));
  },
});

export { expect };
