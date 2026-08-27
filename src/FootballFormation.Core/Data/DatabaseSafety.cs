using Microsoft.Data.Sqlite;

namespace FootballFormation.Core.Data;

/// This app migrates itself unattended on boot, against one SQLite file, and not every migration is reversible in practice — so the copy
/// taken immediately before is the only thing that makes a bad one undoable. See docs/deployment.md.
public static class DatabaseSafety
{
    public const int KeepBackups = 5;

    /// At most one snapshot per schema state: a restart with nothing pending is the common case, and a copy each time would push the
    /// useful ones out of the retention window. Returns null when there was nothing to migrate.
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

        // Named for the schema state left behind, not for the moment of the copy, which is what makes a crash loop survivable: with a
        // per-attempt name, five restarts pruned away the only good snapshot in about as many minutes.
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var schemaState = applied.Count > 0 ? applied[^1] : "empty";
        var backupPath = Path.Combine(backupDir, $"pre-migration-{schemaState}.db");

        if (File.Exists(backupPath))
        {
            logger.LogWarning("Reusing the snapshot already taken of this schema state: {BackupPath}", backupPath);
            return backupPath;
        }

        // The copy is not atomic, so it lands under a temporary name: a container killed midway would otherwise leave a truncated file
        // under the name that means "safely backed up", and every later boot would trust it.
        var pendingPath = backupPath + ".tmp";

        // SQLite opens the destination as a database and refuses one that is not, so a leftover partial file would turn a single killed
        // backup into a boot that can never complete — a failed backup aborts the migration by design.
        File.Delete(pendingPath);

        // SQLite's own backup API, not File.Copy: with WAL journalling a plain copy is a torn snapshot missing the newest rows.
        // Pooling off because disposing a pooled connection only returns it to the pool — the file handle stays open, and the rename
        // below then fails on Windows. POSIX allows renaming over an open file, so the container never sees it.
        await using (var source = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        await using (var destination = new SqliteConnection($"Data Source={pendingPath};Pooling=False"))
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

    /// Throws when the database is damaged. Runs after the migration, not before: SQLite rebuilds a table for many alterations, and that
    /// is precisely where a foreign key gets lost.
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

    /// Newest by name is newest by schema state, since a migration id begins with the timestamp it was scaffolded at. A pruning failure
    /// is swallowed: a full backup folder is not worth refusing to boot over, unlike a missing backup.
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
