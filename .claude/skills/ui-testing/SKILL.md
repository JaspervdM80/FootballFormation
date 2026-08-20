---
name: ui-testing
description: Writing or debugging a Playwright test in tests/ui. Covers the Blazor prerender trap, goto/clickFor/openDialog, why there are no fixed sleeps, and .count() failing open. Use before adding or changing any browser test.
---

# UI tests

```bash
cd tests/ui
npm install          # first time only
npm test             # ~40 tests, about a minute
npm test -- squad    # specs matching "squad"
npm run test:headed  # watch it happen
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

**But it only ever sees MudBlazor's own controls.** Blazor writes that attribute for handlers it has
to register on the element itself — a plain `<button @onclick>` or a `<div @onclick>` of ours never
gets one, measured on `/games`. So a page that renders no MudBlazor control satisfies `goto` never,
and two kinds now do that: a page with no circuit at all (`/stats`), and an interactive page whose
controls are all behind `AuthorizeView` and the visitor is signed out (`/players`, `/games`).
**Those call sites use `gotoRendered`**, which waits for the page to stop fetching instead. Do not
reach for it to make a flaky click pass: on a page that does bind handlers, waiting for them is the
whole point.

`rendermode.spec.js` is where "this page has no circuit" is asserted, and it proves its own probe by
checking that `/games` still opens one.

**There is not a single fixed sleep in `tests/ui`. Do not introduce one** — it is how the suite starts
failing on a slow machine.

## The helpers, and when each applies

- **`clickFor(locator, expectation)`** clicks, checks for the outcome, and clicks again if it has not
  happened. Use it for anything idempotent. **Do not** use it for anything that is not — the
  seeded-password change is clicked exactly once on purpose, because a second attempt would use a
  password that is no longer current.
- **`openDialog()`** asserts visibility and waits. Prefer it over a manual check.

Both, along with `goto`/`gotoRendered`/`waitForHandlers`, live in `tests/ui/helpers.js`.

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

There is no automated touch-target check any more — tap-target sizing (the 44px/8px floors) is a
manual review concern now. See the `responsive-and-touch` skill and
[docs/known_issues/touch-pwa.md](../../../docs/known_issues/touch-pwa.md) before changing anything a
thumb touches.

The browser job is a required check; a red run holds the merge.

Detail: [docs/testing/](../../../docs/testing/ui-testing.md#ui-tests-testsui)
