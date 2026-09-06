namespace FootballFormation.Core.Tests;

/// The slot-by-slot difference two things read the same way: what the coach is about to do at the break, and what actually gets recorded
/// when the next half kicks off. These pin the second reading — only the complete swaps a GameSubstitution can carry survive.
public class LineupDiffTests
{
    private static GamePeriod Lineup(params GamePlayerPosition[] positions) =>
        new() { PlayerPositions = [.. positions] };

    [Fact]
    public void A_straight_swap_is_the_arrival_paired_with_whoever_held_her_slot()
    {
        var before = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(2, PlayerPosition.CM, 5));
        var after = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(3, PlayerPosition.CM, 5));

        var swap = Assert.Single(LineupDiff.Swaps(before, after, new HashSet<int>()));

        Assert.Equal(2, swap.PlayerOffId);
        Assert.Equal(3, swap.PlayerOnId);
        Assert.Equal(5, swap.SlotIndex);
        Assert.Equal(PlayerPosition.CM, swap.Position);
    }

    [Fact]
    public void A_change_touching_an_injured_player_is_left_out()
    {
        var before = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(2, PlayerPosition.CM, 5));
        var after = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(3, PlayerPosition.CM, 5));

        // The one coming on is already hurt: no phantom half-time move for her.
        Assert.Empty(LineupDiff.Swaps(before, after, new HashSet<int> { 3 }));
    }

    [Fact]
    public void An_extra_arrival_with_nobody_leaving_carries_no_substitution()
    {
        var before = Lineup(TestData.Starter(1, PlayerPosition.GK, 0));
        var after = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(2, PlayerPosition.CM, 5));

        Assert.Empty(LineupDiff.Swaps(before, after, new HashSet<int>()));
    }

    [Fact]
    public void A_departure_with_nobody_arriving_carries_no_substitution()
    {
        var before = Lineup(TestData.Starter(1, PlayerPosition.GK, 0), TestData.Starter(2, PlayerPosition.CM, 5));
        var after = Lineup(TestData.Starter(1, PlayerPosition.GK, 0));

        Assert.Empty(LineupDiff.Swaps(before, after, new HashSet<int>()));
    }

    [Fact]
    public void A_player_only_moving_position_is_not_a_substitution()
    {
        var before = Lineup(TestData.Starter(1, PlayerPosition.CM, 5));
        var after = Lineup(TestData.Starter(1, PlayerPosition.CB, 5));

        Assert.Empty(LineupDiff.Swaps(before, after, new HashSet<int>()));
    }
}
