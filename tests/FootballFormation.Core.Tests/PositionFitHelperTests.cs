using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

/// <summary>The five-tier fit that colours every chip on the pitch.</summary>
public class PositionFitHelperTests
{
    [Fact]
    public void An_exact_preferred_match_is_the_top_tier() =>
        Assert.Equal(PositionFit.Preferred,
            PositionFitHelper.GetFit(TestData.Player(1, preferred: PlayerPosition.CM), PlayerPosition.CM));

    [Theory]
    // A broad preference naturally covers its specific slots.
    [InlineData(PlayerPosition.DEF, PlayerPosition.CB)]
    [InlineData(PlayerPosition.DEF, PlayerPosition.LB)]
    [InlineData(PlayerPosition.MID, PlayerPosition.CAM)]
    [InlineData(PlayerPosition.W, PlayerPosition.LW)]
    [InlineData(PlayerPosition.ATT, PlayerPosition.ST)]
    // …and adjacent specific positions cover each other.
    [InlineData(PlayerPosition.CM, PlayerPosition.CDM)]
    [InlineData(PlayerPosition.LM, PlayerPosition.LW)]
    public void A_naturally_covered_slot_is_the_second_tier(PlayerPosition preferred, PlayerPosition slot) =>
        Assert.Equal(PositionFit.NaturalFit,
            PositionFitHelper.GetFit(TestData.Player(1, preferred: preferred), slot));

    [Fact]
    public void An_explicitly_listed_alternative_ranks_below_a_natural_fit()
    {
        var player = TestData.Player(1, preferred: PlayerPosition.ST, alternatives: PlayerPosition.CB);

        Assert.Equal(PositionFit.Alternative, PositionFitHelper.GetFit(player, PlayerPosition.CB));
    }

    [Fact]
    public void A_slot_covered_only_through_an_alternative_is_the_fourth_tier()
    {
        var player = TestData.Player(1, preferred: PlayerPosition.ST, alternatives: PlayerPosition.DEF);

        // DEF is listed, and DEF naturally covers CB — so CB is compatible, not alternative.
        Assert.Equal(PositionFit.Compatible, PositionFitHelper.GetFit(player, PlayerPosition.CB));
    }

    [Fact]
    public void An_unrelated_slot_is_out_of_position()
    {
        var player = TestData.Player(1, preferred: PlayerPosition.ST, alternatives: PlayerPosition.CAM);

        Assert.Equal(PositionFit.OutOfPosition, PositionFitHelper.GetFit(player, PlayerPosition.GK));
    }

    [Fact]
    public void A_keeper_covers_nothing_outfield_and_nobody_covers_the_goal()
    {
        var keeper = TestData.Player(1, preferred: PlayerPosition.GK);
        var outfield = TestData.Player(2, preferred: PlayerPosition.CB);

        Assert.Equal(PositionFit.OutOfPosition, PositionFitHelper.GetFit(keeper, PlayerPosition.CB));
        Assert.Equal(PositionFit.OutOfPosition, PositionFitHelper.GetFit(outfield, PlayerPosition.GK));
        Assert.Equal(PositionFit.Preferred, PositionFitHelper.GetFit(keeper, PlayerPosition.GK));
    }

    [Fact]
    public void Every_position_can_be_rated_against_every_slot_without_throwing()
    {
        var positions = Enum.GetValues<PlayerPosition>();

        foreach (var preferred in positions)
            foreach (var slot in positions)
                PositionFitHelper.GetFit(TestData.Player(1, preferred: preferred), slot);
    }
}
