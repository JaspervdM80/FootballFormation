namespace FootballFormation.Core.Tests;

/// A calendar app fetches the feed with no cookie, so nothing is in scope — the team has to come from the URL or the game, and only that
/// team's fixtures may come back.
public class MatchCalendarQueryTests : ServiceTestBase
{
    private readonly MatchCalendarQuery _calendar;

    public MatchCalendarQueryTests()
    {
        _calendar = new MatchCalendarQuery(RawDbFactory);
    }

    [Fact]
    public async Task A_teams_feed_holds_its_own_games_oldest_first_and_none_of_another_teams()
    {
        var ours = SeedTeam("GJS", "MO15-2");
        var ourSeason = await SeedSeasonAsync();
        var later = await SeedGameAsync(ours.Id, ourSeason.Id, "Later", new DateTime(2026, 3, 21, 10, 0, 0));
        var sooner = await SeedGameAsync(ours.Id, ourSeason.Id, "Sooner", new DateTime(2026, 3, 7, 10, 0, 0));

        var theirs = SeedTeam("GJS", "JO11-1");
        var theirSeason = await SeedSeasonAsync();
        await SeedGameAsync(theirs.Id, theirSeason.Id, "Elsewhere", new DateTime(2026, 3, 14, 10, 0, 0));

        // Scoped to the other team on purpose: the feed must not care what the ambient scope says.
        var calendar = await _calendar.ForTeamAsync(ours.Id);

        Assert.NotNull(calendar);
        Assert.Equal("GJS MO15-2", calendar.TeamFullName);
        Assert.Equal([sooner.Id, later.Id], calendar.Games.Select(g => g.Id));
    }

    [Fact]
    public async Task An_unknown_team_has_no_feed()
    {
        SeedTeam();

        Assert.Null(await _calendar.ForTeamAsync(9999));
    }

    [Fact]
    public async Task A_single_game_names_its_own_team_whichever_team_is_in_scope()
    {
        var ours = SeedTeam("GJS", "MO15-2");
        var season = await SeedSeasonAsync();
        var game = await SeedGameAsync(ours.Id, season.Id, "Opponent", new DateTime(2026, 3, 14, 10, 0, 0));

        SeedTeam("GJS", "JO11-1");

        var calendar = await _calendar.ForGameAsync(game.Id);

        Assert.NotNull(calendar);
        Assert.Equal("GJS MO15-2", calendar.TeamFullName);
        Assert.Equal(game.Id, Assert.Single(calendar.Games).Id);
    }

    [Fact]
    public async Task An_unknown_game_has_no_calendar()
    {
        SeedTeam();

        Assert.Null(await _calendar.ForGameAsync(9999));
    }

    private async Task<Game> SeedGameAsync(int teamId, int seasonId, string opponent, DateTime date)
    {
        var game = new Game { Opponent = opponent, Date = date, TeamId = teamId, SeasonId = seasonId };
        Db.Games.Add(game);
        await Db.SaveChangesAsync();
        return game;
    }
}
