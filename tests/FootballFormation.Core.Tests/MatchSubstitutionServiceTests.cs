namespace FootballFormation.Core.Tests;

/// Against real SQLite, because the line-up row and the substitution row have to go in together.
public class MatchSubstitutionServiceTests : LiveMatchTestBase
{
    [Fact]
    public async Task A_substitution_hands_the_slot_and_position_over()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(720, result.Value!.AtSeconds);
        Assert.Equal(PlayerPosition.CM, result.Value.Position);
        Assert.Equal(5, result.Value.SlotIndex);

        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var off = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        var on = period.PlayerPositions.Single(p => p.PlayerId == players[2].Id);

        Assert.True(off.IsSubstitute);
        Assert.Null(off.SlotIndex);
        Assert.False(on.IsSubstitute);
        Assert.Equal(5, on.SlotIndex);
        Assert.Equal(PlayerPosition.CM, on.Position);
    }

    [Fact]
    public async Task A_player_cannot_be_substituted_for_themselves()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("A player cannot be substituted for themselves", result.Error);
    }

    [Fact]
    public async Task Only_a_player_on_the_pitch_can_come_off()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[2] is on the bench.
        var result = await Subs.SubstituteAsync(game.Id, players[2].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("That player is not on the pitch", result.Error);
    }

    [Fact]
    public async Task Someone_who_turned_up_late_can_still_be_brought_on()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[3] is in no lineup at all — refusing the change mid-match is less useful
        // than adding them.
        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[3].Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Nobody_can_be_brought_on_who_is_already_on()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[0] is in goal. Bringing them on for the midfielder would seat them twice.
        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[0].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("That player is already on the pitch", result.Error);
    }

    [Fact]
    public async Task A_substitution_needs_a_period_to_be_running()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.EndHalfAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No half is being played", result.Error);
    }

    [Fact]
    public async Task Two_players_on_the_pitch_trade_their_slots_and_their_positions()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var result = await Subs.SwapPositionsAsync(game.Id, players[0].Id, players[1].Id);

        Assert.True(result.IsSuccess);

        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var keeper = period.PlayerPositions.Single(p => p.PlayerId == players[0].Id);
        var midfielder = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);

        Assert.Equal(5, keeper.SlotIndex);
        Assert.Equal(PlayerPosition.CM, keeper.Position);
        Assert.Equal(0, midfielder.SlotIndex);
        Assert.Equal(PlayerPosition.GK, midfielder.Position);
        Assert.False(keeper.IsSubstitute);
        Assert.False(midfielder.IsSubstitute);
    }

    [Fact]
    public async Task A_position_swap_is_not_a_substitution_and_is_never_written_down_as_one()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Assert.True((await Subs.SwapPositionsAsync(game.Id, players[0].Id, players[1].Id)).IsSuccess);

        // Nobody left the pitch, so nobody's minutes changed — and a row here would say they did.
        Assert.Empty(await Db.GameSubstitutions.ToListAsync());
    }

    [Fact]
    public async Task Only_two_players_who_are_both_on_can_swap_positions()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[2] is on the bench; bringing them on is a substitution, not a swap.
        var result = await Subs.SwapPositionsAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Both players have to be on the pitch to swap positions", result.Error);
    }

    [Fact]
    public async Task A_player_cannot_swap_positions_with_themselves()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.SwapPositionsAsync(game.Id, players[1].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("A player cannot swap positions with themselves", result.Error);
    }

    [Fact]
    public async Task A_position_swap_needs_a_period_to_be_running()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.EndHalfAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.SwapPositionsAsync(game.Id, players[0].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No half is being played", result.Error);
    }

    [Fact]
    public async Task Undoing_the_most_recent_substitution_restores_the_slot()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        var undone = await Subs.RemoveSubstitutionAsync(sub.Value!.Id);
        Assert.True(undone.IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var back = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        var benched = period.PlayerPositions.Single(p => p.PlayerId == players[2].Id);

        Assert.False(back.IsSubstitute);
        Assert.Equal(5, back.SlotIndex);
        Assert.True(benched.IsSubstitute);
        Assert.Null(benched.SlotIndex);
        Assert.Empty(await Db.GameSubstitutions.ToListAsync());
    }

    [Fact]
    public async Task Undoing_a_substitution_follows_the_slot_a_later_position_swap_moved_it_to()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[1] holds slot 5, players[0] slot 0. Bring players[2] on for players[1], then let
        // them swap with the keeper — so the slot the substitution recorded is somebody else's now.
        Time.Advance(TimeSpan.FromMinutes(10));
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Assert.True((await Subs.SwapPositionsAsync(game.Id, players[2].Id, players[0].Id)).IsSuccess);

        Assert.True((await Subs.RemoveSubstitutionAsync(sub.Value!.Id)).IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var starters = period.PlayerPositions.Where(p => !p.IsSubstitute).ToList();

        // Handing back the recorded slot would have put players[1] into slot 5 alongside players[0]
        // and left slot 0 empty. They take over where the player coming off was actually standing.
        Assert.Equal(starters.Count, starters.Select(p => p.SlotIndex).Distinct().Count());
        Assert.Equal(0, starters.Single(p => p.PlayerId == players[1].Id).SlotIndex);
        Assert.Equal(PlayerPosition.GK, starters.Single(p => p.PlayerId == players[1].Id).Position);
        Assert.Equal(5, starters.Single(p => p.PlayerId == players[0].Id).SlotIndex);
        Assert.True(period.PlayerPositions.Single(p => p.PlayerId == players[2].Id).IsSubstitute);
    }

    [Fact]
    public async Task A_substitution_whose_replacement_was_taken_off_again_cannot_be_undone_yet()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[2] comes on for players[1], then is taken off herself for players[3]: undoing the
        // first now would collide with the second, which built on the slot she left.
        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Time.Advance(TimeSpan.FromMinutes(10));
        await Subs.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        var result = await Subs.RemoveSubstitutionAsync(first.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Undo the later substitution first", result.Error);
    }

    [Fact]
    public async Task An_older_substitution_on_another_slot_can_still_be_undone()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // Two substitutions on different slots: players[2] on for the midfielder, players[3] on for
        // the keeper. The first is no longer the newest, but its replacement is still on the pitch.
        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Time.Advance(TimeSpan.FromMinutes(10));
        await Subs.SubstituteAsync(game.Id, players[0].Id, players[3].Id);

        Assert.True((await Subs.RemoveSubstitutionAsync(first.Value!.Id)).IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var back = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        Assert.False(back.IsSubstitute);
        Assert.Equal(5, back.SlotIndex);
        Assert.True(period.PlayerPositions.Single(p => p.PlayerId == players[2].Id).IsSubstitute);

        // The other slot's substitution is untouched: players[3] is still on for the keeper.
        Assert.False(period.PlayerPositions.Single(p => p.PlayerId == players[3].Id).IsSubstitute);
        Assert.Single(await Db.GameSubstitutions.Where(s => s.GameId == game.Id).ToListAsync());
    }

    [Fact]
    public async Task A_substitution_whose_leaver_came_back_on_cannot_be_undone_yet()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[1] off for players[2], then players[1] back on for the keeper: undoing the first now
        // would move players[1] out of goal and leave it empty, so the later change has to go first.
        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Time.Advance(TimeSpan.FromMinutes(10));
        await Subs.SubstituteAsync(game.Id, players[0].Id, players[1].Id);

        var result = await Subs.RemoveSubstitutionAsync(first.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Undo the later substitution first", result.Error);
    }

    [Fact]
    public async Task Of_two_substitutions_in_the_same_second_the_earlier_waits_on_the_later()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // A double substitution on the same slot: two taps on the touchline, one second on the clock.
        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        var second = await Subs.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        Assert.Equal(first.Value!.AtSeconds, second.Value!.AtSeconds);

        var refused = await Subs.RemoveSubstitutionAsync(first.Value.Id);
        Assert.True(refused.IsFailure);
        Assert.Equal("Undo the later substitution first", refused.Error);

        Assert.True((await Subs.RemoveSubstitutionAsync(second.Value.Id)).IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        // Undoing the earlier one would have left players[2] and players[3] both holding slot 5.
        Assert.Equal([players[2].Id], period.PlayerPositions
            .Where(p => p.SlotIndex == 5)
            .Select(p => p.PlayerId));
    }

    [Fact]
    public async Task Undoing_a_substitution_that_is_not_there_is_refused()
    {
        var result = await Subs.RemoveSubstitutionAsync(999);

        Assert.True(result.IsFailure);
        Assert.Equal("Substitution not found", result.Error);
    }

    [Fact]
    public async Task Undoing_an_injury_recorded_by_another_team_is_refused()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();
        var injury = (await Subs.MarkInjuredAsync(game.Id, players[0].Id)).Value!;

        // FindAsync would fetch the injury regardless of team; the game-in-scope gate is what turns another team's id into "not found".
        SeedTeam("Other Club", "MO17-1");
        var result = await Subs.RemoveInjuryAsync(injury.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Injury not found", result.Error);
    }

    [Fact]
    public async Task A_break_substitution_changes_the_next_half_and_records_nothing_yet()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        var players = await PlayersAsync();

        // players[1] starts the second half, players[2] is on its bench — swap them before it kicks off.
        var result = await Subs.PlanBreakSubstitutionAsync(game.Id, players[1].Id, players[2].Id);
        Assert.True(result.IsSuccess);

        Db.ChangeTracker.Clear();
        var second = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.GameId == game.Id && p.PeriodType == PeriodType.SecondHalf);
        Assert.True(second.PlayerPositions.Single(p => p.PlayerId == players[1].Id).IsSubstitute);
        var on = second.PlayerPositions.Single(p => p.PlayerId == players[2].Id);
        Assert.False(on.IsSubstitute);
        Assert.Equal(5, on.SlotIndex);

        // The change is a plan until the half kicks off — StartNextHalfAsync is what turns it into a substitution.
        Assert.Empty(await Db.GameSubstitutions.Where(s => s.GameId == game.Id).ToListAsync());
    }

    [Fact]
    public async Task A_break_substitution_is_refused_while_a_half_is_being_played()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.PlanBreakSubstitutionAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("End the current half first", result.Error);
    }

    [Fact]
    public async Task A_break_substitution_is_refused_when_no_half_is_left_to_set_up()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        await MatchClock.StartNextHalfAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.PlanBreakSubstitutionAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No half is waiting to be set up", result.Error);
    }

    [Fact]
    public async Task Editing_a_substitution_brings_a_different_player_on()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        // The wrong player came on: it was meant to be players[3], the minute unchanged.
        var edited = await Subs.EditSubstitutionAsync(
            sub.Value!.Id, players[1].Id, players[3].Id, sub.Value.AtSeconds);
        Assert.True(edited.IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var on = period.PlayerPositions.Single(p => p.PlayerId == players[3].Id);
        Assert.False(on.IsSubstitute);
        Assert.Equal(5, on.SlotIndex);
        Assert.True(period.PlayerPositions.Single(p => p.PlayerId == players[2].Id).IsSubstitute);
        Assert.True(period.PlayerPositions.Single(p => p.PlayerId == players[1].Id).IsSubstitute);

        var row = await Db.GameSubstitutions.SingleAsync(s => s.GameId == game.Id);
        Assert.Equal(players[1].Id, row.PlayerOffId);
        Assert.Equal(players[3].Id, row.PlayerOnId);
    }

    [Fact]
    public async Task Editing_a_substitution_moves_it_to_a_new_minute()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Assert.Equal(720, sub.Value!.AtSeconds);

        // The clock has to have reached the corrected minute — a change cannot be moved past the play so far.
        Time.Advance(TimeSpan.FromMinutes(13));
        var edited = await Subs.EditSubstitutionAsync(
            sub.Value.Id, players[1].Id, players[2].Id, atSeconds: 1200);
        Assert.True(edited.IsSuccess);

        var row = await Db.GameSubstitutions.SingleAsync(s => s.GameId == game.Id);
        Assert.Equal(1200, row.AtSeconds);
        Assert.Equal(players[2].Id, row.PlayerOnId);
    }

    [Fact]
    public async Task An_edited_minute_is_kept_inside_the_half()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        var players = await PlayersAsync();
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        await MatchClock.EndHalfAsync(game.Id);

        // The first half ended at 1800s; a later reading cannot belong to it.
        var edited = await Subs.EditSubstitutionAsync(
            sub.Value!.Id, players[1].Id, players[2].Id, atSeconds: 5000);
        Assert.True(edited.IsSuccess);

        var row = await Db.GameSubstitutions.SingleAsync(s => s.GameId == game.Id);
        Assert.Equal(1800, row.AtSeconds);
    }

    [Fact]
    public async Task A_substitution_whose_replacement_was_replaced_cannot_be_edited_yet()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        Time.Advance(TimeSpan.FromMinutes(10));
        await Subs.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        var result = await Subs.EditSubstitutionAsync(
            first.Value!.Id, players[1].Id, players[3].Id, first.Value.AtSeconds);

        Assert.True(result.IsFailure);
        Assert.Equal("Undo the later substitution first", result.Error);
    }

    [Fact]
    public async Task A_substitution_made_for_an_injury_cannot_be_edited()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var injury = await Subs.MarkInjuredAsync(game.Id, players[1].Id, players[2].Id);
        var sub = await Db.GameSubstitutions.SingleAsync(s => s.GameId == game.Id);
        Assert.True(injury.IsSuccess);

        var result = await Subs.EditSubstitutionAsync(
            sub.Id, players[1].Id, players[3].Id, sub.AtSeconds);

        Assert.True(result.IsFailure);
        Assert.Equal("A substitution made for an injury can't be edited", result.Error);
    }

    [Fact]
    public async Task Editing_a_substitution_that_is_not_there_is_refused()
    {
        var result = await Subs.EditSubstitutionAsync(999, 1, 2, 600);

        Assert.True(result.IsFailure);
        Assert.Equal("Substitution not found", result.Error);
    }

    [Fact]
    public async Task An_edited_substitution_cannot_send_a_player_on_for_herself()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var sub = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        var result = await Subs.EditSubstitutionAsync(
            sub.Value!.Id, players[1].Id, players[1].Id, sub.Value.AtSeconds);

        Assert.True(result.IsFailure);
        Assert.Equal("A player cannot be substituted for themselves", result.Error);
    }
}
