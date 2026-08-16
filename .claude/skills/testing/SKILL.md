---
name: testing
description: Writing or changing an xUnit test in tests/FootballFormation.Core.Tests, or checking coverage. Covers ServiceTestBase, TestData, real SQLite, FakeTimeProvider, sentence-style names, and the 80% changed-lines floor. Use when adding a test or running scripts/coverage.sh.
---

# Testing

`dotnet test` from the repo root. xUnit v3.

## Conventions

- **Test names are sentences.** `A_match_in_progress_is_never_complete_however_many_goals_are_logged`
  says what the rule is, so a failure names the rule that broke.
- **Real SQLite, not the in-memory provider.** `ServiceTestBase` opens a `Filename=:memory:`
  connection and holds it open. The services lean on foreign keys, unique indexes, cascades and the
  CSV value converters — the in-memory provider enforces none of it, so a test passing there can
  still fail against the database the app ships with.
- **Services are constructed in `ServiceTestBase`**, not per test class.
- **Arrange with `TestData`.** A game is a four-level graph (game → periods → lineups → players);
  building one inline buries the single fact the test is about. Repetition inside `Arrange` is
  deliberate — tests are meant to read standalone.
- **Time is injected.** `FakeTimeProvider` drives the match clock, so
  `Time.Advance(TimeSpan.FromMinutes(7))` is a seven-minute half rather than a sleep. Never
  `DateTime.UtcNow` in a service — take `TimeProvider`.
- **Comment the why, not the what.** A test pinning a subtle rule should say what would break without
  it.

## Where a test goes

Pure domain logic on a model or in `Core/Reporting` needs no fixture — those are pure functions.
Anything touching the database inherits `ServiceTestBase`: `Db` arranges and asserts, and `Read()`
gives a fresh context for reading back what a service wrote without tracking interference.

**There are no component tests and no bUnit, by design.** A Razor component is never rendered in
isolation. Do not ask for one — the UI is covered by `tests/ui` and `scripts/visual-check.sh`.

## Two suppressions to leave alone

- **`xUnit1051`** is suppressed in the csproj. The analyzer wants
  `TestContext.Current.CancellationToken` on every EF call; against in-memory SQLite that is noise on
  hundreds of call sites buying no responsiveness. Production request lifetime is a different
  question, answered by the token every service method takes — `CancellationTests` passes those
  explicitly, which is the point.
- **`DateInSqlInterceptor`** is registered on the context factory by `ServiceTestBase`, so any query
  sorting or comparing a TEXT date column in SQL throws `DateComparedInSqlException` naming the
  column — in whichever test ran it. Columns come from the EF model, not a hand-kept list, so a new
  `DateTime` property is covered as soon as it is mapped. `DateInSqlGuardTests` pins both the refusal
  and the one `.TagWith(QueryTags.ComparesDatesInSql)` exemption.

## Coverage

```bash
scripts/coverage.sh                        # against origin/main
COVERAGE_BASE=HEAD~1 scripts/coverage.sh   # against the previous commit
COVERAGE_SKIP_TEST=1 scripts/coverage.sh   # re-judge the last run
```

The floor is **80% of the lines this branch added or rewrote**, per file, with the uncovered line
numbers listed. The gate is the change, not the repository — Core is above 96%, so a solution-wide
gate would pass with an entirely untested new service in the diff. If you quote a number, quote the
changed-line one, and never quote one you did not measure.

`UI` and `Web` are not measured and that is not a gap: they have no unit tests on purpose, and a
change there is answered by `tests/ui` or `visual-check.sh`. Migrations and
`DesignTimeDbContextFactory` are excluded as scaffolded or design-time code.

Coverage is a floor, not a target. 100% of a change whose only test asserts it doesn't throw is worse
than 85% with the rule pinned.

Detail: [docs/testing.md](../../../docs/testing.md)
