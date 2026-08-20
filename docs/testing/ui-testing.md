# UI Testing

## UI tests (`tests/ui`)

```bash
cd tests/ui
npm install          # first time only
npm test             # everything, ~1 minute
npm test -- squad    # specs matching "squad"
npm run test:headed  # watch it happen
npm run report       # the HTML report from the last run
```

Playwright, driving the app the way a coach does. `run.mjs` makes a throwaway data directory,
Playwright's `webServer` starts the app against it, and the whole thing is deleted afterwards — no
run can touch a real database. Nothing is stubbed: these are the real dialogs, the real SQLite, the
real SignalR circuit.

| Spec | What it holds |
| --- | --- |
| `smoke.spec.js` | Every page renders, is interactive, and is not still spinning |
| `authorization.spec.js` | The public/admin split — a visitor reads the squad, fixtures and stats, is offered no control that writes, and is bounced from `/settings` and `/users` with the route it wanted remembered |
| `squad.spec.js` | Adding, editing and archiving a player; a nameless player is refused and told why |
| `games.spec.js` | Creating, editing and deleting a match; season defaults filling the form; the missing-lineup warning appearing only for a match already played |
| `match-day.spec.js` | The journey the app exists for: drag a lineup onto the pitch, save it, run the match live, log goals, blow the whistle, and find the scoreline on the games list — plus the playing-time table dropping its `~` estimate for the match clock once that has happened |
| `localization.spec.js` | Dutch by default, the switcher moving the whole app to English, and the choice surviving a navigation |
| `mobile.touchline.spec.js` | The phone layout — the drawer, the full-screen match sheet, the stacked squad — in the `mobile` project on a Pixel 7 |
| `reconnect.spec.js` | Losing the circuit and getting it back: the retry schedule a suspended phone rejoins on, and the rejoined page still being interactive |
| `session.spec.js` | Staying signed in: the auth cookie carrying a real expiry rather than being a session cookie, surviving a link followed in from another site, and a deleted account losing its authority on an open circuit without anyone reloading |
| `selectors.spec.js` | A test for the tests — see below |

### The test that guards the tests

Almost every assertion proving an *absence* is a count of zero, and a count of zero is also what a
selector returns when the class it names no longer exists. Rename `.game-row` and "a visitor is
offered no Delete button" becomes true because nothing is called that any more — the suite stays
green while the check is gone.

Most of those assertions are already paired with a positive one in the same spec (the missing-lineup
warning is asserted present on a played match and absent on a future one; the drawer is asserted out
of the viewport and then in it). But pairing is a convention, not a guard. `selectors.spec.js` is the
guard: every app-owned class name the suite reaches for has to still exist somewhere in `src`. It
reads the source rather than the browser, because putting the app into the state each class appears
in is most of the rest of this directory, and a rename is the thing that actually happens. MudBlazor's
own classes are deliberately not in the list — those are not ours to rename, and an upgrade that
drops one shows up as a spec failing for real.

Adding a spec that leans on a new app class means adding it to `SELECTORS` too.

### Breaking the circuit on purpose

`reconnect.spec.js` is the one spec that takes the connection away, and three things about it cost
an afternoon to find:

- **`context.setOffline(true)` does not drop an established WebSocket.** It blocks new requests, so
  the socket to a loopback server stays up and Blazor never notices anything. Use
  `Blazor._internal.forceCloseConnection()`, which is the hook Blazor's own end-to-end tests use —
  the client stops its connection, the server retains the circuit, and the rejoin that follows is
  the real one. Refusing `**/_blazor/negotiate**` with a route is then what keeps the rejoin from
  succeeding, i.e. what stands in for a phone whose network is not back yet.
- **Don't wait on the overlay to prove a rejoin happened.** With the network right there the rejoin
  lands on the first attempt, and `#components-reconnect-modal` can appear and disappear between two
  polls. Blazor dispatches `components-reconnect-state-changed` on that element for every step —
  `show`, `retrying` with the attempt number and `secondsToNextAttempt`, then `hide` or `rejected` —
  and reading that stream is the same story without the race. It is also the only way to assert on
  the *schedule* rather than on a duration, which is what the spec is actually about.
- It imports Playwright's `test`, not `fixtures.js`. The console-error guard would fail a spec whose
  whole point is refused requests.

### One pipeline, one compile

Everything lives in `.github/workflows/ci.yml`, in three jobs on one chain, all three required checks:

```
Build and test ──┬── Coverage
                 └── Playwright
```

**`Build and test`** restores, builds Release, runs `dotnet test`, then publishes `--no-build`, so it
hands on exactly what the unit tests ran against rather than compiling the commit a second time. The
test step carries `--collect:"XPlat Code Coverage"`, so the report comes out of the run that is
already the gate. This replaced a second workflow that compiled the commit twice more, plus a third
time inside Playwright's `dotnet run`: four compiles of one commit became one.

**`Coverage`** runs `scripts/coverage.mjs` over that report — the same script and 80% floor as
locally. It is the one job checked out with `fetch-depth: 0`, because judging a change means diffing
against its merge base and a single-commit checkout has nothing to diff against. The verdict and a
per-file table with the uncovered line numbers go to `$GITHUB_STEP_SUMMARY`.

