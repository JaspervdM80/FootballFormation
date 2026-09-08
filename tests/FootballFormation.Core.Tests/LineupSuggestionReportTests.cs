namespace FootballFormation.Core.Tests;

public class LineupSuggestionReportTests
{
    private static readonly PlayerPosition[] BackFour =
        [PlayerPosition.GK, PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.RB];

    private static Dictionary<int, int> Minutes(params (int PlayerId, int Played)[] entries) =>
        entries.ToDictionary(entry => entry.PlayerId, entry => entry.Played);

    [Fact]
    public void Everyone_lands_in_the_position_she_plays_when_the_squad_matches_the_shape()
    {
        Player[] squad =
        [
            TestData.Player(1, "Keeper", PlayerPosition.GK, shirt: 1),
            TestData.Player(2, "Left", PlayerPosition.LB, shirt: 2),
            TestData.Player(3, "Centre", PlayerPosition.CB, shirt: 3),
            TestData.Player(4, "Right", PlayerPosition.RB, shirt: 4)
        ];

        var suggestion = LineupSuggestionReport.Build(BackFour, squad, Minutes());

        Assert.All(suggestion.Starters, s => Assert.Equal(PositionFit.Preferred, s.Fit));
        Assert.All(suggestion.Starters, s => Assert.Equal(BackFour[s.SlotIndex], s.Position));
        Assert.Equal([1, 2, 3, 4], suggestion.Starters.Select(s => s.Player.Id));
        Assert.Empty(suggestion.Substitutes);
    }

    [Fact]
    public void The_player_with_the_fewest_minutes_starts_when_two_fit_the_slot_equally_well()
    {
        Player[] squad =
        [
            TestData.Player(1, "Ever present", PlayerPosition.CM, shirt: 5),
            TestData.Player(2, "Left out", PlayerPosition.CM, shirt: 6)
        ];

        var suggestion = LineupSuggestionReport.Build(
            [PlayerPosition.CM], squad, Minutes((1, 240), (2, 30)));

        Assert.Equal(2, Assert.Single(suggestion.Starters).Player.Id);
        Assert.Equal(1, Assert.Single(suggestion.Substitutes).Id);
    }

    [Fact]
    public void A_keeper_who_has_played_every_week_still_goes_in_goal_ahead_of_a_rested_striker()
    {
        Player[] squad =
        [
            TestData.Player(1, "Keeper", PlayerPosition.GK, shirt: 1),
            TestData.Player(2, "Striker", PlayerPosition.ST, shirt: 9)
        ];

        var suggestion = LineupSuggestionReport.Build(
            [PlayerPosition.GK], squad, Minutes((1, 600), (2, 0)));

        Assert.Equal(1, Assert.Single(suggestion.Starters).Player.Id);
    }

    /// Taking the cheapest pair first puts the keeper at centre-back, where she is a better fit than anyone else is in goal.
    [Fact]
    public void The_only_keeper_is_kept_for_goal_even_where_she_fits_an_outfield_slot_better()
    {
        Player[] squad =
        [
            TestData.Player(1, "Utility", PlayerPosition.CB, shirt: 4, PlayerPosition.GK),
            TestData.Player(2, "Midfielder", PlayerPosition.CM, shirt: 8)
        ];

        var suggestion = LineupSuggestionReport.Build(
            [PlayerPosition.GK, PlayerPosition.CB], squad, Minutes());

        Assert.Equal(1, suggestion.Starters.Single(s => s.SlotIndex == 0).Player.Id);
        Assert.Equal(2, suggestion.Starters.Single(s => s.SlotIndex == 1).Player.Id);
    }

    [Fact]
    public void Everyone_who_did_not_make_the_shape_is_on_the_bench()
    {
        Player[] squad =
        [
            TestData.Player(1, "Keeper", PlayerPosition.GK, shirt: 1),
            TestData.Player(2, "Left", PlayerPosition.LB, shirt: 2),
            TestData.Player(3, "Centre", PlayerPosition.CB, shirt: 3),
            TestData.Player(4, "Right", PlayerPosition.RB, shirt: 4),
            TestData.Player(5, "Spare", PlayerPosition.CM, shirt: 6),
            TestData.Player(6, "Other spare", PlayerPosition.ST, shirt: 7)
        ];

        var suggestion = LineupSuggestionReport.Build(BackFour, squad, Minutes());

        Assert.Equal(4, suggestion.Starters.Count);
        Assert.Equal([5, 6], suggestion.Substitutes.Select(p => p.Id));
    }

    [Fact]
    public void A_squad_too_small_for_the_shape_leaves_the_slots_it_cannot_fill_empty()
    {
        Player[] squad =
        [
            TestData.Player(1, "Keeper", PlayerPosition.GK, shirt: 1),
            TestData.Player(2, "Left", PlayerPosition.LB, shirt: 2)
        ];

        var suggestion = LineupSuggestionReport.Build(BackFour, squad, Minutes());

        Assert.Equal([0, 1], suggestion.Starters.Select(s => s.SlotIndex));
        Assert.Empty(suggestion.Substitutes);
    }

    /// Two runs over the same squad must not hand the coach a different eleven, so equal players are separated by shirt number.
    [Fact]
    public void Players_who_are_alike_in_every_respect_are_ordered_by_shirt_number()
    {
        Player[] squad =
        [
            TestData.Player(1, "Nine", PlayerPosition.CM, shirt: 9),
            TestData.Player(2, "Four", PlayerPosition.CM, shirt: 4)
        ];

        var suggestion = LineupSuggestionReport.Build([PlayerPosition.CM], squad, Minutes());

        Assert.Equal(2, Assert.Single(suggestion.Starters).Player.Id);
    }

    [Fact]
    public void An_empty_squad_suggests_nothing()
    {
        var suggestion = LineupSuggestionReport.Build(BackFour, [], Minutes());

        Assert.Empty(suggestion.Starters);
        Assert.Empty(suggestion.Substitutes);
    }
}
