# Testing

`tests/FootballFormation.Core.Tests` — xUnit v3. Run with `dotnet test` from the repo root.

CI runs `dotnet build -c Release` and `dotnet test` as a **gate on the merge**: it is one of the four
checks that have to be green before a pull request can land, and since landing is what deploys, a
commit that does not compile or does not pass never becomes a release. The gate used to sit one step
later — the deploy job depended on a re-run of this workflow — and it moved forward when merging
became the release. What stands between a merge and the volume now is the Docker build, which
compiles the app again and fails the deploy if it cannot, and the `/health` smoke check, which
refuses to call the release good until the new commit is the one answering.

Getting that ordering right matters because the app auto-migrates on boot: a bad migration reaches a
live database on startup, and the only cheap place to catch it is here.

## What is covered

Every test class, so a gap here is visible rather than assumed:

| Area | File | Why it matters |
| --- | --- | --- |
| `Game`, `Season`, `SeasonSquad` | `GameTests`, `SeasonTests`, `SeasonSquadTests` | The domain rules — season windows, the roster rule, the clock arithmetic |
| Date ordering | `GameOrderingTests`, `SeasonOrderingTests` | Pins the rule that dates are sorted after materialising, never in SQL |
| Formation slots | `FormationSlotsTests` | Every pitch draws from this; a bug lays lineups out wrong |
| Formations | `FormationTypeTests` | Each shape must field exactly ten outfield players |
| Report builders | `GameMinutesReportTests`, `PlayerStatsReportTests`, `SeasonStatsReportTests`, `PlayingTimeReportTests`, `PlannedChangesReportTests`, `MatchClockReportTests` | Minutes and statistics — wrong answers here are invisible until a season is over |
| Position fit | `PositionFitHelperTests` | The five tiers that colour every chip |
| Live match | `LiveMatchServiceTests`, `MatchClockServiceTests`, `MatchGoalServiceTests`, `MatchSubstitutionServiceTests`, `LiveMatchNotificationTests` | Split the way the services are: the live screen's read and the home-page match, clock banking and period transitions, the live goal minute, the slot swap and its undo, and that every write announces the game it changed while a refused one stays silent — the invariant `LiveMatchOperation` exists to hold. All share `LiveMatchTestBase` |
| Games and comments | `GameServiceTests`, `GameCommentTests` | Season derivation on create, scalar-only update, the public/private split, and that a single game comes back with its whole graph — `GetByIdAsync` shares its `GameQueries` shapes with `GetLiveAsync`, and a level dropped from one fails silently in the app |
| Seasons and squads | `SeasonServiceTests`, `SeasonSquadServiceTests` | Gapless windows, the single current season, copy-forward and the removal guards |
| Players | `PlayerServiceTests` | That deleting someone who has played is refused, and that archiving them changes nothing already recorded |
| Match preferences | `MatchPreferencesServiceTests` | Per-season inheritance, and next-match dates staying inside the window |
| **Authorization** | `AuthorizationTests` | That every write refuses a non-admin *at the service*, not only in the markup — the guard the whole write path rests on |
| Accounts | `UserServiceTests`, `SeededAdminTests` | Credentials, security stamps, the last-admin guard, and the seeded account being no working login |
| Boot safety | `DatabaseSafetyTests`, `HealthReportTests` | The pre-migration snapshot and what `/health` is allowed to call healthy |
| Migrations that rewrite rows | `GoalClockBackfillTests` | The only migration with a backfill in it. Migrates a seeded database across the boundary and asserts what the app then *shows* — a goal written `30+2` still reads `30+2` — rather than what landed in a column. Every other migration is covered implicitly, because `ServiceTestBase` builds the schema from the model |
| Service lifetime | `ServiceLifetimeTests` | Concurrent reads, and detached entities round-tripping through update |
| `Result` | `ResultTests` | Error keys, arguments, the guard on reading a failed value, and that a cancellation stays one when carried between types |
| Cancellation | `CancellationTests` | That a caller going away is an ordinary outcome and not a logged error — including that an `OperationCanceledException` nobody asked for still is one |

## Conventions

