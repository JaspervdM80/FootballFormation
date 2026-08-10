using FootballFormation.Core.Models;

namespace FootballFormation.Core.Tests;

/// <summary>
/// Reading a live match: which match the home page points at on a given day, and that the live
/// screen's own read comes back with everything it renders. The writing is tested next door, in
/// <see cref="MatchClockServiceTests"/>, <see cref="MatchGoalServiceTests"/> and
/// <see cref="MatchSubstitutionServiceTests"/>.
/// </summary>
public class LiveMatchServiceTests : LiveMatchTestBase
{
    // ---- Match day -------------------------------------------------------------------------

    [Fact]
    public async Task A_match_in_progress_wins_over_whatever_the_calendar_says()
    {
        var yesterdaysGame = await SeedGameAsync();
        yesterdaysGame.Date = KickOff.Date.AddDays(-1);
        await Db.SaveChangesAsync();
        await MatchClock.StartMatchAsync(yesterdaysGame.Id);

        // It could have kicked off before midnight, and it is the one someone at a pitch is watching.
        var result = await Live.GetTodaysMatchAsync();

        Assert.Equal(yesterdaysGame.Id, result.Value!.Id);
    }

    [Fact]
    public async Task An_ordinary_day_has_no_match()
    {
        var game = await SeedGameAsync();
        game.Date = KickOff.Date.AddDays(7);
        await Db.SaveChangesAsync();

        var result = await Live.GetTodaysMatchAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    // ---- The live screen's read ------------------------------------------------------------

    [Fact]
    public async Task The_live_read_brings_back_the_lineups_the_goals_and_the_substitutions_at_once()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(10));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        var result = await Live.GetLiveAsync(game.Id);

        Assert.True(result.IsSuccess);
        var live = result.Value!;
        // One round trip: the screen renders all of this without asking again.
        Assert.Equal(2, live.Periods.Count);
        Assert.NotEmpty(live.Periods.First().PlayerPositions);
        Assert.NotNull(live.Periods.First().PlayerPositions[0].Player);
        Assert.Equal(players[1].Id, live.Goals.Single().Scorer!.Id);
        Assert.Equal(players[2].Id, live.Substitutions.Single().PlayerOn!.Id);
    }

    [Fact]
    public async Task Reading_a_game_that_is_not_there_fails_rather_than_throwing()
    {
        var result = await Live.GetLiveAsync(999);

        Assert.True(result.IsFailure);
        Assert.Contains("999", result.Error);
    }

    /// <summary>
    /// Reads stay open — the live screen is the one URL a parent at the touchline is given, and
    /// nobody signs in to watch.
    /// </summary>
    [Fact]
    public async Task Anyone_can_read_a_live_match()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        CurrentUser.IsAdmin = false;

        var live = await Live.GetLiveAsync(game.Id);
        var today = await Live.GetTodaysMatchAsync();

        Assert.True(live.IsSuccess);
        Assert.Equal(game.Id, today.Value!.Id);
    }
}
