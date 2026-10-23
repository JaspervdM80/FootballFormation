namespace FootballFormation.Core.Tests;

public class PreviousLineupTests : ServiceTestBase
{
    private static Game GameOn(int id, int day, FormationType formation = FormationType.F442,
        GameSplitType split = GameSplitType.Halves)
    {
        var game = TestData.Game(id: id, split: split, date: new DateTime(2026, 3, day));
        game.FormationType = formation;
        foreach (var type in PeriodTypeExtensions.ForSplitType(split)) game.AddPeriod(type);
        return game;
    }

    private static Game Filled(Game game, int firstPlayerId = 1)
    {
        foreach (var period in game.Periods)
        {
            var slots = game.SlotsOf(period);
            period.PlayerPositions = [.. slots.Select((position, slot) => TestData.Starter(firstPlayerId + slot, position, slot))];
        }

        return game;
    }

    [Fact]
    public void A_line_up_with_an_empty_slot_is_not_full()
    {
        var game = Filled(GameOn(1, 1));
        game.Periods[1].PlayerPositions.RemoveAt(4);

        Assert.False(game.HasFullLineup);
        Assert.True(Filled(GameOn(2, 1)).HasFullLineup);
    }

    [Fact]
    public void The_nearest_earlier_full_line_up_is_offered_whether_or_not_that_game_was_played()
    {
        var older = Filled(GameOn(1, 1));
        var nearest = Filled(GameOn(2, 8));
        var halfDone = GameOn(3, 10);
        halfDone.Periods[0].PlayerPositions = [TestData.Starter(1, PlayerPosition.GK, 0)];
        var later = Filled(GameOn(5, 20));
        var target = GameOn(4, 15);

        Assert.Equal(nearest.Id, target.PreviousFullLineup([older, nearest, halfDone, later, target])?.Id);
    }

    [Fact]
    public void A_game_of_another_split_or_size_is_not_offered()
    {
        var quarters = Filled(GameOn(1, 1, split: GameSplitType.Quarters));
        var nineASide = Filled(GameOn(2, 2, FormationType.F332));
        var target = GameOn(3, 15);

        Assert.Null(target.PreviousFullLineup([quarters, nineASide, target]));
    }

    [Fact]
    public void The_first_game_of_the_season_has_nothing_to_copy()
    {
        var target = GameOn(1, 1);
        var later = Filled(GameOn(2, 8));

        Assert.Null(target.PreviousFullLineup([target, later]));
    }

    [Fact]
    public void A_copied_line_up_takes_this_games_slots_and_leaves_out_who_cannot_play()
    {
        var source = Filled(GameOn(1, 1, FormationType.F433));
        var target = GameOn(2, 8, FormationType.F442);
        var available = Enumerable.Range(1, 11).Where(id => id != 5).ToHashSet();

        var copies = target.LineupsCopiedFrom(source, available);

        var slots = FormationSlots.For(FormationType.F442);
        foreach (var period in target.Periods)
        {
            var lineup = copies[period.Id];
            Assert.DoesNotContain(lineup, pp => pp.PlayerId == 5);
            Assert.All(lineup, pp => Assert.Equal(0, pp.Id));
            Assert.All(lineup, pp => Assert.Equal(slots[pp.SlotIndex!.Value], pp.Position));
            Assert.Equal(10, lineup.Count);
        }
    }

    [Fact]
    public async Task The_service_finds_the_previous_full_line_up_in_the_season()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(11);
        var earlier = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, date: new DateTime(2026, 3, 1)))).Value!;
        var target = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, date: new DateTime(2026, 3, 8)))).Value!;

        var slots = FormationSlots.For(FormationType.F442);
        foreach (var period in Read().GamePeriods.Where(p => p.GameId == earlier.Id).ToList())
        {
            await SaveLineupAsync(period.Id,
                [.. slots.Select((position, slot) => TestData.Starter(players[slot].Id, position, slot))]);
        }

        var forTarget = await Lineups.GetPreviousFullLineupAsync(target.Id);
        var forEarlier = await Lineups.GetPreviousFullLineupAsync(earlier.Id);

        Assert.Equal(earlier.Id, forTarget.Value?.Id);
        Assert.NotNull(forTarget.Value!.Periods[0].PlayerPositions[0].Player);
        Assert.True(forEarlier.IsSuccess);
        Assert.Null(forEarlier.Value);
    }

    [Fact]
    public async Task Asking_for_a_game_that_does_not_exist_fails()
    {
        var result = await Lineups.GetPreviousFullLineupAsync(999);

        Assert.True(result.IsFailure);
    }
}
