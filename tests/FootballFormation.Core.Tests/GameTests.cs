using FootballFormation.Core.Models;

namespace FootballFormation.Core.Tests;

public class GameTests
{
    [Theory]
    [InlineData(GameSplitType.Halves, 60, 2, 30.0)]
    [InlineData(GameSplitType.Quarters, 60, 4, 15.0)]
    // A duration that does not divide into whole minutes keeps its fraction rather than truncating.
    [InlineData(GameSplitType.Halves, 45, 2, 22.5)]
    [InlineData(GameSplitType.Quarters, 50, 4, 12.5)]
    [InlineData(GameSplitType.Quarters, 45, 4, 11.25)]
    public void Period_count_and_length_follow_the_split(
        GameSplitType split, int duration, int expectedCount, double expectedLength)
    {
        var game = TestData.Game(split: split, durationMinutes: duration);

        Assert.Equal(expectedCount, game.PeriodCount);
        Assert.Equal((decimal)expectedLength, game.PeriodDurationMinutes);
    }

    [Theory]
    [InlineData(GameSplitType.Halves, 45)]
    [InlineData(GameSplitType.Quarters, 50)]
    [InlineData(GameSplitType.Quarters, 45)]
    [InlineData(GameSplitType.Halves, 61)]
    public void The_periods_always_add_back_up_to_the_full_match_length(
        GameSplitType split, int duration)
    {
        // The reason period length is carried in seconds: a fraction of a minute per period used
        // to be truncated away, so four quarters of a 50 minute match added up to 48.
        var game = TestData.Game(split: split, durationMinutes: duration);

        Assert.Equal(duration * 60, game.PeriodCount * game.PeriodDurationSeconds);
    }

    [Fact]
    public void HasLineup_is_false_until_someone_is_placed()
    {
        var game = TestData.Game();
        game.AddPeriod(PeriodType.FirstHalf);

        Assert.False(game.HasLineup);

        game.Periods[0].PlayerPositions.Add(TestData.Starter(1, PlayerPosition.GK, 0));
        Assert.True(game.HasLineup);
    }

    [Fact]
    public void A_match_in_progress_is_never_complete_however_many_goals_are_logged()
    {
        var game = TestData.Game();
        game.MatchState = MatchState.InProgress;
        game.ScoreHome = 3;
        game.ScoreAway = 1;

        // Otherwise the season table would shift while the game is still being played.
        Assert.False(game.IsComplete);
    }

    [Theory]
    [InlineData(MatchState.Finished, null, null, true)]      // whistled off, score not yet typed in
    [InlineData(MatchState.Finished, 2, 1, true)]
    [InlineData(MatchState.NotStarted, 2, 1, true)]          // never run live, result entered by hand
    [InlineData(MatchState.NotStarted, null, null, false)]   // a future fixture
    [InlineData(MatchState.NotStarted, 2, null, false)]      // half-entered score
    public void IsComplete_covers_both_the_live_and_the_hand_entered_route(
        MatchState state, int? home, int? away, bool expected)
    {
        var game = TestData.Game();
        game.MatchState = state;
        game.ScoreHome = home;
        game.ScoreAway = away;

        Assert.Equal(expected, game.IsComplete);
    }

    [Fact]
    public void PlayedDurationMinutes_uses_real_timings_when_the_game_was_run_live()
    {
        var game = TestData.Game(durationMinutes: 60);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;      // 30 min
        second.StartedAtSeconds = 1800;
        second.EndedAtSeconds = 3300;     // 25 min — the ref cut it short

        Assert.True(game.HasActualTimings);
        Assert.Equal(55, game.PlayedDurationMinutes);
    }

    [Fact]
    public void PlayedDurationMinutes_falls_back_to_the_schedule_when_nothing_was_timed()
    {
        var game = TestData.Game(durationMinutes: 60);
        game.AddPeriod(PeriodType.FirstHalf);

        Assert.False(game.HasActualTimings);
        Assert.Equal(60, game.PlayedDurationMinutes);
    }

    [Fact]
    public void PlayedDurationMinutes_ignores_a_period_that_is_still_running()
    {
        var game = TestData.Game();
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;
        second.StartedAtSeconds = 1800;   // no end yet

        Assert.Equal(30, game.PlayedDurationMinutes);
    }

    [Fact]
    public void ElapsedSecondsAt_adds_the_running_stretch_to_the_banked_total()
    {
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 600;
        game.ClockRunningSince = now.AddSeconds(-45);

        Assert.True(game.IsClockRunning);
        Assert.Equal(645, game.ElapsedSecondsAt(now));
    }