- **Test names are sentences.** `A_match_in_progress_is_never_complete_however_many_goals_are_logged`
  says what the rule is; a failure names the rule that broke.
- **`xUnit1051` is suppressed, and that is not an oversight.** The analyzer wants
  `TestContext.Current.CancellationToken` on every EF call; these tests run against in-memory SQLite
  in milliseconds, where the token would be noise on hundreds of call sites and buy no
  responsiveness. Production request lifetime is a different question, answered by the token every
  service method takes (see [patterns.md](patterns.md#cancellation-the-third-outcome)) —
  `CancellationTests` passes those tokens explicitly, which is the point. The two are unrelated;
  leave the `NoWarn` alone.
- **Real SQLite, not the in-memory provider.** `ServiceTestBase` opens a `Filename=:memory:`
  connection and keeps it open for the test. The services lean on foreign keys, unique indexes,
  cascade behaviour and the CSV value converters — the in-memory provider enforces none of that, so
  a test passing there can still fail against the database the app ships with.
- **Services are constructed in `ServiceTestBase`,** not in each test class, so the wiring lives in
  one place.
- **Every query the suite makes is watched for a date comparison in SQL.**
  `ServiceTestBase` registers `DateInSqlInterceptor` on the context factory, so any query that
  sorts or compares one of the schema's TEXT date columns in SQL throws
  `DateComparedInSqlException` naming the column — whichever test happened to run it. The rule it
  enforces is in [known_issues.md](known_issues.md); the short version is materialise first, then
  order the objects with `GameOrdering` / `SeasonOrdering`.
  The columns come from the EF model rather than a hand-kept list, so a new `DateTime` property is
  covered as soon as it is mapped. A query that genuinely has to compare in SQL opts out with
  `.TagWith(QueryTags.ComparesDatesInSql)` — one does, and `DateInSqlGuardTests` pins both the
  refusal and the exemption.
- **Time is injected.** `FakeTimeProvider` drives the match clock, so
  `Time.Advance(TimeSpan.FromMinutes(7))` is a seven-minute half rather than a sleep. Never use
  `DateTime.UtcNow` in a service — take `TimeProvider`.
- **Arrange with `TestData`.** A game is a four-level graph (game → periods → lineups → players);
  building one inline buries the single fact the test is about.
- **Comment the why, not the what.** A test that pins down a subtle rule should say what would
  break without it.

## Adding a test

Domain logic on a model or in `Core/Reporting` needs no fixture — it is a pure function. For
anything touching the database, inherit `ServiceTestBase` and use the service properties it
exposes; `Db` is for arranging and asserting, and `Read()` gives a fresh context for reading back
what a service wrote without tracking interference.

## What is not covered

No component tests — no bUnit. A Razor component is never rendered in isolation; the UI is checked
by driving the real app in a real browser, which is what `tests/ui` (behaviour) and
`scripts/visual-check.sh` (rendering and touch geometry) do.

## Coverage

```bash
scripts/coverage.sh                        # run the suite with the collector, judge this branch
COVERAGE_BASE=HEAD~1 scripts/coverage.sh   # against another base
COVERAGE_THRESHOLD=90 scripts/coverage.sh
COVERAGE_SKIP_TEST=1 scripts/coverage.sh   # re-judge the last run without re-running the suite
```

`coverlet.collector` writes a Cobertura report into `artifacts/coverage/`, and `coverage.mjs`
answers the only question a review can act on: **is the code this branch changed covered?** The
floor is **80% of the changed lines**, and the script exits non-zero under it, which is exactly how
CI runs it — see [the Coverage job](#one-pipeline-one-compile).

**The gate is the change, not the repository, and that is the whole design.** Core is above 96%
line coverage, so a solution-wide 80% gate would pass with an entirely untested new service in the
diff — the number would move by tenths. The script takes the added and rewritten lines from
`git diff --unified=0` against the merge base (uncommitted work included), keeps the ones the
instrumenter counted as coverable, and reports per file with the uncovered line numbers.

### What the collector may count

`coverage.runsettings` at the repository root is what decides, and both `scripts/coverage.sh` and
CI's test step pass it, so a local number and a pipeline number mean the same thing. Everything
below is out of the report entirely — not merely out of the judgement:

- **`UI` and `Web`**, by module (`[FootballFormation.UI]*`, `[FootballFormation.Web]*`) and by
  file (`**/*.razor`, `**/*.razor.cs`). The test project references `Core` alone, so nothing from
  either is instrumented today; naming them keeps that true the day somebody adds a reference for
  one helper. A `.razor` compiles to a generated class whose lines map back to markup and a
  `.razor.cs` is the other half of that same partial class — neither is reachable without rendering
  a component, and nothing in `tests/` renders one. Those two are covered by `tests/ui` and
  `visual-check.sh` instead, and a change there is reported as unmeasured rather than as a miss.
- **Migrations and the model snapshot.** A `Down()` is never executed by the suite and never will
  be, and counting scaffolded code makes the gate a lottery on how much of it a change touched.
  Excluding them took `Core` from a comfortable 96.4% over 9,960 lines to an honest 93.3% over
  2,509.
- **`DesignTimeDbContextFactory`**, which exists for `dotnet ef` and runs in no test.
- **Generated and deliberately-marked code**, by attribute — `GeneratedCode`, `ExcludeFromCodeCoverage`,
  `Obsolete`. `CompilerGeneratedAttribute` is deliberately *not* one of them: it is not just
  lambdas and iterator state machines, it is how the compiler marks every `async` method body and
  every auto-property, and excluding it took `ServiceOperation.RunAdminAsync` — the write guard
  every service call goes through — and most of `DatabaseSafety` out of the report along with the
  scaffolding. A change that silently stopped judging the admin check would be worse than the
  scaffolding problem this file exists to fix.

`coverage.mjs` still recognises migrations and `DesignTimeDbContextFactory` by name, and treats any
other changed `Core` file the report never mentions the same way: named under the table as excluded
rather than dropped from the diff silently, because a reviewer has to know the change contains code
this number says nothing about.

Branch coverage sits near 75% and is reported for information, not gated — the line floor is what
the `code-reviewer` agent enforces. And a floor is not a target: 100% of a change whose only test
asserts it doesn't throw is worth less than 85% with the rule pinned.

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

Everything lives in `.github/workflows/ci.yml`, in four jobs on one chain, all four required checks:

```
Build and test ──┬── Coverage
                 ├── Playwright
                 └── Visual check
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

**`Playwright` and `Visual check`** download the published artifact and start it. Neither calls a
compiler — they install the SDK only for the runtime. `UI_TEST_APP_DLL` and `VISUAL_APP_DLL` point
each harness at the artifact; both are unset locally, where each falls back to `dotnet run`.

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

The same rule now holds in `scripts/`, where `blazor.mjs` carries `goto`, `clickFor`,
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
way by naming what they create after themselves rather than by counting rows.

Runs on every pull request as a required check — see "Is it stable enough for CI?" above.

## Visual checks

`scripts/visual-check.sh` boots the app and screenshots every page into `artifacts/visual/`
(ignored by git). It builds, starts the app on a **throwaway database** in a temp directory, signs
in, seeds a small squad through the real dialogs, and captures each page at 1440×900. It exits
non-zero if the browser logged an error, which is where a Blazor render failure shows up, or if a
touch target is under its floor.

Setting `VISUAL_APP_DLL` to a published `FootballFormation.Web.dll` skips the build and runs that
copy instead — which is how the `Visual check` job does it, against what `Build and test` published.

It runs on every pull request as the `visual` job in `ci.yml` — required, like the Playwright job
beside it, so a page that stops rendering stops the merge. That job uploads `artifacts/visual/`
whether it passed or not: the
measurements are the part that can fail, but the screenshots are worth a look on a pull request that
changed a page, and nothing else in CI produces one. Locally the harness drives the Chromium in a
Claude Code web container; everywhere else `visual-check.mjs` lets Playwright resolve its own, which
is what makes the job possible at all.

Two things it has to work around, both of them the app behaving correctly:

- A fresh admin still holds the password it was seeded with, and that **locks every route to
  `/settings`** until it changes. Without that step every screenshot is the same page.
- Changing a password **rotates the security stamp**, which invalidates the cookie issued before
  it. The script signs in again afterwards, or it would browse as an anonymous visitor.

It signs in through `/dev/login` — mapped only outside Production and only for loopback callers —
so no password is typed into the login form.

## Touch targets

The same run then stops looking and starts measuring. `scripts/touch-targets.mjs` reopens the app
in three phone-sized touch contexts — **320×568**, **360×640** and **844×390** landscape, the sizes
[known_issues.md](known_issues.md) argues from — and walks the games list, the new-match dialog and
its date picker: `/games` as it renders, then the form at the top and scrolled to the bottom, then
the picker's day, month and year views. Six screens per size, screenshotted into
`artifacts/visual/touch/` with every measurement written to `report.md` beside them.

It exists because the Touch / PWA section of `known_issues.md` is the longest in the file, every
entry in it was reported from a touchline — twice — and all of them are held in place by CSS that
nothing verified. Two rules:

- **Size.** Every hit-testable element is at least **44×44** CSS px. That is what a 36px day cell,
  a 23px month name, a 40px year button and a 36.5px "Annuleren" each failed.
- **Clearance.** The gap between a target and its nearest neighbour above or beside it is either
  nothing — they meet, so there is no hole — or at least **8px**. Anything between is a dead
  gutter: too narrow to see or aim around, wide enough to swallow a tap, and awarded by the browser
  to whichever neighbour has the larger contact area. This is the measurement `elementFromPoint`
  cannot make; it reports both neighbours as perfectly reachable. The 4px gutters between
  MudBlazor's day cells were exactly this.

Together they are the column-pitch guarantee: with no dead gutter left, the distance between two
column centres *is* the cell's own width.

Where the geometry provably cannot reach 44px the number is written down in `RECORDED_FLOORS` with
the reason — a 320px phone has 308px of usable width for seven columns, and a landscape phone is
too short for six 44px rows. **A recorded floor is still a floor**: the run fails if the element
drops below the number recorded for it, so an allowance cannot quietly become a regression.

Two things the measurement deliberately skips. A target only half inside its scroll container is
not a small target — scroll it back and it is the size it always was — so the size check waits
until it is whole. And two targets in different scroll containers are however far apart the scroll
position leaves them, which is not a fact about the layout, so no clearance is reported across that
boundary.

Adding a screen is a few lines in `auditTouchTargets`. The dialog and the picker were covered first
because every entry in that doc section was found in one of them; `/games` came next because it is
the page a phone opens most and the action row on a game card is the densest cluster of targets in
the app. Everything else a thumb touches — the live screen, the formation builder, the app bar and
drawer, `/players` — is still unmeasured, and is worth adding a scene at a time so each finding is
argued on its own.

A scene is only as good as what is on the page when it runs, and that is the seeding's job, not the
audit's. The `/games` scene needs a game card to measure, so `visual-check.mjs` creates one through
the dialog before the screenshots — **dated today**, because the Live button appears on match day
only and that is the day the action row carries six buttons rather than five. Each scene is scoped
to a root selector (`.app-main` for `/games`, the dialog and the popover for the rest), which is
how a page's own targets are measured without the app bar and the install banner arriving with
them.

## Running it in Claude Code on the web

Those containers are rebuilt per session and ship no .NET SDK, so without setup an agent can read
the code but cannot compile it, run the tests, or open a page. `.claude/hooks/session-start.sh`
installs it on session start.

It takes the SDK from **Ubuntu 24.04's own archive** (`dotnet-sdk-10.0` in `noble-updates/main`),
not from `dotnet-install.sh` — the container's egress policy blocks
`builds.dotnet.microsoft.com`, so the usual installer 403s before it downloads anything.
`api.nuget.org` is reachable, so `dotnet restore` works normally.

Chromium is already in the image at `/opt/pw-browsers/chromium`; `scripts/visual-check.sh`
installs the Playwright npm package on first use and drives that binary rather than downloading
its own.
