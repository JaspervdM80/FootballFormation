using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

public class PlayerStatsReportTests
{
    private static readonly Player Subject = TestData.Player(1, "Subject", PlayerPosition.CM, shirt: 8);

    /// <summary>A finished 60-minute game with the subject playing both halves in one position.</summary>
    private static Game FinishedGame(int id = 1, PlayerPosition position = PlayerPosition.CM)
    {
        var game = TestData.Game(id: id, durationMinutes: 60);
        game.MatchState = MatchState.Finished;
        game.ScoreHome = 1;
        game.ScoreAway = 0;
        game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, position, 5));
        game.AddPeriod(PeriodType.SecondHalf, TestData.Starter(1, position, 5));
        return game;
    }

    [Fact]
    public void A_game_still_being_played_contributes_nothing()
    {
        var game = FinishedGame();
        game.MatchState = MatchState.InProgress;
        game.Goals.Add(TestData.Goal(scorerId: 1));

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(0, stats.TotalMinutes);
        Assert.Equal(0, stats.Goals);
        Assert.Equal(0, stats.GamesPlayed);
        Assert.Equal(0, stats.AvailableMinutes);
    }

    [Fact]
    public void Minutes_positions_and_goals_aggregate_across_games()
    {
        var first = FinishedGame(1, PlayerPosition.CM);
        first.Goals.Add(TestData.Goal(1, scorerId: 1));
        first.Goals.Add(TestData.Goal(1, scorerId: 2, assisterId: 1));

        var second = FinishedGame(2, PlayerPosition.ST);
        second.Goals.Add(TestData.Goal(2, scorerId: 1));

        var stats = PlayerStatsReport.Build(Subject, [first, second], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(2, stats.GamesPlayed);
        Assert.Equal(120, stats.TotalMinutes);
        Assert.Equal(2, stats.Goals);
        Assert.Equal(1, stats.Assists);
        Assert.Equal(3, stats.GoalContributions);

        // Half the minutes in each position, so a 50/50 split.
        Assert.Equal(2, stats.Positions.Count);
        Assert.All(stats.Positions, p => Assert.Equal(50, p.Percentage));
        Assert.All(stats.Positions, p => Assert.Equal(60, p.Minutes));
    }

    [Fact]
    public void An_own_goal_does_not_count_towards_the_scorers_tally()
    {
        var game = FinishedGame();
        game.Goals.Add(TestData.Goal(scorerId: 1, ownGoal: true));

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(0, stats.Goals);
    }

    [Fact]
    public void Goalkeeper_minutes_are_tracked_separately()
    {
        var stats = PlayerStatsReport.Build(
            Subject, [FinishedGame(1, PlayerPosition.GK)], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(60, stats.GoalkeeperMinutes);
        Assert.Equal(60, stats.TotalMinutes);
    }

    [Fact]
    public void Unavailable_games_do_not_dilute_utilisation()
    {
        var played = FinishedGame(1);

        // Same fixture, but the player opted out — it must not appear in the denominator.
        var missed = FinishedGame(2);
        missed.Periods[0].PlayerPositions.Clear();
        missed.Periods[1].PlayerPositions.Clear();
        missed.AddPeriod(PeriodType.FirstQuarter, TestData.Starter(2, PlayerPosition.CM, 5));
        missed.UnavailablePlayerIds.Add(Subject.Id);

        var stats = PlayerStatsReport.Build(
            Subject, [played, missed], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(60, stats.AvailableMinutes);
        Assert.Equal(60, stats.TotalMinutes);
        Assert.Equal(100, stats.Utilization);
    }

    [Fact]
    public void Sitting_the_whole_bench_still_counts_as_available()
    {
        var game = FinishedGame();
        game.Periods[0].PlayerPositions[0] = TestData.Sub(1);
        game.Periods[1].PlayerPositions[0] = TestData.Sub(1);
        // Someone has to be on the pitch for the game to have a lineup at all.
        game.Periods[0].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.CM, 5));
        game.Periods[1].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.CM, 5));

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(60, stats.AvailableMinutes);
        Assert.Equal(0, stats.TotalMinutes);
        Assert.Equal(0, stats.Utilization);
        Assert.Equal(0, stats.GamesPlayed);
    }

    [Fact]
    public void Utilisation_cannot_exceed_100_percent_on_a_match_that_over_ran()
    {
        var game = FinishedGame();
        game.Periods[0].StartedAtSeconds = 0;
        game.Periods[0].EndedAtSeconds = 2100;      // 35 min
        game.Periods[1].StartedAtSeconds = 2100;
        game.Periods[1].EndedAtSeconds = 4200;      // 35 min

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        // Both numerator and denominator come from the real timings, so it lands on exactly 100.
        Assert.Equal(70, stats.TotalMinutes);
        Assert.Equal(70, stats.AvailableMinutes);
        Assert.Equal(100, stats.Utilization);
    }

    [Fact]
    public void Per_game_rows_are_flagged_as_estimated_or_actual()
    {
        var estimated = FinishedGame(1);
        var actual = FinishedGame(2);
        actual.Periods[0].StartedAtSeconds = 0;
        actual.Periods[0].EndedAtSeconds = 1800;
        actual.Periods[1].StartedAtSeconds = 1800;
        actual.Periods[1].EndedAtSeconds = 3600;

        var stats = PlayerStatsReport.Build(
            Subject, [estimated, actual], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.False(stats.Games.Single(g => g.Game.Id == 1).IsActual);
        Assert.True(stats.Games.Single(g => g.Game.Id == 2).IsActual);
    }

    [Fact]
    public void A_game_the_player_took_no_part_in_is_left_out_of_the_breakdown()
    {
        var game = FinishedGame();
        game.Periods[0].PlayerPositions.Clear();
        game.Periods[1].PlayerPositions.Clear();
        game.AddPeriod(PeriodType.FirstQuarter, TestData.Starter(2, PlayerPosition.CM, 5));

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Empty(stats.Games);
    }

    [Fact]
    public void Rates_are_zero_rather_than_undefined_with_no_games()
    {
        var stats = PlayerStatsReport.Build(Subject, [], SeasonSquads.Empty);

        Assert.Equal(0, stats.AverageMinutes);
        Assert.Equal(0, stats.GoalsPerGame);
        Assert.Equal(0, stats.Utilization);
        Assert.Empty(stats.Positions);
    }

    [Fact]
    public void Rounding_happens_once_at_the_end_not_per_game()
    {
        // Three games of 29:50 each. Rounding per game gives 3 × 30 = 90; rounding the total
        // (5370s) gives 90 too — but the position share must come from the exact seconds.
        var games = Enumerable.Range(1, 3).Select(i =>
        {
            var game = TestData.Game(id: i, durationMinutes: 60);
            game.MatchState = MatchState.Finished;
            var period = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
            period.StartedAtSeconds = 0;
            period.EndedAtSeconds = 1790;
            return game;
        }).ToList();

        var stats = PlayerStatsReport.Build(Subject, games, SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(90, stats.TotalMinutes);
        Assert.Equal(100, stats.Positions.Single().Percentage);
    }
}
