using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The slot swap: who is on the pitch afterwards, what was written down about it, and what can be
/// taken back. Against real SQLite, because the lineup row and the substitution row go in together.
/// </summary>
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
        Assert.Equal(13, result.Value.Minute);
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
    public async Task A_substitution_needs_a_period_to_be_running()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.EndPeriodAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No period is currently being played", result.Error);
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
        Assert.Equal("Only the most recent substitution of a period can be undone", result.Error);
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
        Assert.Equal("Only the most recent substitution of a period can be undone", refused.Error);

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
}
