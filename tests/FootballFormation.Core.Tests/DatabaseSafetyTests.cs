using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// A real file on disk rather than the shared in-memory database: a backup of one that lives only in memory would prove nothing.
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
        // Roll the history back so the same migrations read as pending again — the shape of a deploy about to change a live database.
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
            var club = db.Clubs.Add(new Core.Models.Club { Name = "GJS" }).Entity;
            await db.SaveChangesAsync();
            db.Players.Add(new Core.Models.Player { FirstName = "Backed up", ClubId = club.Id, ShirtNumber = 7 });
            await db.SaveChangesAsync();
        }
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var path = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // A File.Copy of a WAL database can miss the newest writes, so reading the row back is what distinguishes a real snapshot.
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

        // A restart with nothing pending is the common case, and a snapshot each time would push the useful ones out of retention.
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
        // A migration failing partway leaves the rest pending and Fly restarts the machine, so a snapshot named for the moment would
        // capture the damage once per restart. Only the copy from before the first attempt is worth keeping.
        await using (var db = Open()) await db.Database.MigrateAsync();
        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");

        await using var ctx = Open();
        var first = await DatabaseSafety.BackupBeforeMigrationsAsync(ctx, _dbPath, NullLogger.Instance);

        // Stands in for the damage a half-applied migration does, so a fresh snapshot would be visibly different from the one on disk.
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
            var club = db.Clubs.Add(new Core.Models.Club { Name = "GJS" }).Entity;
            await db.SaveChangesAsync();
            db.Players.Add(new Core.Models.Player { FirstName = "Survives", ClubId = club.Id, ShirtNumber = 9 });
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

        // What a container killed midway through the copy leaves behind — the whole guard rests on that file being complete.
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

        // Foreign keys go unenforced on a connection that has not switched them on, which is exactly how a table-rebuilding migration
        // leaves orphans behind.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO GamePeriods (GameId, PeriodType) VALUES (999999, 0)");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSafety.VerifyIntegrityAsync(db, NullLogger.Instance));
        Assert.Contains("Foreign key", ex.Message);
    }

    [Fact]
    public async Task A_migrated_database_matches_the_model()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();

        await DatabaseSafety.VerifySchemaAsync(db, NullLogger.Instance);
    }

    [Fact]
    public async Task A_column_the_migration_history_claims_was_applied_is_reported_by_name()
    {
        // What a database written before the migrations were squashed looks like: the fold reads as applied, its columns absent.
        await using var db = Open();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players DROP COLUMN IsArchived");

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSafety.VerifySchemaAsync(db, NullLogger.Instance));
        Assert.Contains("Players is missing IsArchived", ex.Message);
    }

    [Fact]
    public async Task A_table_the_model_expects_and_the_database_lacks_is_reported()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE GameComments");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSafety.VerifySchemaAsync(db, NullLogger.Instance));
        Assert.Contains("GameComments does not exist", ex.Message);
    }

    [Fact]
    public async Task A_column_the_model_does_not_map_is_left_alone()
    {
        // A branch that added a column and was then left behind is the everyday local drift, and EF simply ignores the leftover.
        await using var db = Open();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN Trialist INTEGER NOT NULL DEFAULT 0");

        await DatabaseSafety.VerifySchemaAsync(db, NullLogger.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }
}
