using Microsoft.Data.Sqlite;

namespace FootballFormation.Core.Tests;

/// The two factories the app wires by hand: the scoped one every service receives, which stamps the team and club so the query filters
/// bite, and the raw one CurrentTeam and the boot loops take to reach a context without first resolving a team.
public class TeamScopedDbContextFactoryTests
{
    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Filename=:memory:").Options;

    [Fact]
    public async Task The_scoped_factory_stamps_the_team_and_club_in_scope()
    {
        var scoped = (IDbContextFactory<AppDbContext>)new TeamScopedDbContextFactory(
            new RawDbContextFactory(Options()), new FakeCurrentTeam { Id = 7, ClubId = 3 });

        await using var db = await scoped.CreateDbContextAsync();

        Assert.Equal(7, db.CurrentTeamId);
        Assert.Equal(3, db.CurrentClubId);
    }

    [Fact]
    public void The_raw_factory_makes_an_unstamped_context()
    {
        var raw = (IRawDbContextFactory)new RawDbContextFactory(Options());

        using var db = raw.CreateDbContext();

        Assert.Null(db.CurrentTeamId);
        Assert.Null(db.CurrentClubId);
    }

    [Fact]
    public void The_scoped_factory_refuses_a_synchronous_create()
    {
        var scoped = (IDbContextFactory<AppDbContext>)new TeamScopedDbContextFactory(
            new RawDbContextFactory(Options()), new FakeCurrentTeam());

        // A team can only be resolved asynchronously before a query runs, so the sync overload fails fast rather than returning
        // an unstamped context that would read as another team's.
        Assert.Throws<NotSupportedException>(() => scoped.CreateDbContext());
    }
}
