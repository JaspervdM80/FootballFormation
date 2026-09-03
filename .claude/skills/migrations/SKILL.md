---
name: migrations
description: Adding or reviewing an EF Core migration. The base migration's id is load-bearing, and the app auto-migrates against the live SQLite volume on deploy, so a bad Up() is a bad production database. Use whenever a migration is scaffolded, rescaffolded, a column is dropped, or a backfill is written.
---

# Migrations

```bash
dotnet ef migrations add <Name> --project src/FootballFormation.Core
dotnet ef database update    --project src/FootballFormation.Core
```

`DesignTimeDbContextFactory` means no `--startup-project`. Migrations are run from `Core` alone.

`dotnet ef` is not always on PATH. If the command is not found, call the tool by its full path
(`~/.dotnet/tools/dotnet-ef`, `.exe` on Windows) and give `--project` an absolute path.

**The app migrates itself unattended against the live volume on the next deploy.** `Program.cs` takes
a pre-migration snapshot and refuses to migrate if that fails, but the snapshot is the last resort,
not the plan.

## The base migration's id is load-bearing

`Migrations/` starts at **`20260322100416_InitialCreate`**, which carries the whole schema: the
twenty that grew it were folded into it once every database that exists had them all applied. Three
ordinary migrations have been added on top of it since, in August 2026.

**That id is the original `InitialCreate`'s, not the timestamp of the scaffold that wrote the file,
and that is what makes the fold safe.** The live volume already lists it in `__EFMigrationsHistory`,
so production boots with nothing pending and never applies the file. A fresh id would make the entire
schema pending against a database that already has it — the boot would `CREATE TABLE` over a season
of results and fail the deploy. The nineteen rows below it in that history name migrations the
assembly no longer has, which EF ignores: pending work is what the assembly holds and the history
does not.

So **if you ever rescaffold this migration rather than adding one after it, put the id back by
hand** — in both file names and in the `[Migration]` attribute in the designer file — and check the
schema still comes out the same:

```bash
APP_DATA_DIR=/tmp/schema-check dotnet ef database update --project src/FootballFormation.Core
```

Migrations from here are ordinary ones added on top, and there is no reason to fold again until the
count is a nuisance. Folding costs the migration bodies, so anything a `Up()` did that is worth
remembering has to be written down before it goes.

**Names like `AddSeasons`, `AddSeasonSquads` and `StoreGoalPeriodAndClock` are history, not files.**
They are still quoted below and in `docs/` because the lessons are real; do not go looking for them
on disk.

## Read the generated `Up()` and reorder it

The scaffolder does not know what your backfill needs to read. `AddSeasonSquads` needed
`Players.IsGuest` dropped *and* its values copied into a new table; EF emitted the `DropColumn`
**first**, which would have destroyed the source data before the backfill could read it.

Order operations: **`AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`.**
Reads happen before drops.

## Backfills belong in the migration, not in startup code

`migrationBuilder.Sql(...)` in `Up()`. `__EFMigrationsHistory` runs it exactly once, and it is the only
place a new required FK column can be populated *before* the constraint is added. `AddSeasons` and
`ConsolidatePlayerPositions` were written that way.

## A SQLite migration is not atomic

When EF rebuilds a table it emits `PRAGMA foreign_keys = 0`, which cannot run inside a transaction —
so the migration is *not* all-or-nothing, and EF never re-runs `foreign_key_check`. A half-applied
backfill boots silently clean.

Verify after applying:

```sql
SELECT COUNT(*) FROM <table> WHERE <fk> = 0;
PRAGMA foreign_key_check;
SELECT name FROM sqlite_master WHERE name LIKE 'ef_temp%';   -- must be empty
```

**`DropColumn` rebuilds the whole table.** When that table is a *parent* (other tables hold FKs into
it — `Players` has three), check afterwards that its row count is unchanged and no `ef_temp_*` table
survived.

## Where the database is

`DatabasePathHelper.GetDatabasePath()` resolves in order: `APP_DATA_DIR`, then `WEBSITE_INSTANCE_ID`
(`/home/data`), then `%LOCALAPPDATA%\FootballFormation\`. In production that is the Fly.io volume;
locally it is `%LOCALAPPDATA%\FootballFormation\footballformation.db` — one file shared by every
branch, so a local *"no such column"* means the database and the code disagree, not that the file
is corrupt. It can be either way round: **ahead**, from a branch that added the column and was then
left behind, or **behind**, when the history records a squashed migration the file never actually
received. `scripts/dev-db.sh` replaces the local file with a copy of the live one.

## Rehearse anything destructive on a copy

Copy the database to a scratch folder, point `APP_DATA_DIR` at it, run `dotnet ef database update`,
and check the data before touching the real file. Production gets no second chance.

## Boot-time safety net

Startup copies the database to `/data/backups/pre-migration-<last applied>.db` when — and only when —
migrations are pending, keeping the newest 5, then migrates, then runs `PRAGMA integrity_check` and
`PRAGMA foreign_key_check`. The snapshot is named for the *schema state*, not the attempt, so a crash
loop cannot prune the only good copy. A failed backup aborts the migration on purpose.

It then compares every table and column the model maps against `pragma_table_info` and refuses to
serve on a mismatch. Migrating can report success and change nothing — `__EFMigrationsHistory`
alone decides what runs — and neither pragma notices a database that is incomplete rather than
damaged.

Detail: [docs/patterns/](../../../docs/patterns/ef-core.md#migrations-are-one-file) ·
[docs/deployment.md](../../../docs/deployment.md) ·
[docs/known_issues/](../../../docs/known_issues/ef-core.md)
