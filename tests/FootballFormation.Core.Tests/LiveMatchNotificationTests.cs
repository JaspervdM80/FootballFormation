using FootballFormation.Core.Models;

namespace FootballFormation.Core.Tests;

/// <summary>
/// That every write to a match being played tells the viewers about it, and that a refused one does
/// not. This is what <c>LiveMatchOperation</c> exists for: the notification is part of the write
/// shape rather than a line each of the three services has to remember, and the failure it prevents
/// — a spectator's screen quietly stuck on the old score — shows up nowhere else in the suite.
/// </summary>
public class LiveMatchNotificationTests : LiveMatchTestBase
{
    private readonly List<int> _announced = [];

    public LiveMatchNotificationTests() => Notifier.Changed += _announced.Add;

    [Fact]
    public async Task Every_touchline_write_names_the_game_it_changed()
    {
        var game = await SeedGameAsync(GameSplitType.Quarters);
        var players = await PlayersAsync();

        Assert.True((await MatchClock.StartMatchAsync(game.Id)).IsSuccess);
        Time.Advance(TimeSpan.FromMinutes(5));

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.True(goal.IsSuccess);
        Assert.True((await Goals.RemoveGoalAsync(game.Id, goal.Value!.Id)).IsSuccess);

        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Assert.True(sub.IsSuccess);
        // Asked for by the substitution's own id: this one only learns which game it changed by
        // doing the work, and it still has to name it.
        Assert.True((await Subs.RemoveSubstitutionAsync(sub.Value!.Id)).IsSuccess);

        Assert.True((await MatchClock.AdvancePeriodAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.PauseClockAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.ResumeClockAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.EndPeriodAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.StartNextPeriodAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.FinishMatchAsync(game.Id)).IsSuccess);

        // Eleven writes, eleven announcements, each naming this match.
        Assert.Equal(11, _announced.Count);
        Assert.All(_announced, id => Assert.Equal(game.Id, id));
    }

    [Fact]
    public async Task A_refused_write_says_nothing()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        _announced.Clear();

        // A rule broken and an unknown row, across all three services.
        Assert.True((await MatchClock.PauseClockAsync(999)).IsFailure);
        Assert.True((await Goals.LogGoalAsync(game.Id, null, null, false, false)).IsFailure);
        Assert.True((await Goals.RemoveGoalAsync(game.Id, 999)).IsFailure);
        Assert.True((await Subs.SubstituteAsync(game.Id, 999, 998)).IsFailure);
        Assert.True((await Subs.RemoveSubstitutionAsync(999)).IsFailure);

        Assert.Empty(_announced);
    }

    [Fact]
    public async Task A_write_refused_for_want_of_an_admin_says_nothing_either()
    {
        var game = await SeedGameAsync();
        CurrentUser.IsAdmin = false;

        var result = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Empty(_announced);
    }
}
