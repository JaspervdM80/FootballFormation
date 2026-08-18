// The `test` every spec imports: Playwright's, plus one thing this app needs.
//
// A Blazor render failure does not fail a request or blank the page — it logs to the console and
// leaves the circuit in a state where the next interaction does nothing. So a test that only
// asserts on what it can see would pass a page that is quietly broken. Every test here fails if the
// browser logged an error, which is the same check scripts/visual-check.sh makes.
import { test as base, expect } from '@playwright/test';

// Noise from the app's own progressive-web-app plumbing, none of it a render failure. Keep this
// list short and specific; a broad pattern here is how a real error gets missed.
const IGNORED = [
  /manifest\.webmanifest/i,
  /Failed to load resource.*favicon/i,
  /service ?worker/i,
  /No Popover Container found/i,
];

// MudBlazor's popover helper observes the first `.mud-popover-provider` in the document and logs
// when there is none. Its observer outlives an enhanced navigation, so moving from an interactive
// page to a statically rendered one — which has no provider, and needs none — makes it complain a
// few seconds later. Nothing is broken: the pages it fires on open no popovers at all.
//
// Not fixed by putting a placeholder container in the layout: the helper takes the *first* match in
// document order, so on an interactive page it could then observe the empty placeholder instead of
// the real provider, and popovers would be quietly misplaced. A log line is the better trade.
// **If this ever fires on a page that does open a popover, it is a real failure — narrow the
// pattern rather than widening it.**

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