**`Playwright`** downloads the published artifact and starts it. It calls no compiler — it installs
the SDK only for the runtime. `UI_TEST_APP_DLL` points the harness at the artifact; unset locally,
where it falls back to `dotnet run`.

Three details are deliberate:

- **Publish, not build.** The output has to survive the trip to another runner. A published directory
  is self-describing; a `bin/` tree needs the SDK and the sources it was built from.
- **`-p:PublishReadyToRun=false`.** R2R forces a runtime-identifier-specific publish that
  `--no-build` cannot satisfy.
- **The `runtimes/` prune.** 84MB of the 104MB published is SQLitePCLRaw's native library for every
  architecture it supports. Keeping only `linux-x64` takes the artifact to 23MB.

`npx playwright install` still runs on a cache hit: it is a no-op when the revision is already there,
and it is what fetches a new one when a patch release moves the browser revision without moving
`package.json`.

### What triggers it

**`pull_request`, and that is the whole of it**, plus `workflow_dispatch` as the escape hatch.
`actions/checkout` resolves a `pull_request` event to `refs/pull/N/merge` — the branch already merged
into `main` — where a dispatch checks out the branch tip. Merging is what deploys and nothing re-runs
on `main`, so this event is the last word on the commit that reaches the volume.

The cost of carrying no `push` trigger is that a branch gets no CI until a pull request exists for
it. The symptom of a regression here is a pull request whose checks never appear rather than a red
one; one more commit, or the dispatch, recovers it.

### Is it stable enough for CI?

Measured, not assumed: eleven consecutive full runs green, including three pinned to two cores with
busy loops competing, which stretched a run to 2.2–2.5 minutes and changed nothing else. That is the
retry-on-outcome design doing its job — `clickFor` absorbs a slow circuit instead of failing on it.

A red run holds the merge, and so does a flake — re-run the job from the run's page, because the
ruleset grants no bypass. `trace: 'retain-on-failure'` means a failing test can be replayed with
`npx playwright show-trace`, and `CI=true` turns on one retry so a test that only passes on the retry
is reported as flaky rather than quietly green.

One test is calendar-dependent and skips rather than guesses: dating a match earlier in the current
month has nothing to pick on the 1st, and stepping back a month could cross the season boundary the
date decides the season from.

### The one thing to know before writing a test here

**A Blazor Server page renders twice, and the first one is a lie.** The prerender is complete and
correct-looking, with every button visible and enabled and none of them wired to anything. A click
that lands in that window is swallowed with no error, and a `fill()` writes into an input the server
never hears about — so the form then submits the values it was prerendered with.

Two obvious readiness signals are both wrong, measured on `/settings`:

| Signal | Handlers actually attached |
| --- | --- |
| `domcontentloaded`, `window.Blazor` is already true | 0 of 12 buttons |
| the circuit's first WebSocket frame | still 0 — that frame is the handshake |
| a `_bl_*` attribute is present | 15, about 230ms in |

Blazor's renderer writes `_bl_<guid>` onto every element it wires an event to, so that attribute is
the signal. `goto()` waits for it, and `waitForHandlers()` waits for one specific element. This is
why there is not a single fixed sleep in the directory, and adding one is how the suite starts
failing on a slow machine.

**What it does not see, and the second helper that exists because of it.** Blazor writes that
attribute for handlers it registers on the element itself, which in practice means MudBlazor's own
controls — a plain `<button @onclick>` or a `<div @onclick>` of ours never gets one, measured on
`/games`. So the signal really means *MudBlazor has rendered an interactive control*, and a page
that renders none for the current visitor satisfies it never. That used to be impossible, because
the chrome carried a `MudIconButton` on every page; the chrome renders statically since the
render-mode split, so it is the page's own controls or nothing. Two kinds of page now have none: one
with no circuit at all (`/stats`, `/players/{id}/stats`, `/games/{id}/overview`), and an interactive
one whose every control sits behind `AuthorizeView` with the visitor signed out (`/players`,
`/games`). Those call sites use **`gotoRendered`**, which waits for the page to stop fetching rather
than for a handler — past the point where a circuit would have negotiated, which is also what keeps
it from aborting a handshake on the way out. It is not a way to make a flaky click pass: on a page
that does bind handlers, waiting for them is the whole point.

`rendermode.spec.js` is where the render-mode split is pinned — that `/stats`, the player pages and
the match report open no WebSocket at all, and that `/games` still does, so the probe cannot rot
into passing on a listener that stopped working.

The other half of the answer is `clickFor(locator, expectation)`: it clicks, checks for the outcome,
and clicks again if it has not happened. Use it for anything idempotent. **Do not** use it for
anything that is not — the seeded-password change is clicked exactly once on purpose, because a
second attempt would use a password that is no longer current.

### Fixtures and isolation

`global-setup.js` runs once and leaves the app in a state a spec can start from: the language pinned
to English (so a selector is the same string as the source text it came from), the seeded admin's
password changed — it locks every route to `/settings` until it is — a small squad named `Fixture
…`, and one match on file for the specs that only read. It saves two browser states, an admin one
and a visitor one that carries the language cookie and nothing else, so an anonymous test is not
also a Dutch test.

Specs share one app and one database and run in a single worker, so they stay out of each other's
way by naming what they create after themselves rather than by counting rows.

Runs on every pull request as a required check — see "Is it stable enough for CI?" above.

