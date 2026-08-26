using Microsoft.EntityFrameworkCore.Design;

namespace FootballFormation.Core.Data;

/// For the EF Core CLI tools at design time — nothing at runtime resolves this.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var dbPath = DatabasePathHelper.GetDatabasePath();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new AppDbContext(optionsBuilder.Options);
    }
}
