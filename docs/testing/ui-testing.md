# UI Testing

## UI tests (`tests/ui`)

```bash
cd tests/ui
npm ci               # first time, and after the lockfile moves
npm test             # everything, ~4 minutes
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
| `authorization.spec.js` | The public/admin split — a visitor reads the squad, fixtures and stats, is offered no control that writes, and is bounced from `/preferences` and `/users` with the route it wanted remembered, and reaches `/settings` for the language and nothing else |
| `squad.spec.js` | Adding, editing and archiving a player; a nameless player is refused and told why |
| `games.spec.js` | Creating, editing and deleting a match; season defaults filling the form; the missing-lineup warning appearing only for a match already played |
| `match-day.spec.js` | The journey the app exists for: drag a lineup onto the pitch, save it, run the match live, log goals, blow the whistle, and find the scoreline on the games list — plus the playing-time table dropping its `~` estimate for the match clock once that has happened |
| `trainings.spec.js` | Registering a session and who was not at it, the attendance register behind it, and a session offering the squad of its own season rather than of today |
| `localization.spec.js` | Dutch by default, the switcher moving the whole app to English, and the choice surviving a navigation |
| `mobile.touchline.spec.js` | The phone layout — the drawer, the full-screen match sheet, the stacked squad — in the `mobile` project on a Pixel 7 |
| `reconnect.spec.js` | Losing the circuit and getting it back: the retry schedule a suspended phone rejoins on, and the rejoined page still being interactive |
| `session.spec.js` | Staying signed in: the auth cookie carrying a real expiry rather than being a session cookie, surviving a link followed in from another site, a deleted account losing its authority on an open circuit without anyone reloading, and an admin who changes their own password being signed out and back in |
| `live.spec.js` | The live screen past kick-off: a substitution swapping the pitch chip for a bench one and undoing back, the assist asked for after the scorer and a goal corrected into an own goal from the live timeline, the clock stopping at half time and the second half resuming from the banked total, and a spectator's inert pitch |
| `result.spec.js` | The result page: what a visitor is shown of a finished match — the score and the public note, and none of the coach's parts (minutes, half lengths, the line-up per minute, the summary, every edit and correction) — an own goal counting for the opponent, a scorer and assister reaching their own statistics, and the scoreline printed in venue order |
| `result-corrections.spec.js` | Correcting a finished match from `/result`: half lengths that survive a reload, a goal re-timed rather than retyped, and two players swapped at a minute on the line-up per minute and undone from the timeline — all as an admin; `result.spec.js` holds that a visitor is offered none of it |
| `upcoming-season.spec.js` | The season ahead: copying last season's squad forward once and never twice, the archived left behind, a member removed and re-added as a guest, and a season window that would leave a gap being refused |
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
own classes are deliberately left out — those are not ours to rename, and an upgrade that drops one
shows up as a spec failing for real.

**The class names are read out of the tests themselves**, from the string literals in every spec and
in `helpers.js`, `fixtures.js` and `global-setup.js`. It used to be a hand-kept list, and that list
had drifted to under two thirds of the classes the specs used while still naming eleven no test did.
There is nothing to add by hand any more. What it cannot see is a class name assembled at run time
from a variable, so write selectors as literals.

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

Everything lives in `.github/workflows/ci.yml`, on one chain whose four required checks are
`Build and test`, `Coverage`, `Playwright` and `Visual check`:

```
Build and test ──┬── Coverage
                 ├── Playwright 1/2 ──┬── Playwright
                 ├── Playwright 2/2 ──┘
                 └── Visual check
