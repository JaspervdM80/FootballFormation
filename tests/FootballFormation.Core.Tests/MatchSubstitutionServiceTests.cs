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
    public async Task Only_the_most_recent_substitution_of_a_period_can_be_undone()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Time.Advance(TimeSpan.FromMinutes(10));
        await Subs.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        // Reversing the earlier swap would fight every change made on that slot since.
        var result = await Subs.RemoveSubstitutionAsync(first.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Only the most recent substitution of a half can be undone", result.Error);
    }

    [Fact]
    public async Task Of_two_substitutions_in_the_same_second_only_the_later_one_can_be_undone()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // A double substitution: two taps on the touchline, one second on the match clock.
        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);
        var second = await Subs.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        Assert.Equal(first.Value!.AtSeconds, second.Value!.AtSeconds);

        var refused = await Subs.RemoveSubstitutionAsync(first.Value.Id);
        Assert.True(refused.IsFailure);
        Assert.Equal("Only the most recent substitution of a half can be undone", refused.Error);

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
}
