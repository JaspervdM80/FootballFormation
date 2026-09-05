using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// What the ff.team cookie resolves to. The fallback matters more than the cookie does: it is what every visitor who has never chosen a
/// team gets, and what the write guard is handed when nobody has.
public class CurrentTeamTests : ServiceTestBase
{
    [Fact]
    public async Task With_nothing_remembered_it_is_the_first_team()
    {
        var first = SeedTeam("GJS", "MO15-2");
        SeedTeam("GJS", "MO17-1");

        Assert.Equal(first.Id, await Resolve(storedTeamId: null));
    }

    [Fact]
    public async Task A_remembered_team_wins()
    {
        SeedTeam("GJS", "MO15-2");
        var second = SeedTeam("GJS", "MO17-1");

        Assert.Equal(second.Id, await Resolve(second.Id));
    }

    [Fact]
    public async Task A_remembered_team_that_no_longer_exists_falls_back()
    {
        var first = SeedTeam("GJS", "MO15-2");

        // A team deleted since would otherwise scope every page to nothing — the same rule SeasonState applies to its own cookie.
        Assert.Equal(first.Id, await Resolve(storedTeamId: 404));
    }

    [Fact]
    public async Task Before_anything_is_seeded_there_is_no_team()
    {
        Assert.Null(await Resolve(storedTeamId: null));
    }

    [Fact]
    public async Task The_answer_is_resolved_once_and_shared()
    {
        var first = SeedTeam("GJS", "MO15-2");
        var currentTeam = new CurrentTeam(RawDbFactory, null);

        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => currentTeam.GetIdAsync()));

        Assert.All(answers, id => Assert.Equal(first.Id, id));
    }

    private Task<int?> Resolve(int? storedTeamId) => new CurrentTeam(RawDbFactory, storedTeamId).GetIdAsync();
}
