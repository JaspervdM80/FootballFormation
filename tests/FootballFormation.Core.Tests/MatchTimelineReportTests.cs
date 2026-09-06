namespace FootballFormation.Core.Tests;

/// The one list the live screen and the finished-match result page both read. What it has to get right is the order — elapsed seconds, then
/// the second an event was entered, then its id — the half-time divider between the halves whichever way round it is read, and which
/// injuries ride a substitution's line rather than a row of their own.
public class MatchTimelineReportTests
{
    private static readonly DateTime Wall = new(2026, 3, 14, 14, 0, 0, DateTimeKind.Utc);

    /// Two 30-minute halves, kicked off at 0 and 30:00, so an event's half follows the period it carries.
    private static Game HalvesGame()
    {
        var game = TestData.Game(durationMinutes: 60);
        game.AddPeriod(PeriodType.FirstHalf).StartedAtSeconds = 0;
        game.AddPeriod(PeriodType.SecondHalf).StartedAtSeconds = 30 * 60;
        return game;
    }

    private static GameGoal Goal(Game game, int id, int periodId, int atSeconds, int recordedSecond,
        bool ownGoal = false, bool opponentGoal = false)
    {
        var goal = new GameGoal
        {
            Id = id,
            GameId = game.Id,
            GamePeriodId = periodId,
            AtSeconds = atSeconds,
            IsOwnGoal = ownGoal,
            IsOpponentGoal = opponentGoal,
            RecordedAt = Wall.AddSeconds(recordedSecond)
        };
        game.Goals.Add(goal);
        return goal;
    }

    private static GameSubstitution Sub(Game game, int id, int periodId, int offId, int onId, int atSeconds, int recordedSecond)
    {
        var sub = new GameSubstitution
        {
            Id = id,
            GameId = game.Id,
            GamePeriodId = periodId,
            PlayerOffId = offId,
            PlayerOnId = onId,
            AtSeconds = atSeconds,
            Position = PlayerPosition.CM,
            RecordedAt = Wall.AddSeconds(recordedSecond)
        };
        game.Substitutions.Add(sub);
        return sub;
    }

    private static GameInjury Injury(Game game, int id, int periodId, int playerId, int atSeconds, int recordedSecond)
    {
        var injury = new GameInjury
        {
            Id = id,
            GameId = game.Id,
            GamePeriodId = periodId,
            PlayerId = playerId,
            AtSeconds = atSeconds,
            Position = PlayerPosition.CM,
            RecordedAt = Wall.AddSeconds(recordedSecond)
        };
        game.Injuries.Add(injury);
        return injury;
    }

    [Fact]
    public void Each_goal_carries_the_running_score_it_made_it()
    {
        var game = HalvesGame();
        Goal(game, id: 1, periodId: 1, atSeconds: 300, recordedSecond: 300);
        Goal(game, id: 2, periodId: 2, atSeconds: 2000, recordedSecond: 2000, opponentGoal: true);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);
        var progression = ScoreProgressionReport.Build(game);

