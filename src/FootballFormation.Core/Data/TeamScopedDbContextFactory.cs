using FootballFormation.Core.Security;

namespace FootballFormation.Core.Data;

/// A context with nothing stamped on it, so its query filters match nothing. CurrentTeam uses it to resolve the team off the cookie
/// without asking itself which team it is, and the boot steps use it to stamp a team by hand and walk every one. Everything else takes the
/// scoped factory below and never sees this.
public interface IRawDbContextFactory
{
    AppDbContext CreateDbContext();
}

public sealed class RawDbContextFactory(DbContextOptions<AppDbContext> options) : IRawDbContextFactory
{
    public AppDbContext CreateDbContext() => new(options);
}

/// The default <see cref="IDbContextFactory{AppDbContext}"/> every service receives. It stamps each context with the team and club in
/// scope before handing it over, so the query filters in <see cref="AppDbContext"/> scope every read without a service having to remember
/// to — a query that forgets its team returns nothing rather than another team's rows. See docs/patterns/authorization-and-auth.md.
public sealed class TeamScopedDbContextFactory(IRawDbContextFactory raw, ICurrentTeam currentTeam) : IDbContextFactory<AppDbContext>
{
    /// The team has to be resolved to stamp the context, and that resolution is async — a sync create could only get there by blocking.
    public AppDbContext CreateDbContext() => throw new NotSupportedException(
        "Use CreateDbContextAsync: a team-scoped context resolves its team before any query runs.");

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var db = raw.CreateDbContext();
        db.CurrentTeamId = await currentTeam.GetIdAsync();
        db.CurrentClubId = await currentTeam.GetClubIdAsync();
        return db;
    }
}
