namespace FootballFormation.Core.Tests;

/// What LiveMatchOperation exists for: the notification is part of the write shape rather than a line each service has to remember, and
/// the failure it prevents — a spectator's screen quietly stuck on the old score — shows up nowhere else in the suite.
public class LiveMatchNotificationTests : LiveMatchTestBase
{
    private readonly List<int> _announced = [];
    private readonly List<LiveMatchEvent> _kinds = [];
    private readonly List<int> _teams = [];

    public LiveMatchNotificationTests() => Notifier.Changed += change =>
    {
        _announced.Add(change.GameId);
        _kinds.Add(change.Event);
        _teams.Add(change.TeamId);
    };

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
        // Asked for by the substitution's own id, so this one names the game only after doing it.
        Assert.True((await Subs.RemoveSubstitutionAsync(sub.Value!.Id)).IsSuccess);

        Assert.True((await Subs.SwapPositionsAsync(game.Id, players[0].Id, players[1].Id)).IsSuccess);

        Assert.True((await MatchClock.EndHalfAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.StartNextHalfAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.FinishMatchAsync(game.Id)).IsSuccess);

        // Nine writes, nine announcements, each naming this match.
        Assert.Equal(9, _announced.Count);
        Assert.All(_announced, id => Assert.Equal(game.Id, id));
    }

    [Fact]
    public async Task Only_kick_off_a_goal_and_full_time_are_worth_waking_a_phone_for()
    {
        var game = await SeedGameAsync(GameSplitType.Halves);
        var players = await PlayersAsync();

        Assert.True((await MatchClock.StartMatchAsync(game.Id)).IsSuccess);
        Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false)).IsSuccess);
        Assert.True((await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id)).IsSuccess);
        Assert.True((await MatchClock.EndHalfAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.StartNextHalfAsync(game.Id)).IsSuccess);
        Assert.True((await MatchClock.FinishMatchAsync(game.Id)).IsSuccess);

        Assert.Equal(
            [LiveMatchEvent.KickOff, LiveMatchEvent.Goal, LiveMatchEvent.Other, LiveMatchEvent.Other,
             LiveMatchEvent.Other, LiveMatchEvent.FullTime],
            _kinds);
    }

    [Fact]
    public async Task Every_announcement_names_the_team_whose_match_it_was()
    {
        SeedTeam("Club B", "MO17-1");
        var other = await SeedGameAsync();
        var otherTeam = CurrentTeam.Id!.Value;

        var team = SeedTeam("GJS", "MO15-2");
        var game = await SeedGameAsync();

        Assert.True((await MatchClock.StartMatchAsync(game.Id)).IsSuccess);

        SeedTeam("Club B", "MO17-1");
        Assert.True((await MatchClock.StartMatchAsync(other.Id)).IsSuccess);

        Assert.Equal([game.Id, other.Id], _announced);
        Assert.Equal([team.Id, otherTeam], _teams);
    }

    [Fact]
    public async Task A_refused_write_says_nothing()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        _announced.Clear();

        // A rule broken and an unknown row, across all three services.
        Assert.True((await MatchClock.StartNextHalfAsync(game.Id)).IsFailure);
        Assert.True((await Goals.LogGoalAsync(game.Id, null, null, false, false)).IsFailure);
        Assert.True((await Goals.RemoveGoalAsync(game.Id, 999)).IsFailure);
        Assert.True((await Subs.SubstituteAsync(game.Id, 999, 998)).IsFailure);
        Assert.True((await Subs.SwapPositionsAsync(game.Id, 999, 998)).IsFailure);
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
