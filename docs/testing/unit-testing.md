# Unit Testing

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
  service method takes (see [patterns](../patterns/result-and-cancellation.md#cancellation-the-third-outcome)) —
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
  enforces is in [known_issues](../known_issues/index.md); the short version is materialise first, then
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
by driving the real app in a real browser, which is what `tests/ui` does.

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
  a component, and nothing in `tests/` renders one. Those two are covered by `tests/ui` instead,
  and a change there is reported as unmeasured rather than as a miss.
- **Migrations and the model snapshot.** A `Down()` is never executed by the suite and never will
  be, and counting scaffolded code makes the gate a lottery on how much of it a change touched.
  Excluding them took `Core` from a comfortable 96.4% over 9,960 lines to an honest 93.3% over
  2,509 — measured while twenty migrations were on file. Folding them into one shrank the
  scaffolded half but not the reason for the rule: the next migration adds it straight back.
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

