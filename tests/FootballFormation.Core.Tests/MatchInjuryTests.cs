namespace FootballFormation.Core.Tests;

/// The injury half of MatchSubstitutionService: what the line-up says afterwards, and what taking it back does.
public class MatchInjuryTests : LiveMatchTestBase
{
    [Fact]
    public async Task Going_off_hurt_with_a_replacement_records_the_injury_and_the_substitution()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var result = await Subs.MarkInjuredAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(720, result.Value!.AtSeconds);
        Assert.Equal(PlayerPosition.CM, result.Value.Position);
        Assert.Equal(5, result.Value.SlotIndex);

        // Both rows, from one call.
        var sub = Assert.Single(await Read().GameSubstitutions.ToListAsync());
        Assert.Equal(players[1].Id, sub.PlayerOffId);
        Assert.Equal(players[2].Id, sub.PlayerOnId);
        Assert.Equal(720, sub.AtSeconds);

        var period = await LivePeriodAsync(game.Id);
        Assert.True(period.PlayerPositions.Single(p => p.PlayerId == players[1].Id).IsSubstitute);
        Assert.Equal(5, period.PlayerPositions.Single(p => p.PlayerId == players[2].Id).SlotIndex);
    }

    [Fact]
    public async Task Going_off_hurt_with_nobody_to_replace_her_leaves_the_slot_empty()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(20));
        var result = await Subs.MarkInjuredAsync(game.Id, players[1].Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1200, result.Value!.AtSeconds);

        // Nobody came on, and a substitution row here would name somebody who did not.
        Assert.Empty(await Read().GameSubstitutions.ToListAsync());

        var period = await LivePeriodAsync(game.Id);
        var off = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        Assert.True(off.IsSubstitute);
        Assert.Null(off.SlotIndex);
        Assert.DoesNotContain(period.PlayerPositions, p => !p.IsSubstitute && p.SlotIndex == 5);
    }

    [Fact]
    public async Task Only_a_player_on_the_pitch_can_go_off_hurt()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // players[2] is on the bench.
        var result = await Subs.MarkInjuredAsync(game.Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("That player is not on the pitch", result.Error);
    }

    [Fact]
    public async Task An_injury_needs_a_half_to_be_running()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.EndHalfAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.MarkInjuredAsync(game.Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No half is being played", result.Error);
    }

    [Fact]
    public async Task A_player_cannot_be_replaced_by_herself()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var result = await Subs.MarkInjuredAsync(game.Id, players[1].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("A player cannot be substituted for themselves", result.Error);
    }

    [Fact]
    public async Task The_same_player_cannot_be_hurt_twice_in_one_match()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Assert.True((await Subs.MarkInjuredAsync(game.Id, players[1].Id)).IsSuccess);

        // Back on the pitch first, so the refusal is about the injury and not about where she is.
        Assert.True((await Subs.SubstituteAsync(game.Id, players[0].Id, players[1].Id)).IsSuccess);
        var again = await Subs.MarkInjuredAsync(game.Id, players[1].Id);

        Assert.True(again.IsFailure);
        Assert.Equal("That player is already marked injured", again.Error);
        Assert.Single(await Read().GameInjuries.ToListAsync());
    }

    [Fact]
    public async Task Undoing_an_unreplaced_injury_puts_her_back_in_the_slot_she_left()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(8));
        var injury = await Subs.MarkInjuredAsync(game.Id, players[1].Id);

        var undone = await Subs.RemoveInjuryAsync(injury.Value!.Id);
        Assert.True(undone.IsSuccess);

        var period = await LivePeriodAsync(game.Id);
        var back = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);

        Assert.False(back.IsSubstitute);
        Assert.Equal(5, back.SlotIndex);
        Assert.Equal(PlayerPosition.CM, back.Position);
        Assert.Empty(await Read().GameInjuries.ToListAsync());
    }

    [Fact]
    public async Task Undoing_the_substitution_takes_the_injury_recorded_with_it()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(8));
        Assert.True((await Subs.MarkInjuredAsync(game.Id, players[1].Id, players[2].Id)).IsSuccess);

        var sub = await Read().GameSubstitutions.SingleAsync();
        Assert.True((await Subs.RemoveSubstitutionAsync(sub.Id)).IsSuccess);

        // One tap made the pair, so undoing that tap has to take both.
        Assert.Empty(await Read().GameInjuries.ToListAsync());

        var period = await LivePeriodAsync(game.Id);
        Assert.Equal(5, period.PlayerPositions.Single(p => p.PlayerId == players[1].Id).SlotIndex);
    }

    [Fact]
    public async Task An_injury_is_loaded_with_the_game_details_the_statistics_are_built_from()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(15));
        Assert.True((await Subs.MarkInjuredAsync(game.Id, players[1].Id)).IsSuccess);
        await MatchClock.FinishMatchAsync(game.Id);

        // Without the include, AvailableMinutesFor silently reads an empty list.
        var loaded = (await Games.GetAllWithDetailsAsync()).Value!.Single();

        Assert.Equal(900, Assert.Single(loaded.Injuries).AtSeconds);
        Assert.Equal(15, loaded.AvailableMinutesFor(players[1].Id));
        Assert.Equal(loaded.PlayedDurationMinutes, loaded.AvailableMinutesFor(players[0].Id));
    }

    [Fact]
    public async Task Recording_an_injury_is_an_admin_write()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        CurrentUser.IsAdmin = false;
        var result = await Subs.MarkInjuredAsync(game.Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Empty(await Read().GameInjuries.ToListAsync());
    }

    private async Task<GamePeriod> LivePeriodAsync(int gameId)
    {
        var live = await ReloadAsync(gameId);
        return await Read().GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);
    }
}
