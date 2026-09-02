# EF Core Conventions

## EF Core Conventions
- **DbContext**: `AppDbContext` with primary constructor
- **Include chains are named, not respelled.** `Core/Data/GameQueries.cs` holds every shape a
  `Game` is loaded in, as `IQueryable<Game>` extension methods composed at the call site:

  ```csharp
  var game = await db.Games
      .AsNoTrackingWithIdentityResolution()
      .WithNamedLineups()
      .WithGoalsAndScorers()
      .WithSubstitutionPlayers()
      .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
  ```

  Pairs, shallow then deep over one navigation: `WithPeriods` / `WithPeriodLineups` /
  `WithNamedLineups`, `WithGoals` / `WithGoalsAndScorers`, `WithSubstitutions` /
  `WithSubstitutionPlayers`. Compose one of a pair, never both — EF rejects a filtered and an
  unfiltered include of the same navigation in one query, which is also why the deep ones include
  their collection twice.

  **Deliberately not a repository:** they stay `IQueryable`, so tracking, filtering, ordering and
  tagging remain the caller's, each being a decision made for a documented reason
  (`AsNoTrackingWithIdentityResolution` for the Blazor circuit, `QueryTags.ComparesDatesInSql`).
  Only the chain is shared, because that is the part that drifted — spelled out in two files it
  fails *silently* when one copy changes.
- **Value converters**: `List<PlayerPosition>` → comma-separated ints; `List<int>` → comma-separated values. Both need `ValueComparer` for change tracking.
- **SavePeriodLineupAsync**: Deletes all existing positions, then inserts fresh entities with `Id = 0` to avoid UNIQUE constraint errors (never reuse tracked entity IDs). Both halves run inside one `BeginTransactionAsync` — delete-then-insert needs all of it or none, or a failed insert leaves the period with no lineup at all rather than the one it had. It **refuses a period that `HasKickedOff`** before any of that: the delete would throw away the rows the touchline wrote and hand out new ids, and the only caller is a page whose cache can be an hour stale. See [known_issues](../known_issues/live-match.md).
- **Auto-migration**: `db.Database.MigrateAsync()` in Program.cs startup
- **Never order or compare a date in the query.** SQLite keeps every `DateTime` in a TEXT column,
  so `ORDER BY Date` sorts the text a date was written as. Materialise first, then use
  `GameOrdering` / `SeasonOrdering` (`Models/Game.cs`, `Models/Season.cs`). See
  [known_issues](../known_issues/index.md) for what goes wrong and the one deliberate exception.
- **Data backfills** belong in the migration's `Up()` via `migrationBuilder.Sql(...)`, not in
  startup code: `__EFMigrationsHistory` runs them exactly once, and it is the only place you can
  populate a new required FK column *before* the constraint is added. Order the operations
  `AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`. `AddSeasons`
  and `ConsolidatePlayerPositions` were written that way.
  **Do not assume atomicity:** when EF rebuilds a SQLite table it emits `PRAGMA foreign_keys = 0`,
  which cannot run inside a transaction, so the migration is *not* all-or-nothing — and EF never
  re-runs `foreign_key_check`, so a partial backfill boots silently clean. Verify with
  `SELECT COUNT(*) FROM <table> WHERE <fk> = 0` and `PRAGMA foreign_key_check` after applying.
- **Check the scaffolder's ordering before trusting it.** `AddSeasonSquads` needed `Players.IsGuest`
  dropped *and* its values copied into a new table; EF emitted the `DropColumn` **first**, which
  would have destroyed the source data before the backfill could read it. Always read the generated
  `Up()` and reorder so reads happen before drops.
- **`DropColumn` rebuilds the whole table.** When that table is a *parent* (other tables hold FKs
  into it — `Players` has three), verify afterwards that its row count is unchanged and that no
  `ef_temp_*` table survived: `SELECT name FROM sqlite_master WHERE name LIKE 'ef_temp%'`.
- **Rehearse destructive migrations on a copy.** Copy the DB to a scratch folder, point
  `APP_DATA_DIR` at it, run `dotnet ef database update`, and check the data before touching the
  real file. Fly.io auto-migrates on boot, so production gets no second chance.

### Migrations are one file

`Migrations/` holds a single migration, `20260322100416_InitialCreate`, which creates the whole
schema. The twenty that grew it between March and August 2026 were folded into it once every
database that exists had them all applied; the names still quoted in these docs — `AddSeasons`,
`AddSeasonSquads`, `StoreGoalPeriodAndClock` — are history rather than files you can open.

**The id is the original `InitialCreate`'s, not the timestamp it was scaffolded at, and that is
what makes the fold safe.** The live volume has `20260322100416_InitialCreate` in its
`__EFMigrationsHistory` already, so it boots with nothing pending and never runs this file; a fresh
id would have re-run `CREATE TABLE` over a season of data and failed the deploy. The nineteen rows
below it in that table now name migrations the assembly no longer has, which EF ignores — pending
work is what the assembly holds and the history does not.

So if you ever rescaffold this migration rather than adding one after it, **put the id back by
hand** — in both file names and in the `[Migration]` attribute in the designer file — and check
that the schema still comes out the same:

```bash
APP_DATA_DIR=/tmp/schema-check dotnet ef database update --project src/FootballFormation.Core
```

Migrations from here on are ordinary ones added on top. There is no reason to fold again until the
count is a nuisance, and doing it costs the migration bodies: `GoalClockBackfillTests` covered the
one backfill that rewrote rows, and went with the file it tested.

