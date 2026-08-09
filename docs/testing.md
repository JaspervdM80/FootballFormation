# Testing

`tests/FootballFormation.Core.Tests` — xUnit v3. Run with `dotnet test` from the repo root.

CI runs `dotnet build -c Release` and `dotnet test` as a **gate**: the Fly.io deploy job depends on
it, so nothing reaches the production volume that doesn't compile and pass. Before that gate
existed a push to `main` deployed straight to production, and the app auto-migrates on boot — a bad
migration reached a live database on startup.

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
| Live match | `LiveMatchServiceTests` | Clock banking, period transitions, substitution undo |
| Games and comments | `GameServiceTests`, `GameCommentTests` | Season derivation on create, scalar-only update, and the public/private split |
| Seasons and squads | `SeasonServiceTests`, `SeasonSquadServiceTests` | Gapless windows, the single current season, copy-forward and the removal guards |
| Players | `PlayerServiceTests` | That deleting someone who has played is refused, and that archiving them changes nothing already recorded |
| Match preferences | `MatchPreferencesServiceTests` | Per-season inheritance, and next-match dates staying inside the window |
| **Authorization** | `AuthorizationTests` | That every write refuses a non-admin *at the service*, not only in the markup — the guard the whole write path rests on |
| Accounts | `UserServiceTests`, `SeededAdminTests` | Credentials, security stamps, the last-admin guard, and the seeded account being no working login |
| Boot safety | `DatabaseSafetyTests`, `HealthReportTests` | The pre-migration snapshot and what `/health` is allowed to call healthy |
| Service lifetime | `ServiceLifetimeTests` | Concurrent reads, and detached entities round-tripping through update |
| `Result` | `ResultTests` | Error keys, arguments, and the guard on reading a failed value |

## Conventions

- **Test names are sentences.** `A_match_in_progress_is_never_complete_however_many_goals_are_logged`
  says what the rule is; a failure names the rule that broke.
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
`scripts/visual-check.sh` (rendering and touch geometry) do. The `verify-ui` skill still describes
the manual desktop/mobile × anonymous/admin matrix for anything neither of those covers.

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
| `match-day.spec.js` | The journey the app exists for: drag a lineup onto the pitch, save it, run the match live, log goals, blow the whistle, and find the scoreline on the games list |
| `localization.spec.js` | Dutch by default, the switcher moving the whole app to English, and the choice surviving a navigation |
| `mobile.touchline.spec.js` | The phone layout — the drawer, the full-screen match sheet, the stacked squad — in the `mobile` project on a Pixel 7 |
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

### Is it stable enough for CI?

Measured, not assumed. Eleven consecutive full runs at the time of writing, every one green:
eight on an idle machine at about a minute each, and three pinned to two cores with three
busy loops competing for them, which stretched a run to 2.2–2.5 minutes and changed nothing else.
That is the retry-on-outcome design doing its job — `clickFor` absorbs a slow circuit instead of
failing on it.

Two things would still need doing before it becomes a gate: the job has to install a Chromium
(`npx playwright install --with-deps chromium`, and `playwright.config.js` already falls back to
Playwright's own browser when `/opt/pw-browsers/chromium` is absent), and it should run as its own
job rather than inside **Build and test**, so a failure there is legible as a UI failure.

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

Not wired into CI yet — deliberately. See the stability measurements above for what it would take.

## Visual checks

`scripts/visual-check.sh` boots the app and screenshots every page into `artifacts/visual/`
(ignored by git). It builds, starts the app on a **throwaway database** in a temp directory, signs
in, seeds a small squad through the real dialogs, and captures each page at 1440×900. It exits
non-zero if the browser logged an error, which is where a Blazor render failure shows up.

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
[known_issues.md](known_issues.md) argues from — and walks the new-match dialog and its date picker:
the form at the top and scrolled to the bottom, then the picker's day, month and year views. Five
screens per size, screenshotted into `artifacts/visual/touch/` with every measurement written to
`report.md` beside them.

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

Adding a screen is a few lines in `auditTouchTargets`. The dialog and the picker are covered first
because every entry in that doc section was found in one of them.

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
