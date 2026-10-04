---
name: ui-testing
description: Writing or debugging a Playwright test in tests/ui, or the visual-check/touch-target harness in scripts/. Covers the Blazor prerender trap and the data-circuit readiness marker, goto/clickFor/waitForStableBox, the visitor/openPage fixtures and shared state helpers, keeping the suite fast (no sleeps, no timeouts as the expected path), tests that cannot fail, the lockfile and the CI shards, and .count() failing open. Use before adding or changing any browser test.
---

# UI tests and the visual harness

```bash
cd tests/ui
npm ci               # first time, and after the lockfile moves
npm test             # everything
npm test -- squad    # specs matching "squad"
npm run test:headed  # watch it happen
scripts/visual-check.sh   # screenshots every page, then measures every touch target
```

`run.mjs` makes a throwaway data directory, Playwright's `webServer` starts the app against it, and it
is deleted afterwards — no run can touch a real database. Nothing is stubbed: real dialogs, real
SQLite, real SignalR circuit.

## The one thing to know first

**A Blazor Server page renders twice, and the first one is a lie.** The prerender is complete and
correct-looking, every button visible and enabled and none of them wired to anything. A click landing
in that window is swallowed with no error; a `fill()` writes into an input the server never hears
about, so the form submits the values it was prerendered with.

Measured on `/settings`, two obvious readiness signals are both wrong:

| Signal | Handlers actually attached |
|---|---|
| `domcontentloaded`, `window.Blazor` already true | 0 of 12 buttons |
| the circuit's first WebSocket frame | still 0 — that frame is the handshake |

**The signal is `InteractiveShell`'s hidden `data-circuit` marker**: `pending` in the prerender, `live`
in the circuit's first render, which arrives with every handler in it attached. `goto()` waits until
no marker reads `pending` — so a page with no circuit is ready once loaded — then for the web fonts.
It is the only navigation helper, for every page and every visitor; **`settle()`** is the same wait
for a page reached another way, such as a form post or a redirect. Both hold after a **full load**
only: after an in-app navigation the previous page's `live` marker stays in the DOM until the new
markup lands.

- **An interactive page without `<InteractiveShell />` has no marker, so `goto` takes its inert
  prerender as ready and clicks race the circuit again.** Nothing times out to say so. A `goto` that
  *does* time out means a marker never left `pending`: the circuit never drew.
- `gotoRendered` (network idle) and `waitForHandlers` (`_bl_` attributes) were removed: `_bl_` only
  ever appears on MudBlazor's controls, and network idle cost ≥0.5 s a navigation. Do not bring
  either back, and do not add `waitForLoadState('networkidle')` to a spec except where it is the
  assertion itself (`rendermode`, `duties`: "no WebSocket was ever opened").
- **Leaving a page mid-handshake logs "Failed to complete negotiation"**, which the console-error
  check fails. A step that lands on an interactive page without `goto` — signing in through the
  form — ends with `settle()` before the next navigation.
- Do not rename or move the marker without changing `READY` in `tests/ui/helpers.js` **and**
  `scripts/blazor.mjs` in the same commit.

`rendermode.spec.js` is where "this page has no circuit" is asserted, and it proves its own probe by
checking that `/games` still opens one.

**There is not a single fixed sleep in `tests/ui` or `scripts/`. Do not introduce one** — it is how the
suite starts failing on a slow machine.

## The helpers, and when each applies

- **`clickFor(locator, expectation)`** clicks, checks for the outcome, and clicks again if it has not
  happened. Use it for anything idempotent. **Do not** use it for anything that is not — the
  seeded-password change is clicked exactly once on purpose, because a second attempt would use a
  password that is no longer current. **The expectation must still be false before the click**: one
  that already holds confirms nothing and never retries. `startSecondHalf` once waited for "Half time"
  to go, which it already had at the break; it waits for its own button to go now.
- **`waitForStableBox`** — MudBlazor scales a dialog and a popover in, so anything measured the moment
  it becomes visible is measured mid-animation (a full-width sheet reads about 86% of its width). Two
  identical bounding boxes a frame apart is the exact answer.
- **`openDialog()`** asserts visibility and waits. Prefer it over a manual check.
- **The states a test starts from have helpers — use them, never an inline copy**: `matchWithId`,
  `liveMatch`, `playedMatch` (both halves, one goal against, on its result page), `halfTime`,
  `startSecondHalf`, `logGoal`, `tapOnPitch`, `substitute`, `offInjured`, `openOverview`, `fileScore`. The same
  click sequences were once pasted five to seven times over. A sequence a third spec needs becomes a
  helper; a second copy of an existing one is a review finding.

`scripts/blazor.mjs` carries its own copy of `goto`/`clickFor`/`waitForStableBox`/`waitUntil` for the
visual harness. The duplication is deliberate — `scripts/` and `tests/ui/` are separate npm packages —
so change one and look at the other.

## Keeping it fast

The suite took 7.8 minutes in CI, and one helper was 2.5 of them. Every rule here is one that was
broken.