    [Fact]
    public void A_stopped_clock_reads_the_same_at_any_instant()
    {
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 600;
        game.ClockRunningSince = null;

        Assert.False(game.IsClockRunning);
        Assert.Equal(600, game.ElapsedSecondsAt(DateTime.UtcNow));
        Assert.Equal(600, game.ElapsedSecondsAt(DateTime.UtcNow.AddYears(5)));
    }

    [Fact]
    public void A_clock_anchor_in_the_future_never_runs_backwards()
    {
        // Clock skew between server and the instant an anchor was written must not produce a
        // negative elapsed time, which would read as a match un-playing itself.
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 100;
        game.ClockRunningSince = now.AddSeconds(30);

        Assert.Equal(100, game.ElapsedSecondsAt(now));
    }

    [Fact]
    public void Squad_players_are_in_the_roster_unless_marked_unavailable()
    {
        var player = TestData.Player(1);
        var squad = TestData.Squad(1, [player]);
        var game = TestData.Game();

        Assert.True(game.IsInRoster(player, squad));

        game.UnavailablePlayerIds.Add(player.Id);
        Assert.False(game.IsInRoster(player, squad));
    }

    [Fact]
    public void Guests_are_out_of_the_roster_unless_explicitly_added()
    {
        var guest = TestData.Player(2);
        var squad = TestData.Squad(1, [guest], guestIds: 2);
        var game = TestData.Game();

        Assert.False(game.IsInRoster(guest, squad));

        game.GuestPlayerIds.Add(guest.Id);
        Assert.True(game.IsInRoster(guest, squad));
    }

    [Fact]
    public void The_roster_rule_reads_each_games_own_season()
    {
        // Guest in season 1, regular in season 2 — the same player, judged differently per game.
        var player = TestData.Player(1);
        var squads = new SeasonSquads([
            new SeasonSquadMember { SeasonId = 1, PlayerId = 1, Player = player, IsGuest = true },
            new SeasonSquadMember { SeasonId = 2, PlayerId = 1, Player = player, IsGuest = false }
        ]);

        var guestSeasonGame = TestData.Game(id: 1, seasonId: 1);
        var memberSeasonGame = TestData.Game(id: 2, seasonId: 2);

        Assert.False(guestSeasonGame.IsInRoster(player, squads));
        Assert.True(memberSeasonGame.IsInRoster(player, squads));
    }

    [Fact]
    public void SelectRoster_keeps_only_the_players_taking_part()
    {
        var a = TestData.Player(1, "A");
        var b = TestData.Player(2, "B");
        var guest = TestData.Player(3, "G");
        var squad = TestData.Squad(1, [a, b, guest], guestIds: 3);

        var game = TestData.Game();
        game.UnavailablePlayerIds.Add(b.Id);
        game.GuestPlayerIds.Add(guest.Id);

        Assert.Equal([1, 3], game.SelectRoster([a, b, guest], squad).Select(p => p.Id));
    }

    [Fact]
    public void An_own_goal_counts_for_the_opponent_and_not_for_us()
    {
        List<GameGoal> goals = [
            TestData.Goal(scorerId: 1),
            TestData.Goal(scorerId: 2),
            TestData.Goal(scorerId: 3, ownGoal: true),
            TestData.Goal(opponentGoal: true)
        ];

        Assert.Equal(2, Game.CountOurGoals(goals));
        Assert.Equal(2, Game.CountTheirGoals(goals));
    }

    [Fact]
    public void The_clock_goes_from_one_half_to_the_next_rather_than_from_quarter_to_quarter()
    {
        var game = QuartersGame();

        // Before kick-off the next period is simply the first one.
        Assert.Equal(PeriodType.FirstQuarter, game.NextPeriod()!.PeriodType);

        // The first half has been played, so the line-up planned for the rest of it is behind the
        // clock — the whistle hands over to the half that follows.
        game.Periods.Single(p => p.PeriodType == PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Assert.Equal(PeriodType.ThirdQuarter, game.NextPeriod()!.PeriodType);

        game.Periods.Single(p => p.PeriodType == PeriodType.ThirdQuarter).StartedAtSeconds = 1800;
        Assert.Null(game.NextPeriod());
    }

    private static Game QuartersGame() => new()
    {
        Opponent = "X",
        SplitType = GameSplitType.Quarters,
        Periods = [.. PeriodTypeExtensions.ForSplitType(GameSplitType.Quarters)
            .Select(type => new GamePeriod { PeriodType = type })]
    };

    [Fact]
    public void Split_period_count_is_derived_from_the_period_table_itself()
    {
        foreach (var split in Enum.GetValues<GameSplitType>())
            Assert.Equal(PeriodTypeExtensions.ForSplitType(split).Length, split.PeriodCount());
    }
}