        Assert.Equal(progression[1], timeline[0].Score);
        Assert.Equal(progression[2], timeline[1].Score);
        Assert.Equal(new MatchScore(1, 0), timeline[0].Score);
        Assert.Equal(new MatchScore(1, 1), timeline[1].Score);
    }

    [Fact]
    public void Read_kick_off_first_the_break_sits_above_the_first_second_half_row()
    {
        var game = HalvesGame();
        var early = Goal(game, id: 1, periodId: 1, atSeconds: 300, recordedSecond: 300);
        var late = Goal(game, id: 2, periodId: 2, atSeconds: 2000, recordedSecond: 2000);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);

        Assert.Equal(new[] { early.Id, late.Id }, timeline.Select(e => e.Id).ToArray());
        Assert.False(timeline[0].HalfTimeAbove);
        Assert.True(timeline[1].HalfTimeAbove);
    }

    [Fact]
    public void Read_newest_first_the_break_sits_above_the_first_first_half_row()
    {
        var game = HalvesGame();
        var early = Goal(game, id: 1, periodId: 1, atSeconds: 300, recordedSecond: 300);
        var late = Goal(game, id: 2, periodId: 2, atSeconds: 2000, recordedSecond: 2000);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: true);

        Assert.Equal(new[] { late.Id, early.Id }, timeline.Select(e => e.Id).ToArray());
        Assert.False(timeline[0].HalfTimeAbove);
        Assert.True(timeline[1].HalfTimeAbove);
    }

    [Fact]
    public void Newest_first_is_the_exact_reverse_of_kick_off_first()
    {
        var game = HalvesGame();
        Goal(game, id: 1, periodId: 1, atSeconds: 300, recordedSecond: 300);
        Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 900, recordedSecond: 900);
        Injury(game, id: 1, periodId: 2, playerId: 7, atSeconds: 2200, recordedSecond: 2200);
        Goal(game, id: 2, periodId: 2, atSeconds: 2500, recordedSecond: 2500);

        var forward = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);
        var backward = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: true);

        // Compared on the second rather than the id, which is unique only within a kind — a goal and a substitution can share one.
        Assert.Equal(
            forward.Select(e => e.AtSeconds).Reverse().ToArray(),
            backward.Select(e => e.AtSeconds).ToArray());
    }

    [Fact]
    public void A_goal_and_the_substitution_in_the_same_second_order_by_when_they_were_entered()
    {
        var game = HalvesGame();
        Goal(game, id: 1, periodId: 1, atSeconds: 600, recordedSecond: 600);
        Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 600, recordedSecond: 605);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);

        Assert.NotNull(timeline[0].Goal);
        Assert.NotNull(timeline[1].Substitution);
    }

    [Fact]
    public void Two_changes_in_the_same_second_settle_by_id()
    {
        var game = HalvesGame();
        var first = Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 700, recordedSecond: 700);
        var second = Sub(game, id: 2, periodId: 1, offId: 6, onId: 10, atSeconds: 700, recordedSecond: 700);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);

        Assert.Equal(new[] { first.Id, second.Id }, timeline.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Hiding_substitutions_drops_the_rotation_but_keeps_an_injury_nobody_came_on_for()
    {
        var game = HalvesGame();
        Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 600, recordedSecond: 600);
        var injury = Injury(game, id: 1, periodId: 1, playerId: 7, atSeconds: 1200, recordedSecond: 1200);

        Assert.Equal(2, MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false).Count);

        var hidden = MatchTimelineReport.Build(game, includeSubstitutions: false, newestFirst: false);
        var only = Assert.Single(hidden);
        Assert.Equal(injury.Id, only.Id);
        Assert.NotNull(only.Injury);
        Assert.Null(only.Substitution);
    }

    [Fact]
    public void An_injury_someone_came_on_for_rides_its_substitutions_row_not_one_of_its_own()
    {
        var game = HalvesGame();
        Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 1000, recordedSecond: 1000);
        var injury = Injury(game, id: 1, periodId: 1, playerId: 5, atSeconds: 1000, recordedSecond: 1000);

        var timeline = MatchTimelineReport.Build(game, includeSubstitutions: true, newestFirst: false);

        var entry = Assert.Single(timeline);
        Assert.NotNull(entry.Substitution);
        Assert.Equal(injury, entry.Injury);
        Assert.DoesNotContain(timeline, e => e.Substitution is null && e.Injury is not null);
    }

    /// An injury somebody came on for is a substitution, so the toggle that hides the substitutions hides it too — only one nobody came on
    /// for is a record the rotation filter must leave standing.
    [Fact]
    public void With_substitutions_hidden_an_injury_that_caused_one_goes_with_it()
    {
        var game = HalvesGame();
        Sub(game, id: 1, periodId: 1, offId: 5, onId: 9, atSeconds: 1000, recordedSecond: 1000);
        Injury(game, id: 1, periodId: 1, playerId: 5, atSeconds: 1000, recordedSecond: 1000);

        Assert.Empty(MatchTimelineReport.Build(game, includeSubstitutions: false, newestFirst: false));
    }

    [Fact]
    public void A_match_with_nothing_on_the_clock_has_an_empty_timeline()
    {
        Assert.Empty(MatchTimelineReport.Build(HalvesGame(), includeSubstitutions: true, newestFirst: false));
    }
}