- **A timeout must never be the expected path.** `clearInjured` cleared one player, then waited for a
  caption that only goes away after the *last* one — so every other player cost a full timeout plus a
  retry, 8–11 s a test. Do all the work, then assert once. The tell in CI is a test whose time is
  identical every run: that is a timeout expiring, not work being done.
- **Don't build a state to read one thing off it.** Five tests each played a whole match to check one
  admin-only part was missing for a visitor; that is one test now. Tests that read different things
  off the same state share it (`test.describe.serial` on one match, as `result-corrections` does).
- **Don't re-test what another spec owns.** The page loop is `smoke.spec.js`'s, the circuit probe
  `rendermode.spec.js`'s. Add a route or a case there rather than a second loop elsewhere.

## A test that cannot fail is worse than none

- **Its name has to be what it proves.** A "missing translation falls back" test that involved no
  missing translation, and a cookie test passing on the cookie the saved state already carried, were
  both green and both proved nothing. Set up the precondition the name claims (clear the cookie
  first), or delete the test.
- **An absence needs a presence.** `toHaveCount(0)` / `toBeHidden()` on a class also passes once the
  class is renamed. Assert the element exists in the state where it should (or on the admin's page)
  before asserting it is gone.
- **Selectors are string literals.** `selectors.spec.js` reads every class name out of the specs and
  helpers and checks it still exists in `src`; a class assembled at run time from a variable is
  invisible to it.
- **A second browser comes from the `visitor` or `openPage` fixture, never `browser.newContext()`.**
  The fixtures carry the console-error check and close the context on failure; a hand-made context
  passes its "nothing is shown" assertions on a page that failed to render.
- **Dates in the past:** `createMatch(page, { past: true })` and `pickDaysAgo` go with
  `test.skip(noEarlierDayThisSeason(), …)` — 1 July is the one day "yesterday" is last season.

## `.count()` is the one locator call that does not wait, and it fails open

Every other locator call retries until its timeout; `count()` answers from the DOM as it stands right
now. `if (await dialog.count()) await confirmDialog(...)` read zero before a MudBlazor dialog had
rendered, skipped the confirmation entirely, and let the test carry on against a player who was never
archived — green locally for months, red on a loaded runner. Reach for `count()` only to assert
something is *absent*, and even then `toHaveCount(0)` is the waiting version.

## Waiting for a consequence is not waiting for the navigation it causes

Changing the seeded admin's password rotates the security stamp, the cookie is rejected, and the
circuit navigates to `/login`. Waiting for the *notice* to clear happens on re-render — earlier than
the drop — and signing in on that signal starts a navigation while the circuit's own is still in
flight, which Playwright abandons. Wait on `page.waitForURL`. Any Blazor flow ending in a
server-driven redirect has this shape.

## Fixtures and isolation

`global-setup.js` pins the language to English (so a selector matches the source text), changes the
seeded admin's password — which locks every route to `/settings` until done — seeds a small squad
named `Fixture …`, and saves an admin and a visitor browser state so an anonymous test is not also a
Dutch test. Specs share one app and one database in a single worker, so they stay out of each other's
way by **naming what they create after themselves**, never by counting rows.

**A CI retry does not get a clean database.** A test that creates a player and then fails will, on
retry, add that player a second time — read a two-attempt failure as "flaked, then hit dirty state".

## CI: two shards and a lockfile

- CI runs the suite as **two shards**, each its own app and database, behind a job still named
  **`Playwright`** — that name is the required check in `.github/rulesets/main-every-check-green.json`.
  Keep the gate job's `if: always()`: a skipped required check counts as passed.
- Playwright splits by **whole spec file in sorted order**. A spec relying on data another spec made
  only works while both land in the same shard — make each spec create what it reads.
- **`package-lock.json` is committed** in `tests/ui/` and `scripts/`, CI runs `npm ci`, and the npm and
  browser caches are keyed on the lockfile. Move Playwright by changing the lockfile in a pull request;
  never git-ignore the lockfile again or key a cache on `package.json`.

## Touch targets

`scripts/touch-targets.mjs` reopens the app at **320×568**, **360×640** and **844×390** landscape, and
enforces two rules: every hit-testable element is at least **44×44** CSS px, and the gap to its
nearest neighbour is either zero or at least **8px** — anything between is a dead gutter the browser
awards to whichever neighbour has the larger contact area. Where the geometry provably cannot reach
44px the number is in `RECORDED_FLOORS` with its reason, and **a recorded floor is still a floor**.

**The harness runs Chromium's full build (`channel: 'chromium'`), never the headless shell** Playwright
picks by default. The shell denies notifications, so the home scene's required opt-in button is never
drawn, and it lays text out differently from the Chrome on a phone. The Playwright suite still uses
the shell.

Both browser jobs are required checks; a red run holds the merge.

Detail: [docs/testing/](../../../docs/testing/ui-testing.md#ui-tests-testsui)
