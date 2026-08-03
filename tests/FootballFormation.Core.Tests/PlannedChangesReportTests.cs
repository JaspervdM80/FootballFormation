using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The live screen never names a quarter — it announces the changes due halfway through the half
/// instead, which is the difference between two planned line-ups. Get this wrong and a coach is
/// told to make a substitution that was never planned.
/// </summary>
public class PlannedChangesReportTests
{
    private static Player? Find(int id) => TestData.Player(id, $"Player {id}");

    [Fact]
    public void A_player_swapped_for_another_is_one_substitution()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(3));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(2));

        var changes = PlannedChangesReport.Build(q1, q2, Find);

        var swap = Assert.Single(changes.Substitutions);
        Assert.Equal(2, swap.PlayerOff!.Id);
        Assert.Equal(3, swap.PlayerOn!.Id);
        Assert.Equal(PlayerPosition.CM, swap.Position);
        Assert.Empty(changes.Moves);
    }

    /// <summary>
    /// The case the split exists for: rewriting a back line touches every slot, but only one
    /// player actually leaves the pitch, and that is what the coach has to act on.
    /// </summary>
    [Fact]
    public void A_reshuffle_around_one_swap_reports_one_substitution_and_the_moves_separately()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q3 = game.AddPeriod(PeriodType.ThirdQuarter,
            TestData.Starter(1, PlayerPosition.LB, 2),
            TestData.Starter(2, PlayerPosition.CB, 3),
            TestData.Starter(3, PlayerPosition.RB, 4));
        var q4 = game.AddPeriod(PeriodType.FourthQuarter,
            TestData.Starter(3, PlayerPosition.LB, 2),
            TestData.Starter(1, PlayerPosition.CB, 3),
            TestData.Starter(4, PlayerPosition.RB, 4));

        var changes = PlannedChangesReport.Build(q3, q4, Find);

        var swap = Assert.Single(changes.Substitutions);
        Assert.Equal(2, swap.PlayerOff!.Id);
        Assert.Equal(4, swap.PlayerOn!.Id);

        Assert.Collection(changes.Moves,
            m =>
            {
                Assert.Equal(3, m.Player.Id);
                Assert.Equal(PlayerPosition.RB, m.From);
                Assert.Equal(PlayerPosition.LB, m.To);
            },
            m =>
            {
                Assert.Equal(1, m.Player.Id);
                Assert.Equal(PlayerPosition.LB, m.From);
                Assert.Equal(PlayerPosition.CB, m.To);
            });
    }

    [Fact]
    public void An_arrival_is_paired_with_whoever_held_the_slot_they_take()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.CB, 3),
            TestData.Starter(2, PlayerPosition.ST, 10));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(3, PlayerPosition.CB, 3),
            TestData.Starter(4, PlayerPosition.ST, 10));

        var changes = PlannedChangesReport.Build(q1, q2, Find);

        Assert.Collection(changes.Substitutions,
            cb => Assert.Equal((1, 3), (cb.PlayerOff!.Id, cb.PlayerOn!.Id)),
            st => Assert.Equal((2, 4), (st.PlayerOff!.Id, st.PlayerOn!.Id)));
    }

    [Fact]
    public void An_unchanged_lineup_produces_nothing()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5),
            // Who sits on the bench is not a change to the pitch.
            TestData.Sub(3));

        Assert.True(PlannedChangesReport.Build(q1, q2, Find).IsEmpty);
    }

    [Fact]
    public void Line_ups_that_do_not_balance_still_name_everyone_they_concern()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(2, PlayerPosition.LB, 2));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(3, PlayerPosition.LB, 2));

        var changes = PlannedChangesReport.Build(q1, q2, Find);

        Assert.Collection(changes.Substitutions,
            swap => Assert.Equal((2, 3), (swap.PlayerOff!.Id, swap.PlayerOn!.Id)),
            leftOver =>
            {
                Assert.Equal(1, leftOver.PlayerOff!.Id);
                Assert.Null(leftOver.PlayerOn);
            });
    }

    [Fact]
    public void Someone_coming_off_the_bench_counts_as_arriving()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Sub(2));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(1));

        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find).Substitutions);
        Assert.Equal((1, 2), (swap.PlayerOff!.Id, swap.PlayerOn!.Id));
    }

    [Theory]
    [InlineData(PeriodType.FirstHalf, PeriodType.FirstHalf)]
    [InlineData(PeriodType.FirstQuarter, PeriodType.FirstHalf)]
    [InlineData(PeriodType.SecondQuarter, PeriodType.FirstHalf)]
    [InlineData(PeriodType.SecondHalf, PeriodType.SecondHalf)]
    [InlineData(PeriodType.ThirdQuarter, PeriodType.SecondHalf)]
    [InlineData(PeriodType.FourthQuarter, PeriodType.SecondHalf)]
    public void Every_period_belongs_to_one_of_the_two_halves(PeriodType period, PeriodType expected)
    {
        Assert.Equal(expected, period.Half());
        Assert.Equal(expected.DisplayName(), period.HalfDisplayName());
    }
}
