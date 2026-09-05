namespace FootballFormation.Core.Tests;

/// Line-up and goal rows hang off the player row, so deleting someone used to quietly edit every season she had played. Delete now
/// refuses that, and archiving takes her out of the seasons to come while leaving every row behind her where it was.
public class PlayerServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_player_who_has_never_played_can_still_be_deleted()
    {
        SeedTeam();
        // The case delete is actually for: a name typed wrong a minute ago, with nothing behind it.
        var player = (await Players.CreateAsync(new Player { FirstName = "Typo" })).Value!;

        Assert.True((await Players.DeleteAsync(player.Id)).IsSuccess);
        Assert.Empty(Read().Players);
    }

    [Fact]
    public async Task Deleting_a_player_who_has_played_is_refused_and_their_lineup_survives()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);

        var result = await Players.DeleteAsync(players[0].Id);

        Assert.True(result.IsFailure);
        Assert.Single(Read().Players);
        Assert.Single(Read().GamePlayerPositions);
    }

    [Fact]
    public async Task Deleting_a_scorer_is_refused_even_with_no_lineup_to_their_name()
    {
        // A goal is enough on its own: ScorerId is SetNull rather than cascade, so deleting her blanks the scorer out of a scoreline
        // that still counts — harder to spot than a missing row.
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 5 });

        Assert.True((await Players.DeleteAsync(players[0].Id)).IsFailure);
        Assert.Equal(players[0].Id, Read().GameGoals.Single().ScorerId);
    }

    [Fact]
    public async Task A_players_history_in_an_old_season_is_enough_to_refuse_the_delete()
    {
        // Unlike removal from a squad, which is judged per season, this looks across all of them:
        // the cascade does not care which season the rows belong to, so neither does the guard.
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(1);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: lastSeason.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 0)]);

        Assert.True((await Players.DeleteAsync(players[0].Id)).IsFailure);
    }

    [Fact]
    public async Task Archiving_a_player_who_has_played_keeps_every_row_they_are_in()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.ST, slot: 0)]);
        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 12 });

        Assert.True((await Players.SetArchivedAsync(players[0].Id, true)).IsSuccess);

        // The whole point: the season they played reads exactly as it did before.
        Assert.True(Read().Players.Single().IsArchived);
        Assert.Single(Read().GamePlayerPositions);
        Assert.Single(Read().SeasonSquadMembers.Where(m => m.SeasonId == season.Id));
        Assert.Equal(players[0].Id, Read().GameGoals.Single().ScorerId);
    }

    [Fact]
    public async Task An_archived_player_can_be_brought_back()
    {
        var players = await SeedPlayersAsync(1);

        await Players.SetArchivedAsync(players[0].Id, true);
        Assert.True((await Players.SetArchivedAsync(players[0].Id, false)).IsSuccess);

        Assert.False(Read().Players.Single().IsArchived);
    }

    [Fact]
    public async Task Archiving_someone_who_is_not_on_file_is_refused()
    {
        Assert.True((await Players.SetArchivedAsync(9999, true)).IsFailure);
    }

    [Fact]
    public async Task Archived_players_stay_in_the_lookup_the_pages_resolve_names_against()
    {
        // GetAllAsync is how a match report turns a player id back into a name, so filtering archived players out would blank a scorer
        // out of a game she scored in.
        var players = await SeedPlayersAsync(2);
        await Players.SetArchivedAsync(players[0].Id, true);

        var all = await Players.GetAllAsync();

        Assert.Equal(2, all.Value!.Count);
    }
}
