namespace FootballFormation.Core.Tests;

/// The behaviour that made a shared scoped DbContext a hazard: components on a circuit render concurrently, and two queries on one
/// context throw.
public class ServiceLifetimeTests : ServiceTestBase
{
    [Fact]
    public async Task Concurrent_reads_across_services_do_not_throw()
    {
        var season = await SeedSeasonAsync();
        await SeedPlayersAsync(3);

        // Exactly the shape of the old crash: the layout's season picker and a page both querying
        // while neither has finished.
        var results = await Task.WhenAll(
            Seasons.GetAllAsync().ContinueWith(t => (object)t.Result),
            Players.GetAllAsync().ContinueWith(t => (object)t.Result),
            Games.GetAllAsync(season.Id).ContinueWith(t => (object)t.Result),
            Squads.GetSquadAsync(season.Id).ContinueWith(t => (object)t.Result));

        Assert.All(results, r => Assert.True(((Result)r).IsSuccess));
    }

    [Fact]
    public async Task Many_concurrent_reads_of_the_same_service_do_not_throw()
    {
        await SeedSeasonAsync();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Seasons.GetAllAsync()));

        Assert.All(results, r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task A_write_is_visible_to_the_next_read_on_a_different_context()
    {
        // Each operation gets its own context, so nothing is served from a stale change tracker.
        var created = await Players.CreateAsync(new Player { FirstName = "Nieuw", PreferredPosition = PlayerPosition.ST });
        Assert.True(created.IsSuccess);

        var all = await Players.GetAllAsync();

        Assert.Contains(all.Value!, p => p.Id == created.Value!.Id);
    }

    [Fact]
    public async Task A_reload_after_an_update_returns_the_new_values_not_the_cached_ones()
    {
        var created = await Players.CreateAsync(new Player { FirstName = "Oud", PreferredPosition = PlayerPosition.CM });
        var player = created.Value!;

        player.FirstName = "Gewijzigd";
        player.ShirtNumber = 11;
        Assert.True((await Players.UpdateAsync(player)).IsSuccess);

        var reloaded = await Players.GetByIdAsync(player.Id);

        Assert.Equal("Gewijzigd", reloaded.Value!.FirstName);
        Assert.Equal(11, reloaded.Value.ShirtNumber);
    }

    [Fact]
    public async Task A_detached_entity_round_trips_through_update_without_losing_its_list_columns()
    {
        // The CSV value converters are the part most likely to break when an entity arrives
        // detached: without the ValueComparer, an in-place list edit is silently never written.
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);

        var created = await Games.CreateAsync(new Game
        {
            Opponent = "VVAC",
            Date = Now.Date,
            SeasonId = season.Id
        });

        var game = created.Value!;
        game.UnavailablePlayerIds.Add(players[0].Id);
        game.GuestPlayerIds.Add(players[1].Id);

        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        await using var db = Read();
        var stored = await db.Games.FirstAsync(g => g.Id == game.Id);

        Assert.Equal([players[0].Id], stored.UnavailablePlayerIds);
        Assert.Equal([players[1].Id], stored.GuestPlayerIds);
    }

    [Fact]
    public async Task Creating_a_game_across_two_services_still_resolves_its_season()
    {
        // GameService delegates to SeasonService, which now opens its own context. The game must
        // still come back with the season that call created.
        var created = await Games.CreateAsync(new Game
        {
            Opponent = "Sliedrecht",
            Date = new DateTime(2027, 9, 12),
            SeasonId = 0                       // "auto by date"
        });

        Assert.True(created.IsSuccess);
        Assert.NotEqual(0, created.Value!.SeasonId);

        await using var db = Read();
        var season = await db.Seasons.FirstAsync(s => s.Id == created.Value.SeasonId);

        Assert.Equal("2027/28", season.Name);
        Assert.True(season.Contains(new DateTime(2027, 9, 12)));
    }

    [Fact]
    public async Task A_goal_logged_live_is_committed_before_the_scoreline_is_recomputed()
    {
        // MatchGoalService writes through GameService (its own context) and then re-reads the goals to
        // sync the score. If the write were not committed first the score would lag by one.
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);

        var created = await Games.CreateAsync(new Game
        {
            Opponent = "Stedoco",
            Date = Now.Date,
            SeasonId = season.Id
        });

        await MatchClock.StartMatchAsync(created.Value!.Id);
        await Goals.LogGoalAsync(created.Value.Id, players[0].Id, null, false, false);

        await using var db = Read();
        var stored = await db.Games.FirstAsync(g => g.Id == created.Value.Id);

        Assert.Equal(1, stored.ScoreHome);
        Assert.Equal(0, stored.ScoreAway);
    }
}
