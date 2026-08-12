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

        var changes = PlannedChangesReport.Build(q1, q2, Find, []);

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

        var changes = PlannedChangesReport.Build(q3, q4, Find, []);

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

        var changes = PlannedChangesReport.Build(q1, q2, Find, []);

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

        Assert.True(PlannedChangesReport.Build(q1, q2, Find, []).IsEmpty);
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

        var changes = PlannedChangesReport.Build(q1, q2, Find, []);

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

        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find, []).Substitutions);
        Assert.Equal((1, 2), (swap.PlayerOff!.Id, swap.PlayerOn!.Id));
    }

    /// <summary>
    /// Play overtakes the plan. The line-up still differs from the next one, so the difference
    /// still names the slot — but it now proposes to withdraw the player who came on for the one
    /// the plan meant to take off, which is a substitution nobody planned.
    /// </summary>
    [Fact]
    public void A_swap_whose_outgoing_player_has_already_been_taken_off_drops_out()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        // As the pitch stands after 2 went off for 4 — the lineup records where everyone is now.
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(4, PlayerPosition.CM, 5),
            TestData.Sub(2));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5));
        var live = TestData.Substitution(game, q1, offId: 2, onId: 4, atSeconds: 300, PlayerPosition.CM, slot: 5);

        // Without the substitution the difference reads as a swap of the player who just came on.
        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find, []).Substitutions);
        Assert.Equal((4, 3), (swap.PlayerOff!.Id, swap.PlayerOn!.Id));

        Assert.Empty(PlannedChangesReport.Build(q1, q2, Find, [live]).Substitutions);
    }

    [Fact]
    public void A_swap_is_kept_when_its_outgoing_player_is_still_on_the_pitch()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Starter(4, PlayerPosition.ST, 10),
            TestData.Sub(6));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Starter(4, PlayerPosition.ST, 10));
        var live = TestData.Substitution(game, q1, offId: 6, onId: 4, atSeconds: 300, PlayerPosition.ST, slot: 10);

        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find, [live]).Substitutions);
        Assert.Equal((2, 3), (swap.PlayerOff!.Id, swap.PlayerOn!.Id));
    }

    /// <summary>
    /// The rewind has to unwind the substitutions newest first. Taken the other way round, a player
    /// who left and returned reads as somebody who was never in the starting line-up, and the swap
    /// the plan still holds for them disappears.
    /// </summary>
    [Fact]
    public void A_player_who_went_off_and_came_back_is_still_the_one_the_plan_takes_off()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(3));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(4, PlayerPosition.CM, 5));
        var off = TestData.Substitution(game, q1, offId: 2, onId: 3, atSeconds: 300, PlayerPosition.CM, slot: 5);
        var back = TestData.Substitution(game, q1, offId: 3, onId: 2, atSeconds: 600, PlayerPosition.CM, slot: 5);

        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find, [off, back]).Substitutions);
        Assert.Equal((2, 4), (swap.PlayerOff!.Id, swap.PlayerOn!.Id));
    }

    /// <summary>An arrival with nobody named to come off is a line-up worth flagging, not hiding.</summary>
    [Fact]
    public void An_arrival_with_nobody_to_come_off_survives_the_viability_check()
    {
        var game = TestData.Game(split: GameSplitType.Quarters);
        var q1 = game.AddPeriod(PeriodType.FirstQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0));
        var q2 = game.AddPeriod(PeriodType.SecondQuarter,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5));

        var swap = Assert.Single(PlannedChangesReport.Build(q1, q2, Find, []).Substitutions);
        Assert.Null(swap.PlayerOff);
        Assert.Equal(2, swap.PlayerOn!.Id);
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
