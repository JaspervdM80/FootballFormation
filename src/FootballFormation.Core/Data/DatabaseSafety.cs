using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Data;

/// <summary>
/// What runs around <c>MigrateAsync()</c> on startup so a schema change cannot quietly cost the
/// club its season.
/// <para>
/// The app migrates itself the moment the container boots, unattended, against a single SQLite
/// file on one volume. Migrations are not all reversible in practice — <c>AddMatchTypeAndComments</c>
/// drops a column, <c>AddMustChangePasswordAndLineupUniqueIndex</c> deletes rows — so by the time
/// anyone notices a bad one, the previous state is gone. A copy taken immediately before is the
/// only thing that makes such a change undoable.
/// </para>
/// </summary>
public static class DatabaseSafety
{
    /// <summary>How many pre-migration snapshots to keep before the oldest is pruned.</summary>
    public const int KeepBackups = 5;

    /// <summary>
    /// Copies the database if — and only if — migrations are about to change it, and at most once
    /// per schema state. A restart with nothing pending is by far the common case (Fly wakes this
    /// app from zero), and writing a snapshot each time would fill the volume with identical files
    /// and push the useful ones out of the retention window.
    /// </summary>
    /// <returns>
    /// The snapshot's path — freshly written, or the one already held for this schema state — or
    /// null when there was nothing to migrate.
    /// </returns>
    public static async Task<string?> BackupBeforeMigrationsAsync(
        AppDbContext db, string dbPath, ILogger logger)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0) return null;

        logger.LogInformation("{Count} migration(s) pending: {Migrations}",
            pending.Count, string.Join(", ", pending));

        if (!File.Exists(dbPath))
        {
            logger.LogInformation("No database yet at {DbPath} — nothing to back up", dbPath);
            return null;
        }

        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
        Directory.CreateDirectory(backupDir);

        // Named for the schema state being left behind, not for the moment the copy is taken, so a
        // state gets exactly one snapshot however many times the app tries to migrate away from it.
        //
        // That is what makes a crash loop survivable. Several migrations here run outside a
        // transaction, so one that fails partway leaves the rest pending — and Fly restarts the
        // machine. With a per-attempt name, every restart wrote another snapshot of the *broken*
        // database and pruned an older one, so five restarts destroyed the only good copy, in about
        // as many minutes, precisely when it was the thing that mattered.
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var schemaState = applied.Count > 0 ? applied[^1] : "empty";
        var backupPath = Path.Combine(backupDir, $"pre-migration-{schemaState}.db");

        if (File.Exists(backupPath))
        {
            logger.LogWarning("Reusing the snapshot already taken of this schema state: {BackupPath}", backupPath);
            return backupPath;
        }

        // Written under a temporary name and moved into place, because the copy is not atomic: a
        // container killed midway through one would otherwise leave a truncated file under the name
        // that means "this state is safely backed up", and every later boot would trust it.
        var pendingPath = backupPath + ".tmp";

        // Whatever a previous attempt left here is garbage by definition, and SQLite will not write
        // into it — it opens the destination as a database and refuses a file that is not one. Left
        // in place that turns a single killed backup into a boot the app can never complete, since
        // a failed backup deliberately aborts the migration.
        File.Delete(pendingPath);

        // SQLite's own backup API, not File.Copy: with WAL journalling the .db file alone can be
        // missing everything still in the -wal, so a plain copy is a torn snapshot of exactly the
        // rows most recently written.
        await using (var source = new SqliteConnection($"Data Source={dbPath}"))
        await using (var destination = new SqliteConnection($"Data Source={pendingPath}"))
        {
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }

        File.Move(pendingPath, backupPath, overwrite: true);

        var size = new FileInfo(backupPath).Length;
        logger.LogWarning("Database backed up to {BackupPath} ({Size:N0} bytes) before applying {Count} migration(s)",
            backupPath, size, pending.Count);

        Prune(backupDir, logger);
        return backupPath;
    }

    /// <summary>
    /// Asks SQLite whether the file it just migrated is still sound, and whether every foreign key
    /// still points at something. Runs after the migration rather than before: a migration that
    /// rebuilds a table (SQLite does this for many alterations) is precisely where referential
    /// integrity gets lost, and finding out on the next page load instead means finding out from a
    /// parent on match day.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database is damaged.</exception>
    public static async Task VerifyIntegrityAsync(AppDbContext db, ILogger logger)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check";
            var result = (await cmd.ExecuteScalarAsync())?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogCritical("Database integrity check failed: {Result}", result);
                throw new InvalidOperationException($"Database integrity check failed: {result}");
            }
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await cmd.ExecuteReaderAsync();

            var violations = new List<string>();
            while (await reader.ReadAsync() && violations.Count < 10)
                violations.Add($"{reader.GetValue(0)} row {reader.GetValue(1)} → {reader.GetValue(2)}");

            if (violations.Count > 0)
            {
                logger.LogCritical("Foreign key violations after migration: {Violations}",
                    string.Join("; ", violations));
                throw new InvalidOperationException(
                    $"Foreign key violations after migration: {string.Join("; ", violations)}");
            }
        }

        logger.LogInformation("Database integrity verified");
    }

    /// <summary>Keeps the newest <see cref="KeepBackups"/> snapshots — newest by name, which is
    /// newest by schema state, since a migration id begins with the timestamp it was scaffolded at.
    /// A pruning failure is logged and swallowed — a full backup folder is a problem, but not one
    /// worth refusing to boot over, unlike a missing backup.</summary>
    private static void Prune(string backupDir, ILogger logger)
    {
        try
        {
            var stale = new DirectoryInfo(backupDir)
                .GetFiles("pre-migration-*.db")
                .OrderByDescending(f => f.Name)
                .Skip(KeepBackups)
                .ToList();

            foreach (var file in stale)
            {
                file.Delete();
                logger.LogInformation("Pruned old backup {BackupName}", file.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not prune old database backups");
        }
    }
}
