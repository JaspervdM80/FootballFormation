namespace FootballFormation.Core.Tests;

public class HalfLineupWalkTests
{
    [Fact]
    public void A_swap_splits_the_half_at_the_second_it_was_made()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(2, PlayerPosition.GK, 0));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Swap(game, period, 1, 2, atSeconds: 600);

        var walk = HalfLineupWalk.Rewind(game, period).Walk(0, 1800);

        var before = HalfLineupWalk.At(walk, 599);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), before.OnPitch[1]);
        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), before.OnPitch[2]);

        // A change at the very second asked for has already happened.
        var from = HalfLineupWalk.At(walk, 600);
        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), from.OnPitch[1]);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), from.OnPitch[2]);
    }

    [Fact]
    public void A_substitute_takes_the_spot_of_whoever_went_off_as_she_stood_then()
    {
        // 1 and 2 swap at 5', so 2 is in goal when 3 replaces her at 15'.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(3, PlayerPosition.GK, 0),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Swap(game, period, 1, 2, atSeconds: 300);
        TestData.Substitution(game, period, offId: 2, onId: 3, atSeconds: 900, position: PlayerPosition.GK, slot: 0);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 300, [PlayerPosition.CM] = 1500 }, minutes.PositionsFor(1));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.CM] = 300, [PlayerPosition.GK] = 600 }, minutes.PositionsFor(2));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 900 }, minutes.PositionsFor(3));
    }

    [Fact]
    public void A_swap_added_before_a_substitution_hands_the_substitute_the_swapped_spot()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        var sub = TestData.Substitution(game, period, offId: 2, onId: 3, atSeconds: 900, position: PlayerPosition.CM, slot: 5);

        var swap = new LineupChange(LineupChangeKind.Swap, 300, DateTime.UnixEpoch, 99, 1, 2);
        var walk = HalfLineupWalk.Rewind(game, period).With(added: swap).Walk(0, 1800);

        Assert.Empty(walk.Skipped);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), walk.SpotOf[(LineupChangeKind.Substitution, sub.Id)]);
        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), walk.AtWhistle[1]);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), walk.AtWhistle[3]);
    }

    [Fact]
    public void A_swap_and_a_substitution_in_the_same_second_are_walked_in_the_order_they_were_recorded()
    {
        // Both ids are 1 — they come from different tables — so only RecordedAt can put the swap first.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(3, PlayerPosition.GK, 0),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Swap(game, period, 1, 2, atSeconds: 600).RecordedAt = DateTime.UnixEpoch;
        var sub = TestData.Substitution(game, period, offId: 2, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        sub.RecordedAt = DateTime.UnixEpoch.AddSeconds(1);

        var walk = HalfLineupWalk.Rewind(game, period).Walk(0, 1800);

        Assert.Empty(walk.Skipped);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), walk.SpotOf[(LineupChangeKind.Substitution, sub.Id)]);
        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), HalfLineupWalk.At(walk, 599).OnPitch[2]);
    }

    [Fact]
    public void A_player_moved_within_a_minute_of_coming_on_is_credited_nothing_in_the_spot_she_came_on_in()
    {
        // The keeper (1) goes off for 3 at 10', and 3 trades places with 2 twenty seconds later: 2 is the one put in goal.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(1));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        TestData.Swap(game, period, 3, 2, atSeconds: 620);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.CM] = 1200 }, minutes.PositionsFor(3));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.CM] = 600, [PlayerPosition.GK] = 1200 }, minutes.PositionsFor(2));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 600 }, minutes.PositionsFor(1));
    }

    [Fact]
    public void A_swap_a_minute_or_more_after_she_came_on_still_counts_from_its_own_second()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(1));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        TestData.Swap(game, period, 3, 2, atSeconds: 660);

        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 60, [PlayerPosition.CM] = 1140 },
            GameMinutesReport.Build(game).PositionsFor(3));
    }

    [Fact]
    public void A_swap_is_left_at_its_own_second_when_something_else_moved_her_after_she_came_on()
    {
        // 3 comes on for the keeper, swaps with 2, then with 4 — all inside the minute. Only the first swap follows the arrival.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Starter(4, PlayerPosition.ST, 9),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(1));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        TestData.Swap(game, period, 3, 2, atSeconds: 610);
        TestData.Swap(game, period, 3, 4, atSeconds: 630);

        var walk = HalfLineupWalk.Rewind(game, period).Walk(0, 1800);

        Assert.Empty(walk.Skipped);
        Assert.Equal(new[] { 600, 600, 630 }, HalfLineupWalk.Rewind(game, period).Changes.Select(c => c.AtSeconds).ToArray());
        Assert.Equal(new PitchSpot(9, PlayerPosition.ST), HalfLineupWalk.At(walk, 620).OnPitch[3]);
    }

    [Fact]
    public void A_swap_added_inside_the_minute_after_an_arrival_settles_the_way_the_stored_rows_will()
    {
        // 3 comes on for the keeper at 10' and swaps with 2 at 10:30, which settles to 10'. A swap with 4 is then added at 10:10.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Starter(4, PlayerPosition.ST, 9),
            TestData.Sub(1));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        TestData.Swap(game, period, 3, 2, atSeconds: 630);

        var added = new LineupChange(LineupChangeKind.Swap, 610, DateTime.UnixEpoch.AddDays(1), 99, 3, 4);
        var corrected = HalfLineupWalk.Rewind(game, period).With(added: added);

        // What the next read of the stored rows will make of the half.
        TestData.Swap(game, period, 3, 4, atSeconds: 610).RecordedAt = added.RecordedAt;
        var stored = HalfLineupWalk.Rewind(game, period);

        Assert.Equal(stored.Changes.Select(c => (c.Kind, c.AtSeconds, c.PlayerId, c.OtherPlayerId)),
            corrected.Changes.Select(c => (c.Kind, c.AtSeconds, c.PlayerId, c.OtherPlayerId)));
    }

    [Fact]
    public void A_settled_swap_follows_its_arrival_whatever_the_ids_of_the_rows_around_it()
    {
        // Rows written without a RecordedAt: the swap's id is lower than its arrival's, so only its place in the list keeps it after her.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Starter(5, PlayerPosition.ST, 9),
            TestData.Sub(1),
            TestData.Sub(4));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 4, onId: 5, atSeconds: 300, position: PlayerPosition.ST, slot: 9);
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.GK, slot: 0);
        TestData.Swap(game, period, 3, 2, atSeconds: 620);

        var walk = HalfLineupWalk.Rewind(game, period).Walk(0, 1800);

        Assert.Empty(walk.Skipped);
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.CM] = 1200 }, GameMinutesReport.Build(game).PositionsFor(3));
    }

    [Fact]
    public void Leaving_a_swap_out_walks_the_half_as_if_it_never_happened()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(2, PlayerPosition.GK, 0));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Swap(game, period, 1, 2, atSeconds: 600);

        var rewound = HalfLineupWalk.Rewind(game, period);
        var walk = rewound.With(removed: rewound.Changes.Single()).Walk(0, 1800);

        Assert.Single(walk.Stretches);
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), walk.AtWhistle[1]);
        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), walk.AtWhistle[2]);
    }

    [Fact]
    public void A_swap_with_a_player_who_is_not_on_is_skipped_rather_than_walked()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 2, onId: 3, atSeconds: 900, position: PlayerPosition.CM, slot: 5);

        // 3 only comes on at 15', so she cannot have traded places with the keeper at 5'.
        var swap = new LineupChange(LineupChangeKind.Swap, 300, DateTime.UnixEpoch, 99, 1, 3);
        var walk = HalfLineupWalk.Rewind(game, period).With(added: swap).Walk(0, 1800);

        Assert.Equal(swap, Assert.Single(walk.Skipped));
        Assert.Equal(new PitchSpot(0, PlayerPosition.GK), walk.AtWhistle[1]);
    }

    [Fact]
    public void An_unreplaced_injury_takes_her_off_from_the_spot_she_held()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        var injury = TestData.Injury(game, period, playerId: 2, atSeconds: 600, position: PlayerPosition.CM, slot: 5);

        var walk = HalfLineupWalk.Rewind(game, period).Walk(0, 1800);

        Assert.Equal(new PitchSpot(5, PlayerPosition.CM), walk.SpotOf[(LineupChangeKind.Injury, injury.Id)]);
        Assert.False(walk.AtWhistle.ContainsKey(2));
        Assert.True(HalfLineupWalk.At(walk, 599).OnPitch.ContainsKey(2));
    }

    [Fact]
    public void A_swap_recorded_before_swaps_had_a_minute_credits_the_spot_moved_into_for_the_whole_half()
    {
        // Older matches: 3 came on for 2 in slot 5, then traded places with the keeper with nothing written down. The final line-up is all
        // there is, so 2 is rewound into the spot 3 ended in — the same rule undoing the substitution follows.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5),
            TestData.Starter(3, PlayerPosition.GK, 0),
            TestData.Sub(2));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 2, onId: 3, atSeconds: 600, position: PlayerPosition.CM, slot: 5);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 600 }, minutes.PositionsFor(2));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.GK] = 1200 }, minutes.PositionsFor(3));
        Assert.Equal(new Dictionary<PlayerPosition, int> { [PlayerPosition.CM] = 1800 }, minutes.PositionsFor(1));
    }
}
