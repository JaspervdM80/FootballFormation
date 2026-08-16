---
name: migrations
description: Adding or reviewing an EF Core migration. The app auto-migrates against the live SQLite volume on deploy, so a bad Up() is a bad production database. Use whenever a migration is scaffolded, a column is dropped, or a backfill is written.
---

# Migrations

```bash
dotnet ef migrations add <Name> --project src/FootballFormation.Core
dotnet ef database update    --project src/FootballFormation.Core
```

`DesignTimeDbContextFactory` means no `--startup-project`. Migrations are run from `Core` alone.

**The app migrates itself unattended against the live volume on the next deploy.** `Program.cs` takes
a pre-migration snapshot and refuses to migrate if that fails, but the snapshot is the last resort,
not the plan.

## Read the generated `Up()` and reorder it

The scaffolder does not know what your backfill needs to read. `AddSeasonSquads` needed
`Players.IsGuest` dropped *and* its values copied into a new table; EF emitted the `DropColumn`
**first**, which would have destroyed the source data before the backfill could read it.

Order operations: **`AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`.**
Reads happen before drops.

## Backfills belong in the migration, not in startup code

`migrationBuilder.Sql(...)` in `Up()`. `__EFMigrationsHistory` runs it exactly once, and it is the only
place a new required FK column can be populated *before* the constraint is added. See `AddSeasons` and
`ConsolidatePlayerPositions`.

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

## Rehearse anything destructive on a copy

Copy the database to a scratch folder, point `APP_DATA_DIR` at it, run `dotnet ef database update`,
and check the data before touching the real file. Production gets no second chance.

## Boot-time safety net

Startup copies the database to `/data/backups/pre-migration-<last applied>.db` when — and only when —
migrations are pending, keeping the newest 5, then migrates, then runs `PRAGMA integrity_check` and
`PRAGMA foreign_key_check`. The snapshot is named for the *schema state*, not the attempt, so a crash
loop cannot prune the only good copy. A failed backup aborts the migration on purpose.

Detail: [docs/deployment.md](../../../docs/deployment.md) ·
[docs/patterns.md](../../../docs/patterns.md)