```

**The Playwright run is two shards**, each on its own runner with its own app and database, and
`Playwright` itself is a tiny job that is green only when both are. That keeps the required check's
name while halving the slowest job. It runs with `if: always()` on purpose: a skipped required check
counts as passed, so a failed build or shard has to report there as red rather than as skipped.
Playwright splits by whole spec file, in sorted order and balanced by test count, so the boundary
moves as tests are added: a spec creates what it reads rather than relying on one before it.

**`Build and test`** restores, builds Release, runs `dotnet test`, then publishes `--no-build`, so it
hands on exactly what the unit tests ran against rather than compiling the commit a second time. The
test step carries `--collect:"XPlat Code Coverage"`, so the report comes out of the run that is
already the gate. This replaced a second workflow that compiled the commit twice more, plus a third
time inside Playwright's `dotnet run`: four compiles of one commit became one.

**`Coverage`** runs `scripts/coverage.mjs` over that report — the same script and 80% floor as
locally. It is the one job checked out with `fetch-depth: 0`, because judging a change means diffing
against its merge base and a single-commit checkout has nothing to diff against. The verdict and a
per-file table with the uncovered line numbers go to `$GITHUB_STEP_SUMMARY`.

**`Playwright` and `Visual check`** download the published artifact and start it. Neither calls a
compiler — they install the SDK only for the runtime. `UI_TEST_APP_DLL` and `VISUAL_APP_DLL` point
each harness at the artifact; both are unset locally, where each falls back to `dotnet run`.

Three details are deliberate:

- **Publish, not build.** The output has to survive the trip to another runner. A published directory
  is self-describing; a `bin/` tree needs the SDK and the sources it was built from.
  It also changes one header, which one spec depends on — see below.
- **`-p:PublishReadyToRun=false`.** R2R forces a runtime-identifier-specific publish that
  `--no-build` cannot satisfy.
- **The `runtimes/` prune.** 84MB of the 104MB published is SQLitePCLRaw's native library for every
  architecture it supports. Keeping only `linux-x64` takes the artifact to 23MB.

**Both `package-lock.json` files are committed** (`tests/ui/` and `scripts/`), CI installs with
`npm ci`, and both caches are keyed on the lockfile. Without one, `^1.56.1` let CI move to every new
Playwright release on its own, while the browser cache, keyed on an unchanged `package.json`, kept
restoring the old browser — so every run downloaded Chrome and never saved it. Moving Playwright is
now a lockfile change in a pull request. `npx playwright install` still runs on a cache hit: the
download is a no-op there, but `--with-deps` still spends about 16 seconds in apt.

### The one spec that needs the published app

`service-worker.spec.js` **skips unless `UI_TEST_APP_DLL` is set**, and the reason is a header rather
than a shortcut. `MapStaticAssets` answers a fingerprinted route with `max-age=31536000, immutable`
when the app is published, and `no-cache` for the very same file from a `dotnet run` off the sources
— so an edit is picked up during development. The worker caches on that header alone
(`isImmutable`), so from a source run there is nothing for it to keep and the spec asserted against
an empty cache on every local run.

The skip is keyed to **how the app was started**, not to a probe of the symptom: on the published run
CI actually makes, the spec runs, and a build that stopped emitting the header fails here rather than
skipping quietly. To run it by hand:

```bash
dotnet publish src/FootballFormation.Web -c Release -o /tmp/ff-publish
UI_TEST_APP_DLL=/tmp/ff-publish/FootballFormation.Web.dll npm test -- service-worker
```

### What triggers it

**`pull_request`, and that is the whole of it**, plus `workflow_dispatch` as the escape hatch.
`actions/checkout` resolves a `pull_request` event to `refs/pull/N/merge` — the branch already merged
into `main` — where a dispatch checks out the branch tip. Merging is what deploys and nothing re-runs
on `main`, so this event is the last word on the commit that reaches the volume.

The cost of carrying no `push` trigger is that a branch gets no CI until a pull request exists for
it. The symptom of a regression here is a pull request whose checks never appear rather than a red
one; one more commit, or the dispatch, recovers it.

### Is it stable enough for CI?

Measured, not assumed, when the suite was about a minute long: eleven consecutive full runs green,
including three pinned to two cores with busy loops competing, which stretched a run to 2.2–2.5
minutes and changed nothing else. That is the
retry-on-outcome design doing its job — `clickFor` absorbs a slow circuit instead of failing on it.

A red run holds the merge, and so does a flake — re-run the job from the run's page, because the
ruleset grants no bypass. `trace: 'retain-on-failure'` means a failing test can be replayed with
`npx playwright show-trace`, and `CI=true` turns on one retry so a test that only passes on the retry
is reported as flaky rather than quietly green.

A test that dates a match in the past (`createMatch(page, { past: true })`, `pickDaysAgo`) carries
`test.skip(noEarlierDayThisSeason(), …)`. The season is chosen from the date, so on 1 July "yesterday"
is last season and the match would drop out of the list the test is about to read. That is the one
day a year it skips; it used to be the 1st of every month.

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

**The signal is a marker the app renders for the tests.** `InteractiveShell`, which every interactive
page opens with, carries `<span hidden data-circuit="…">`: `pending` in the prerender, `live` from
`RendererInfo.IsInteractive` in the circuit's first render — and a render arrives with every handler
in it attached. `goto()` waits until no marker reads `pending`, so a page with no circuit at all
(`/stats`, `/games/{id}/overview`, `/login`) is ready once it has loaded, and an interactive one once
its circuit has drawn. It then waits for the web fonts, so nothing is measured in the fallback.
`settle()` is the same wait for a page reached some other way — signing in through the form lands on
the interactive start page, and navigating away mid-handshake logs "Failed to complete negotiation",
which the console-error check fails. Both hold after a full load only: after an in-app navigation the
previous page's `live` marker stays in the DOM until the new markup lands.

That replaced Blazor's own `_bl_<guid>` attributes, which turned out to be written only for handlers
on MudBlazor's controls: a page that drew none for the visitor (`/players` and `/games` signed out)
never showed one, so it needed a second helper, `gotoRendered`, that waited for the network to go
quiet instead — at least half a second on each of about 115 navigations. One `goto` now covers every
page. There is no `gotoRendered` or `waitForHandlers` any more, and nothing should bring them back.

**The marker fails open.** An interactive page without its `<InteractiveShell />` has no marker, so
`goto` takes its inert prerender as ready and the clicks race the circuit again — intermittently, with
nothing timing out to say why. A `goto` that *does* time out means a marker never left `pending`: the
circuit never drew, as in the published-app trap in `docs/known_issues/general.md`.

There is not a single fixed sleep in the directory, and adding one is how the suite starts failing
on a slow machine.

`rendermode.spec.js` is where the render-mode split is pinned — that `/stats`, the player pages and
the match report, and `/games` and `/players` for a visitor, open no WebSocket at all, and that `/`
still does, so the probe cannot rot
into passing on a listener that stopped working.

The same rule holds in `scripts/`, where `blazor.mjs` carries `goto`, `clickFor`,
`waitForStableBox` and `waitUntil` for the visual harness. That harness was written before any of
this was understood and was built on fourteen fixed sleeps; replacing them with waits on the thing
itself took a local run from **123s to 67s** and made it steadier rather than less safe — verified
by reintroducing the three regressions it exists to catch and watching it fail on all of them. The
two copies are deliberate: `scripts/` and `tests/ui/` are separate npm packages with different
dependencies, and a dozen duplicated lines beat a cross-package import. Change one, look at the
other.

`waitForStableBox` is the one worth knowing about. MudBlazor scales a dialog and a popover in, so
anything measured the moment it becomes visible is measured mid-animation — a full-width sheet reads
about 86% of its width. Two identical bounding boxes a frame apart is the exact answer, and it costs
what the animation actually takes rather than what a sleep guessed.

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
way by naming what they create after themselves rather than by counting rows. A CI shard is a second
app and database, never a second worker on the same one.

**A second browser comes from a fixture, never from `browser.newContext()`.** `visitor` is a
signed-out English page and `openPage(options)` any other — a parent watching, a phone. Both are held
to the same console-error check as `page` and closed when the test ends, pass or fail. A context
opened by hand skips that check, so its "nothing is shown here" assertions pass just as well on a page
that failed to render; twenty of them did.

**Build the common states with the helpers in `helpers.js`**, not inline — `playedMatch`, for one, is
a match run through both halves with a goal against and left on its result page. The same click
sequences used to be pasted five to seven times over, and fixing a step meant finding every copy.

**Tests that read different things off the same state share it.** The visitor's view of a finished
match is one test asserting every admin-only part is missing, not five that each play a match first;
the result corrections run as a `test.describe.serial` group on one played match.

Runs on every pull request as a required check — see "Is it stable enough for CI?" above.

