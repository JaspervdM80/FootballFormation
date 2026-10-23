namespace FootballFormation.Core.Tests;

// The builder's drag and tap gestures, without a browser: each one has to leave exactly one entry per player, in the slot the pitch
// will draw her in.
public class LineupEditorTests
{
    private static readonly PlayerPosition[] Slots = FormationSlots.For(FormationType.F442);

    private static readonly Player Keeper = TestData.Player(1, "Keeper", PlayerPosition.GK);
    private static readonly Player Back = TestData.Player(2, "Back", PlayerPosition.CB);
    private static readonly Player Bench = TestData.Player(3, "Bench", PlayerPosition.ST);
    private static readonly Player Listed = TestData.Player(4, "Listed", PlayerPosition.RM);

    private static List<GamePlayerPosition> Lineup() =>
    [
        LineupEditor.Starter(Keeper, PlayerPosition.GK, 0),
        LineupEditor.Starter(Back, PlayerPosition.CB, 2),
        LineupEditor.Substitute(Bench)
    ];

    [Fact]
    public void Moving_a_starter_onto_a_taken_slot_trades_the_two_places()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).MoveOnPitch(fromSlot: 2, toSlot: 0);

        var keeper = lineup.Single(e => e.PlayerId == Keeper.Id);
        var back = lineup.Single(e => e.PlayerId == Back.Id);
        Assert.Equal((2, PlayerPosition.CB), (keeper.SlotIndex, keeper.Position));
        Assert.Equal((0, PlayerPosition.GK), (back.SlotIndex, back.Position));
    }

    [Fact]
    public void Moving_a_starter_onto_an_empty_slot_takes_that_slots_position()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).MoveOnPitch(fromSlot: 2, toSlot: 9);

        var back = lineup.Single(e => e.PlayerId == Back.Id);
        Assert.Equal((9, PlayerPosition.ST), (back.SlotIndex, back.Position));
        Assert.Equal(3, lineup.Count);
    }

    [Fact]
    public void Moving_from_an_empty_slot_changes_nothing()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).MoveOnPitch(fromSlot: 5, toSlot: 0);

        Assert.Equal(0, lineup.Single(e => e.PlayerId == Keeper.Id).SlotIndex);
    }

    [Fact]
    public void A_sub_brought_onto_a_taken_slot_sends_its_occupant_to_the_bench()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).PutOnPitch(Bench, slot: 2, fromBench: true);

        var bench = lineup.Single(e => e.PlayerId == Bench.Id);
        var back = lineup.Single(e => e.PlayerId == Back.Id);
        Assert.Equal((false, 2, PlayerPosition.CB), (bench.IsSubstitute, bench.SlotIndex, bench.Position));
        Assert.Equal((true, (int?)null), (back.IsSubstitute, back.SlotIndex));
    }

    [Fact]
    public void A_player_dropped_from_the_squad_list_onto_a_taken_slot_replaces_its_occupant_outright()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).PutOnPitch(Listed, slot: 2, fromBench: false);

        Assert.DoesNotContain(lineup, e => e.PlayerId == Back.Id);
        Assert.Equal(2, lineup.Single(e => e.PlayerId == Listed.Id).SlotIndex);
    }

    [Fact]
    public void A_starter_put_on_the_pitch_again_ends_up_once_in_the_new_slot()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).PutOnPitch(Back, slot: 9, fromBench: false);

        Assert.Equal(9, lineup.Single(e => e.PlayerId == Back.Id).SlotIndex);
        Assert.Equal(3, lineup.Count);
    }

    [Fact]
    public void A_starter_sent_to_the_bench_comes_back_with_her_own_position()
    {
        var lineup = Lineup();

        new LineupEditor(lineup, Slots).PutOnBench(Keeper);

        var keeper = lineup.Single(e => e.PlayerId == Keeper.Id);
        Assert.Equal((true, (int?)null, PlayerPosition.GK), (keeper.IsSubstitute, keeper.SlotIndex, keeper.Position));
    }

    [Fact]
    public void A_starter_dropped_on_a_sub_trades_places_with_her()
    {
        var lineup = Lineup();

        Assert.True(new LineupEditor(lineup, Slots).SwapWithSub(Back.Id, starterSlot: 2, Bench.Id));

        var bench = lineup.Single(e => e.PlayerId == Bench.Id);
        var back = lineup.Single(e => e.PlayerId == Back.Id);
        Assert.Equal((false, 2, PlayerPosition.CB), (bench.IsSubstitute, bench.SlotIndex, bench.Position));
        Assert.Equal((true, PlayerPosition.CB), (back.IsSubstitute, back.Position));
    }

    [Fact]
    public void A_swap_with_someone_who_is_not_on_the_bench_is_refused_and_changes_nothing()
    {
        var lineup = Lineup();

        Assert.False(new LineupEditor(lineup, Slots).SwapWithSub(Back.Id, starterSlot: 2, Keeper.Id));

        Assert.False(lineup.Single(e => e.PlayerId == Back.Id).IsSubstitute);
        Assert.False(lineup.Single(e => e.PlayerId == Keeper.Id).IsSubstitute);
    }

    [Fact]
    public void Benching_a_slot_benches_whoever_stands_in_it_and_an_empty_slot_is_left_alone()
    {
        var lineup = Lineup();
        var editor = new LineupEditor(lineup, Slots);

        editor.BenchAt(2);
        editor.BenchAt(5);

        Assert.True(lineup.Single(e => e.PlayerId == Back.Id).IsSubstitute);
        Assert.False(lineup.Single(e => e.PlayerId == Keeper.Id).IsSubstitute);
    }

    [Fact]
    public void A_copy_is_fresh_rows_that_editing_the_original_does_not_reach()
    {
        var lineup = Lineup();
        lineup[0].Id = 41;

        var copy = LineupEditor.Copy(lineup);
        new LineupEditor(lineup, Slots).MoveOnPitch(fromSlot: 2, toSlot: 9);

        // Id 0, or saving the copy would try to insert the source's primary key a second time.
        Assert.All(copy, entry => Assert.Equal(0, entry.Id));
        Assert.Equal(2, copy.Single(e => e.PlayerId == Back.Id).SlotIndex);
    }
}
