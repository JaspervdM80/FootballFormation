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
    public void A_player_marked_injured_after_playing_keeps_the_available_minutes_of_games_already_played()
    {
        var game = FinishedGame();

        // Injured *now*, on the live squad the report reads — must not rewrite what already
        // happened. IsInRoster is deliberately blind to it for exactly this reason.
        var squad = TestData.Squad(1, [Subject], injuredIds: [Subject.Id]);

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(squad));

        Assert.Equal(60, stats.AvailableMinutes);
        Assert.Equal(60, stats.TotalMinutes);
        Assert.Equal(100, stats.Utilization);
    }

    [Fact]
    public void Being_hurt_during_a_match_ends_the_minutes_it_counts_as_available()
    {
        // Hurt on 20' of the first half, with nobody coming on: 20 of the 20 she could have played.
        var game = TestData.Game(durationMinutes: 60);
        game.MatchState = MatchState.Finished;
        game.ScoreHome = 1;
        game.ScoreAway = 0;

        var first = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Sub(1));
        var second = game.AddPeriod(PeriodType.SecondHalf, TestData.Starter(2, PlayerPosition.GK, 0));

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;
        second.StartedAtSeconds = 1800;
        second.EndedAtSeconds = 3600;
        TestData.Injury(game, first, playerId: 1, atSeconds: 1200, position: PlayerPosition.CM, slot: 5);

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(20, stats.TotalMinutes);
        Assert.Equal(20, stats.AvailableMinutes);
        Assert.Equal(100, stats.Utilization);
    }

    [Fact]
    public void The_four_availability_figures_partition_the_season_maximum()
    {
        // One game played out, one she was left out of, one she was hurt 20' into, one on the bench.
        var played = FinishedGame(1);

        var missed = FinishedGame(2);
        missed.Periods[0].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        missed.Periods[1].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        missed.UnavailablePlayerIds.Add(Subject.Id);

        var hurt = TestData.Game(id: 3, durationMinutes: 60);
        hurt.MatchState = MatchState.Finished;
        var first = hurt.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5), TestData.Starter(2, PlayerPosition.GK, 0));
        var second = hurt.AddPeriod(PeriodType.SecondHalf, TestData.Starter(2, PlayerPosition.GK, 0));
        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;
        second.StartedAtSeconds = 1800;
        second.EndedAtSeconds = 3600;
        TestData.Injury(hurt, first, playerId: 1, atSeconds: 1200, position: PlayerPosition.CM, slot: 5);

        var benched = FinishedGame(4);
        benched.Periods[0].PlayerPositions[0] = TestData.Sub(1);
        benched.Periods[1].PlayerPositions[0] = TestData.Sub(1);
        benched.Periods[0].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.CM, 5));
        benched.Periods[1].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.CM, 5));

        List<Game> games = [played, missed, hurt, benched];
        var stats = PlayerStatsReport.Build(Subject, games, SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(80, stats.TotalMinutes);
        Assert.Equal(60, stats.NotPlayedMinutes);
        Assert.Equal(40, stats.InjuredMinutes);
        Assert.Equal(60, stats.UnavailableMinutes);

        // Four games of an hour, however the hours were spent.
        Assert.Equal(240, stats.MaximumMinutes);
        Assert.Equal(
            stats.MaximumMinutes,
            stats.TotalMinutes + stats.NotPlayedMinutes + stats.InjuredMinutes + stats.UnavailableMinutes);
        Assert.Equal(33, stats.Availability);
    }

    [Fact]
    public void A_match_missed_injured_is_told_apart_from_one_simply_missed()
    {
        var injured = FinishedGame(1);
        injured.Periods[0].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        injured.Periods[1].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        injured.InjuredPlayerIds.Add(Subject.Id);

        var missed = FinishedGame(2);
        missed.Periods[0].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        missed.Periods[1].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.CM, 5);
        missed.UnavailablePlayerIds.Add(Subject.Id);

        var stats = PlayerStatsReport.Build(
            Subject, [injured, missed], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(60, stats.InjuredMinutes);
        Assert.Equal(60, stats.UnavailableMinutes);
        Assert.Equal(0, stats.NotPlayedMinutes);
        Assert.Equal(120, stats.MaximumMinutes);
    }

    [Fact]
    public void The_maximum_is_the_same_figure_for_everybody()
    {
        // The whole point of the availability bars: they are read against each other, so the scale
        // cannot move from row to row.
        var other = TestData.Player(2, "Other", PlayerPosition.GK, shirt: 1);

        var played = FinishedGame(1);
        played.Periods[0].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.GK, 0));
        played.Periods[1].PlayerPositions.Add(TestData.Starter(2, PlayerPosition.GK, 0));

        var missed = FinishedGame(2);
        missed.Periods[0].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.GK, 0);
        missed.Periods[1].PlayerPositions[0] = TestData.Starter(2, PlayerPosition.GK, 0);
        missed.UnavailablePlayerIds.Add(Subject.Id);

        var squads = SeasonSquads.Of(TestData.Squad(1, [Subject, other]));
        List<Game> games = [played, missed];

        Assert.Equal(
            PlayerStatsReport.Build(other, games, squads).MaximumMinutes,
            PlayerStatsReport.Build(Subject, games, squads).MaximumMinutes);
    }

    [Fact]
    public void A_game_nobody_was_picked_for_offers_nobody_any_minutes()
    {
        var game = FinishedGame();
        game.Periods[0].PlayerPositions.Clear();
        game.Periods[1].PlayerPositions.Clear();

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(0, stats.MaximumMinutes);
        Assert.Equal(0, stats.Availability);
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
        // Three games of 29:24 each (1764s, a .4 fraction that rounds *down* on its own). Summing
        // that per-game rounding gives 3 x 29 = 87; the three games together ran 5292s, which
        // rounds to 88 — the figure both the numerator (TotalMinutes) and the denominator
        // (AvailableMinutes) must land on, or a full-match player reads under- or over 100%.
        var games = Enumerable.Range(1, 3).Select(i =>
        {
            var game = TestData.Game(id: i, durationMinutes: 60);
            game.MatchState = MatchState.Finished;
            var period = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
            period.StartedAtSeconds = 0;
            period.EndedAtSeconds = 1764;
            return game;
        }).ToList();

        var stats = PlayerStatsReport.Build(Subject, games, SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(88, stats.TotalMinutes);
        Assert.Equal(88, stats.AvailableMinutes);
        Assert.Equal(100, stats.Utilization);
        Assert.Equal(100, stats.Positions.Single().Percentage);
    }

    [Fact]
    public void Utilisation_across_several_overrunning_games_never_exceeds_100_percent()
    {
        // Two matches that each ran 20s into stoppage time (3620s = 60:20). Rounding each game's
        // AvailableMinutes on its own and summing the results gave 121' / 120' (101%) for a player
        // on the pitch throughout, the exact shape of the reported bug — see
        // docs/known_issues/domain.md.
        var games = Enumerable.Range(1, 2).Select(i =>
        {
            var game = TestData.Game(id: i, durationMinutes: 60);
            game.MatchState = MatchState.Finished;

            var first = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
            first.StartedAtSeconds = 0;
            first.EndedAtSeconds = 1810;

            var second = game.AddPeriod(PeriodType.SecondHalf, TestData.Starter(1, PlayerPosition.CM, 5));
            second.StartedAtSeconds = 1810;
            second.EndedAtSeconds = 3620;

            return game;
        }).ToList();

        var stats = PlayerStatsReport.Build(Subject, games, SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(121, stats.TotalMinutes);
        Assert.Equal(121, stats.AvailableMinutes);
        Assert.Equal(100, stats.Utilization);
    }

    [Fact]
    public void A_full_match_player_is_never_shown_over_100_percent_utilisation()
    {
        // Two halves that ran into stoppage time: 1825s each, 3650s (60:50) total, on a cumulative
        // match clock like MatchClockService writes. Rounding the numerator (61') while truncating
        // the denominator (60') used to read as 102% for a player who was on the pitch the entire
        // match — see Game.SecondsToMinutes.
        var game = TestData.Game(id: 1, durationMinutes: 60);
        game.MatchState = MatchState.Finished;

        var first = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1825;

        var second = game.AddPeriod(PeriodType.SecondHalf, TestData.Starter(1, PlayerPosition.CM, 5));
        second.StartedAtSeconds = 1825;
        second.EndedAtSeconds = 3650;

        var stats = PlayerStatsReport.Build(Subject, [game], SeasonSquads.Of(TestData.Squad(1, [Subject])));

        Assert.Equal(61, stats.TotalMinutes);
        Assert.Equal(61, stats.AvailableMinutes);
        Assert.Equal(100, stats.Utilization);
    }
}
