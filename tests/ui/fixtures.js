// The `test` every spec imports: Playwright's, plus one thing this app needs.
//
// A Blazor render failure does not fail a request or blank the page — it logs to the console and
// leaves the circuit in a state where the next interaction does nothing. So a test that only
// asserts on what it can see would pass a page that is quietly broken. Every test here fails if the
// browser logged an error.
import { test as base, expect } from '@playwright/test';

// Noise from the app's own progressive-web-app plumbing, none of it a render failure. Keep this
// list short and specific; a broad pattern here is how a real error gets missed.
const IGNORED = [
  /manifest\.webmanifest/i,
  /Failed to load resource.*favicon/i,
  /service ?worker/i,
];

export const test = base.extend({
  page: async ({ page }, use, testInfo) => {
    const errors = [];
    const note = (text) => { if (!IGNORED.some(p => p.test(text))) errors.push(text); };

    page.on('console', m => { if (m.type() === 'error') note(`[console] ${m.text()}`); });
    page.on('pageerror', e => note(`[pageerror] ${e.message}`));

    await use(page);

    // Only when the test itself passed: a failing test has already said what went wrong, and a
    // console error is usually a consequence of it rather than a second finding.
    if (testInfo.status === testInfo.expectedStatus) {
      expect(errors, 'the browser logged errors').toEqual([]);
    }
  },
});

export { expect };
