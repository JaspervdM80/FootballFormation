namespace FootballFormation.Core.Tests;

public class FormationSlotsTests
{
    [Fact]
    public void Slot_zero_is_always_the_keeper_and_there_are_eleven_slots()
    {
        foreach (var formation in Enum.GetValues<FormationType>())
        {
            var slots = FormationSlots.For(formation);

            Assert.Equal(11, slots.Length);
            Assert.Equal(PlayerPosition.GK, slots[0]);
        }
    }

    [Fact]
    public void An_explicit_slot_index_is_honoured_over_the_position()
    {
        var slots = FormationSlots.For(FormationType.F442);

        // Slot 8 is a central midfielder in 4-4-2, but this entry says slot 1 (left back).
        var assignments = FormationSlots.Assign(slots, [
            new GamePlayerPosition { PlayerId = 7, Position = PlayerPosition.CM, SlotIndex = 1 }
        ]);

        Assert.Equal(7, assignments[1]!.PlayerId);
        Assert.Null(assignments[6]);
    }

    [Fact]
    public void Legacy_entries_without_a_slot_fall_back_to_matching_on_position()
    {
        var slots = FormationSlots.For(FormationType.F442);

        var assignments = FormationSlots.Assign(slots, [
            new GamePlayerPosition { PlayerId = 1, Position = PlayerPosition.GK },
            new GamePlayerPosition { PlayerId = 2, Position = PlayerPosition.ST }
        ]);

        Assert.Equal(1, assignments[0]!.PlayerId);
        // The first ST slot in 4-4-2 is index 9.
        Assert.Equal(2, assignments[9]!.PlayerId);
    }

    [Fact]
    public void A_legacy_entry_never_steals_a_slot_an_explicit_one_is_entitled_to()
    {
        var slots = FormationSlots.For(FormationType.F442);

        // Both are strikers; only one names a slot. The explicit claim must win slot 9, pushing
        // the legacy entry to slot 10 — this is why the two passes cannot be collapsed.
        var assignments = FormationSlots.Assign(slots, [
            new GamePlayerPosition { PlayerId = 1, Position = PlayerPosition.ST },
            new GamePlayerPosition { PlayerId = 2, Position = PlayerPosition.ST, SlotIndex = 9 }
        ]);

        Assert.Equal(2, assignments[9]!.PlayerId);
        Assert.Equal(1, assignments[10]!.PlayerId);
    }

    [Fact]
    public void Substitutes_are_never_placed_on_the_pitch()
    {
        var assignments = FormationSlots.Assign(FormationType.F442, [
            new GamePlayerPosition { PlayerId = 1, Position = PlayerPosition.GK, SlotIndex = 0, IsSubstitute = true }
        ]);

        Assert.All(assignments, Assert.Null);
    }

    [Fact]
    public void An_out_of_range_slot_index_is_ignored_rather_than_throwing()
    {
        // A lineup saved under a formation with more slots than the current one.
        var assignments = FormationSlots.Assign(FormationType.F442, [
            new GamePlayerPosition { PlayerId = 1, Position = PlayerPosition.CB, SlotIndex = 50 },
            new GamePlayerPosition { PlayerId = 2, Position = PlayerPosition.CB, SlotIndex = -1 }
        ]);

        // Both fall through to the position pass and take the two centre-back slots.
        Assert.Equal([1, 2], assignments.Where(a => a is not null).Select(a => a!.PlayerId));
    }

    [Fact]
    public void Two_entries_claiming_the_same_slot_do_not_overwrite_each_other()
    {
        var assignments = FormationSlots.Assign(FormationType.F442, [
            new GamePlayerPosition { PlayerId = 1, Position = PlayerPosition.CM, SlotIndex = 5 },
            new GamePlayerPosition { PlayerId = 2, Position = PlayerPosition.CM, SlotIndex = 5 }
        ]);

        Assert.Equal(1, assignments[5]!.PlayerId);
        // The loser falls through to the position pass rather than vanishing.
        Assert.Contains(assignments, a => a?.PlayerId == 2);
    }

    [Fact]
    public void An_empty_lineup_leaves_every_slot_open()
    {
        var assignments = FormationSlots.Assign(FormationType.F433, []);

        Assert.Equal(11, assignments.Length);
        Assert.All(assignments, Assert.Null);
    }

    [Fact]
    public void OrdinalOf_numbers_the_slots_that_share_a_position()
    {
        // 4-4-2: [GK, LB, CB, CB, RB, LM, CM, CM, RM, ST, ST]
        var slots = FormationSlots.For(FormationType.F442);

        Assert.Equal((0, 2), FormationSlots.OrdinalOf(slots, 2));    // first CB of two
        Assert.Equal((1, 2), FormationSlots.OrdinalOf(slots, 3));    // second CB
        Assert.Equal((0, 1), FormationSlots.OrdinalOf(slots, 0));    // the lone keeper
        Assert.Equal((1, 2), FormationSlots.OrdinalOf(slots, 10));   // second striker
    }

    [Fact]
    public void OrdinalOf_counts_three_of_a_kind()
    {
        // 3-5-2 fields three centre-backs.
        var slots = FormationSlots.For(FormationType.F352);

        Assert.Equal((0, 3), FormationSlots.OrdinalOf(slots, 1));
        Assert.Equal((2, 3), FormationSlots.OrdinalOf(slots, 3));
    }
}
