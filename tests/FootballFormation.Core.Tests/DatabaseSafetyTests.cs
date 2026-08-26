using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The startup guards around migration. These need a real file on disk rather than the shared
/// in-memory database the other suites use — a backup of a database that lives only in memory
/// would prove nothing about the case that matters.
/// </summary>
public class DatabaseSafetyTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public DatabaseSafetyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ff-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "footballformation.db");
    }

    private AppDbContext Open() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private string BackupDir => Path.Combine(_dir, "backups");

    private static string[] Backups(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "pre-migration-*.db") : [];

    [Fact]
    public async Task A_database_with_migrations_pending_is_backed_up_first()
    {
        // Migrate to a real schema, then roll the history back so the same migrations read as
        // pending again — the shape of a deploy that is about to change a live database.
        await using (var db = Open()) await db.Database.MigrateAsync();
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Single(Backups(BackupDir));
    }

    [Fact]
    public async Task The_backup_is_a_readable_database_carrying_the_rows()
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            db.Players.Add(new Core.Models.Player { FirstName = "Backed up", ShirtNumber = 7 });
            await db.SaveChangesAsync();
        }
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // A File.Copy of a WAL database can miss the most recent writes entirely. Reading the row
        // back out is what distinguishes a real snapshot from a plausible-looking file.
        await using var restored = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await restored.OpenAsync();
        await using var cmd = restored.CreateCommand();
        cmd.CommandText = "SELECT FirstName FROM Players";
        Assert.Equal("Backed up", (await cmd.ExecuteScalarAsync())?.ToString());
    }

    [Fact]
    public async Task Nothing_is_written_when_no_migration_is_pending()
    {
        await using (var db = Open()) await db.Database.MigrateAsync();

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // Fly wakes this app from zero, so a restart with nothing pending is the common case.
        // Writing a snapshot each time would push the useful ones out of the retention window.
        Assert.Null(path);
        Assert.Empty(Backups(BackupDir));
    }

    [Fact]
    public async Task A_fresh_install_has_nothing_to_back_up()
    {
        await using var ctx = Open();

        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        Assert.Null(path);
    }

    [Fact]
    public async Task Only_the_most_recent_backups_are_kept()
    {
        await using (var db = Open()) await db.Database.MigrateAsync();
        Directory.CreateDirectory(BackupDir);

        // Older than anything the run below will create — the names sort chronologically.
        for (var i = 0; i < DatabaseSafety.KeepBackups + 3; i++)
            await File.WriteAllTextAsync(Path.Combine(BackupDir, $"pre-migration-20200101-0000{i:00}.db"), "old");

        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        Assert.Equal(DatabaseSafety.KeepBackups, Backups(BackupDir).Length);
    }

    [Fact]
    public async Task Retrying_a_failed_migration_does_not_take_a_second_snapshot()
    {
        // A migration that fails partway leaves the rest pending, and Fly restarts the machine. The
        // restart arrives here with the database in whatever state the failure left it — so a
        // snapshot named for the moment would capture the damage, and there would be one per
        // restart. Only the copy taken before the first attempt is worth keeping.
        await using (var db = Open()) await db.Database.MigrateAsync();
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var first = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // Stand in for the damage a half-applied migration does, so a fresh snapshot would be
        // visibly different from the one already on disk.
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM Players");

        for (var restart = 0; restart < DatabaseSafety.KeepBackups + 2; restart++)
        {
            var again = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);
            Assert.Equal(first, again);
        }

        Assert.Single(Backups(BackupDir));
    }

    [Fact]
    public async Task The_snapshot_a_crash_loop_keeps_is_the_one_from_before_the_failure()
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            db.Players.Add(new Core.Models.Player { FirstName = "Survives", ShirtNumber = 9 });
            await db.SaveChangesAsync();
        }
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // The failure wipes the table; the restarts must not carry that into the backup folder.
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM Players");
        for (var restart = 0; restart < DatabaseSafety.KeepBackups + 2; restart++)
            await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        await using var restored = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await restored.OpenAsync();
        await using var cmd = restored.CreateCommand();
        cmd.CommandText = "SELECT FirstName FROM Players";
        Assert.Equal("Survives", (await cmd.ExecuteScalarAsync())?.ToString());
    }

    [Fact]
    public async Task A_snapshot_left_half_written_is_not_mistaken_for_a_finished_one()
    {
        await using (var db = Open()) await db.Database.MigrateAsync();
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        // What a container killed midway through the copy leaves behind. It must not be picked up
        // as the snapshot for this schema state — the whole guard rests on that file being complete.
        Directory.CreateDirectory(BackupDir);
        var torn = Path.Combine(BackupDir, "pre-migration-empty.db.tmp");
        await File.WriteAllTextAsync(torn, "truncated");

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        Assert.NotNull(path);
        await using var restored = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await restored.OpenAsync();
        await using var cmd = restored.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Players";
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_healthy_database_passes_the_integrity_check()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();

        await DatabaseSafety.VerifyIntegrityAsync(db, NullLogger.Instance);
    }

    [Fact]
    public async Task A_dangling_foreign_key_fails_the_integrity_check()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();

        // Foreign keys are not enforced on a connection that has not switched them on, which is
        // exactly how a table-rebuilding migration can leave orphans behind. The check exists to
        // catch that afterwards rather than on someone's next page load.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO GamePeriods (GameId, PeriodType) VALUES (999999, 0)");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSafety.VerifyIntegrityAsync(db, NullLogger.Instance));
        Assert.Contains("Foreign key", ex.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }
}
