# EF Core

- **UNIQUE constraint on save**: When re-saving `GamePlayerPosition` entities, always create NEW entities with `Id = 0`. Never re-add tracked entities with existing IDs — EF tries INSERT with the old PK.
- **List value converters need ValueComparer**: Without it, EF won't detect changes to `List<PlayerPosition>` or `List<int>` properties.
- **DB path must be absolute** — a relative path resolves against the working directory, which
  changes. `APP_DATA_DIR` is the supported override.
- **`ORDER BY` on a date sorts its text, not the date**: SQLite has no date type, so all eight
  `DateTime` columns in this schema (`Game.Date`, `Game.ClockRunningSince`, `Season.StartDate`,
  `Season.EndDate`, `GameComment.CreatedAt`/`EditedAt`, `GameGoal.RecordedAt`,
  `GameSubstitution.RecordedAt`) are TEXT, and an `ORDER BY` or a `<`/`>` in the query compares the
  string the value was written as. That matches date order only while every row carries
  byte-identical formatting — one written with an ISO `T` separator instead of EF's space (a
  restored backup, a value written by anything but this app) sorts as if the `T` were part of the
  time, because `'T'` > `' '`. **Sort and compare dates after materialising the rows**, where the
  parsed `DateTime` is what gets compared: `GameOrdering` (`Models/Game.cs`) and `SeasonOrdering`
  (`Models/Season.cs`) do it with the tie-break spelled out, and `SeasonService` reads the whole
  season table and does its window arithmetic in memory for the same reason — which is also what
  lets `Season.Contains` be the single date-only definition of a window. `GameOrderingTests` and
  `SeasonOrderingTests` pin it. The one deliberate exception is `LiveMatchService`'s
  `Date >= today && Date < tomorrow`, a same-day range kept in SQL so the games table is not
  loaded whole on every home-page hit.
  **The rule is now mechanical, because prose could not hold it.** `DateInSqlInterceptor`, wired
  into `ServiceTestBase`'s context factory, reads the SQL of every query the suite runs and throws
  on a date column in an `ORDER BY`, an inequality, a `MIN`/`MAX`, or a `BETWEEN` — so a new query
  that reintroduces this fails whichever test first executes it, rather than sorting almost-right
  until a backup is restored. `MIN`/`MAX` and `BETWEEN` are watched because they compare the stored
  TEXT just as fragilely while emitting none of the four inequality operators — `MaxAsync(g =>
  g.Date)` is the rewrite a later reader would reach for instead of materialising first, and it is
  exactly as wrong. Its column list comes from the EF model, so it covers a date property from the
  moment it is mapped. The exception opts out by name with `.TagWith(QueryTags.ComparesDatesInSql)`,
  which is the only way past and is meant to be argued for.
  Two things it does not catch: SQL no test ever executes (nothing watches a path the suite does
  not walk), and equality on a date. `=` compares text just as fragilely, but an `UPDATE ... SET
  "Date" = @p0` is an assignment wearing the same syntax, so flagging the operator would fail every
  write. `ORDER BY`, inequality, `MIN`/`MAX` and `BETWEEN` are unambiguous; equality is not, which
  is where the guard stops.
- **Sorting backup filenames as text is a different case, and it is fine**: `DatabaseSafety` names
  backups `pre-migration-<last applied migration>.db` and prunes by `OrderByDescending(f => f.Name)`.
  A migration id begins with the fixed-width timestamp it was scaffolded at, so lexicographic *is*
  chronological — the sort is on text by design, not by accident. (The name used to be a timestamp
  of the moment the copy was taken; it is the schema state now, so a crash loop cannot write five
  snapshots of the broken database and prune the only good one. See [deployment](../deployment.md).)
- **The scaffolder ordered a destructive migration wrongly**: `AddSeasonSquads` had to copy `Players.IsGuest` into a new table *and* drop the column; EF emitted the `DropColumn` first, which would have wiped the source before the backfill ran. Always read and reorder the generated `Up()`.
- **The one migration on file carries an id older than the file**: `Migrations/` holds a single
  `20260322100416_InitialCreate` that the twenty real migrations were folded into, and it keeps
  the original `InitialCreate`'s id rather than the timestamp of the scaffold that wrote it. That is
  load-bearing, not untidiness. The live volume already has that row in `__EFMigrationsHistory`, so
  it boots with nothing pending; a rescaffold that let EF assign a fresh id would make the whole
  schema pending against a database that has it all, and the boot would `CREATE TABLE` over a season
  of results and fail. Restore the id by hand in both file names and the `[Migration]` attribute —
  see [patterns](../patterns/ef-core.md#migrations-are-one-file).
- **A transaction cannot span two `AppDbContext` instances, and nothing warns you**: each operation
  opens its own context from the factory, so calling another *service's* write from inside your own
  gives two transactions with a gap between them — even though the code reads like one operation and
  every `Result` check passes. Logging a goal was shaped that way and an interruption left the goal
  on file behind a stale score. **When one row is derived from another, write both through the same
  context and commit them once.** The rule, the worked example and the two rejected alternatives are
  in [patterns](../patterns/transactions-and-writes.md#when-two-rows-have-to-agree-one-context-writes-both).
- **Collapsing that into a single `SaveChanges` looks tidier and reintroduces a lost update**:
  counting the goals in memory and adding the new one is a read-modify-write on a row with no
  concurrency token, so two admins on the same live match each write a scoreline of *n+1*.
  **Recount after the write, inside the transaction**, where SQLite's write lock has already
  serialised the two.

