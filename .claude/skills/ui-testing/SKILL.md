---
name: ui-testing
description: Writing or debugging a Playwright test in tests/ui, or the visual-check/touch-target harness in scripts/. Covers the Blazor prerender trap, goto/clickFor/waitForStableBox, why there are no fixed sleeps, and .count() failing open. Use before adding or changing any browser test.
---

# UI tests and the visual harness

```bash
cd tests/ui
npm install          # first time only
npm test             # ~40 tests, about a minute
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
| a `_bl_*` attribute is present | 15, about 230 ms in |

Blazor writes `_bl_<guid>` onto every element it wires an event to, so **that** is the signal. `goto()`
waits for it; `waitForHandlers()` waits for one specific element.

**There is not a single fixed sleep in `tests/ui` or `scripts/`. Do not introduce one** — it is how the
suite starts failing on a slow machine.

## The helpers, and when each applies

- **`clickFor(locator, expectation)`** clicks, checks for the outcome, and clicks again if it has not
  happened. Use it for anything idempotent. **Do not** use it for anything that is not — the
  seeded-password change is clicked exactly once on purpose, because a second attempt would use a
  password that is no longer current.
- **`waitForStableBox`** — MudBlazor scales a dialog and a popover in, so anything measured the moment
  it becomes visible is measured mid-animation (a full-width sheet reads about 86% of its width). Two
  identical bounding boxes a frame apart is the exact answer.
- **`openDialog()`** asserts visibility and waits. Prefer it over a manual check.

`scripts/blazor.mjs` carries its own copy of `goto`/`clickFor`/`waitForStableBox`/`waitUntil` for the
visual harness. The duplication is deliberate — `scripts/` and `tests/ui/` are separate npm packages —
so change one and look at the other.

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

## Touch targets

`scripts/touch-targets.mjs` reopens the app at **320×568**, **360×640** and **844×390** landscape, and
enforces two rules: every hit-testable element is at least **44×44** CSS px, and the gap to its
nearest neighbour is either zero or at least **8px** — anything between is a dead gutter the browser
awards to whichever neighbour has the larger contact area. Where the geometry provably cannot reach
44px the number is in `RECORDED_FLOORS` with its reason, and **a recorded floor is still a floor**.

Both browser jobs are required checks; a red run holds the merge.

Detail: [docs/testing.md](../../../docs/testing.md#ui-tests-testsui)
