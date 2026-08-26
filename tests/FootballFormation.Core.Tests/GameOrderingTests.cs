namespace FootballFormation.Core.Tests;

/// <summary>
/// The order games are handed back in. SQLite has no date type, so <see cref="Game.Date"/> lives
/// in a TEXT column and an <c>ORDER BY</c> in the database compares the text a date was written
/// as — these pin the ordering to the date itself.
/// </summary>
public class GameOrderingTests : ServiceTestBase
{
    [Fact]
    public async Task Games_come_back_newest_first()
    {
        var season = await SeedSeasonAsync();
        await SeedGamesAsync(season.Id,
            ("Oldest", new DateTime(2026, 3, 1)),
            ("Newest", new DateTime(2026, 3, 21)),
            ("Middle", new DateTime(2026, 3, 14)));

        var games = (await Games.GetAllAsync(season.Id)).Value!;

        Assert.Equal(
            new[] { "Newest", "Middle", "Oldest" },
            games.Select(g => g.Opponent).ToArray());
    }

    [Fact]
    public async Task The_details_read_is_ordered_the_same_way()
    {
        var season = await SeedSeasonAsync();
        await SeedGamesAsync(season.Id,
            ("Oldest", new DateTime(2026, 3, 1)),
            ("Newest", new DateTime(2026, 3, 21)));

        var games = (await Games.GetAllWithDetailsAsync(season.Id)).Value!;

        Assert.Equal(
            new[] { "Newest", "Oldest" },
            games.Select(g => g.Opponent).ToArray());
    }

    /// <summary>
    /// The reason the ordering happens on the materialised date. EF writes a date as
    /// <c>2026-03-21 09:00:00</c>; the ISO form writes the same instant as
    /// <c>2026-03-21T09:00:00</c>, and <c>T</c> sorts after a space, so as text the earlier
    /// kick-off climbs above the later one. Read as dates, it cannot.
    /// </summary>
    [Fact]
    public async Task A_date_written_in_another_text_format_still_sorts_by_its_date()
    {
        var season = await SeedSeasonAsync();
        var ids = await SeedGamesAsync(season.Id,
            ("Morning", new DateTime(2026, 3, 21, 9, 0, 0)),
            ("Afternoon", new DateTime(2026, 3, 21, 15, 0, 0)));

        await Db.Database.ExecuteSqlRawAsync(
            "UPDATE Games SET Date = '2026-03-21T09:00:00' WHERE Id = {0}", ids["Morning"]);

        var games = (await Games.GetAllAsync(season.Id)).Value!;

        Assert.Equal(
            new[] { "Afternoon", "Morning" },
            games.Select(g => g.Opponent).ToArray());
    }

    [Fact]
    public async Task Two_fixtures_on_the_same_day_keep_the_order_they_were_entered()
    {
        var season = await SeedSeasonAsync();
        var sameDay = new DateTime(2026, 3, 21);
        var ids = await SeedGamesAsync(season.Id,
            ("Entered first", sameDay),
            ("Entered second", sameDay));

        var games = (await Games.GetAllAsync(season.Id)).Value!;

        Assert.Equal(
            new[] { ids["Entered first"], ids["Entered second"] },
            games.Select(g => g.Id).ToArray());
    }

    /// <summary>The clock does not move between these two calls, so both rows carry the same
    /// CreatedAt and the tie-break is the only thing deciding.</summary>
    [Fact]
    public async Task Comments_written_in_the_same_instant_put_the_later_one_on_top()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Written first" });
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Written second" });

        var comments = (await Games.GetCommentsAsync(game.Id, includePrivate: true)).Value!;

        Assert.Equal(
            new[] { "Written second", "Written first" },
            comments.Select(c => c.Body).ToArray());
    }

    /// <summary>Games written straight to the database — these tests are about read order, and
    /// <c>CreateAsync</c> would drag the whole period graph in for no gain.</summary>
    private async Task<Dictionary<string, int>> SeedGamesAsync(
        int seasonId, params (string Opponent, DateTime Date)[] fixtures)
    {
        var games = fixtures
            .Select(f => new Game { SeasonId = seasonId, Opponent = f.Opponent, Date = f.Date })
            .ToList();

        Db.Games.AddRange(games);
        await Db.SaveChangesAsync();

        return games.ToDictionary(g => g.Opponent, g => g.Id);
    }
}
