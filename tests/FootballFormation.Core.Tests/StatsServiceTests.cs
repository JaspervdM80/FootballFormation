namespace FootballFormation.Core.Tests;

// Two rules that pull against each other: the report is reused between reads, and it does not
// survive a write by a single read.
public class StatsServiceTests : ServiceTestBase
{
    // A season with one squad member and one finished game they played the whole of.
    private async Task<(Season Season, Player Player, Game Game)> ArrangeSeasonAsync(int goals = 1)
    {
        var season = await SeedSeasonAsync();
        var player = (await SeedPlayersAsync(1))[0];
        await Squads.AddMemberAsync(season.Id, player.Id);

        var created = await Games.CreateAsync(new Game
        {
            Opponent = "VVAC",
            Date = Now.Date,
            SeasonId = season.Id,
            SplitType = GameSplitType.Halves,
            GameDurationMinutes = 60
        });

        var game = created.Value!;

        // A lineup in both halves, so the player ends with minutes rather than an empty report.
        var db = Read();
        var periods = await db.GamePeriods.Where(p => p.GameId == game.Id).ToListAsync();
        foreach (var period in periods)
        {
            db.GamePlayerPositions.Add(new GamePlayerPosition
            {
                GamePeriodId = period.Id,
                PlayerId = player.Id,
                Position = PlayerPosition.ST
            });
        }

        // A scoreline on a match nobody ran live is enough to make it complete.
        var stored = await db.Games.FirstAsync(g => g.Id == game.Id);
        stored.ScoreHome = goals;
        stored.ScoreAway = 0;
        await db.SaveChangesAsync();

        return (season, player, stored);
    }

    [Fact]
    public async Task A_second_read_is_served_from_the_cache_rather_than_rebuilt()
    {
        var (season, _, game) = await ArrangeSeasonAsync();

        var first = await Stats.GetSeasonAsync(season.Id);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.Stats.GoalsFor);

        // Raw SQL skips SaveChanges, so the invalidator never sees it — the only way to prove the
        // second read never touched the database. A real write would invalidate and prove nothing.
        await Read().Database.ExecuteSqlRawAsync(
            "UPDATE Games SET ScoreHome = 99 WHERE Id = {0}", game.Id);

        var second = await Stats.GetSeasonAsync(season.Id);

        Assert.Equal(1, second.Value!.Stats.GoalsFor);
        Assert.Same(first.Value, second.Value);
    }

    [Fact]
    public async Task A_write_between_two_reads_is_visible_in_the_second()
    {
        var (season, _, game) = await ArrangeSeasonAsync();

        var before = await Stats.GetSeasonAsync(season.Id);
        Assert.Equal(1, before.Value!.Stats.GoalsFor);

        game.ScoreHome = 4;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        var after = await Stats.GetSeasonAsync(season.Id);

        Assert.Equal(4, after.Value!.Stats.GoalsFor);
        Assert.NotSame(before.Value, after.Value);
    }

    [Fact]
    public async Task Any_write_at_all_drops_the_cache_even_one_that_cannot_change_a_figure()
    {
        var (season, _, _) = await ArrangeSeasonAsync();
        await Stats.GetSeasonAsync(season.Id);

        var generation = StatsCache.Generation;

        // Nothing to do with anyone's minutes, and still counts: the alternative is a list of
        // which writes matter, and being wrong about it is a stale figure nobody can explain.
        Assert.True((await Users.CreateAsync("Nieuw", "nieuw", "x!Password1", UserRole.Admin)).IsSuccess);

        Assert.NotEqual(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task A_synchronous_write_drops_the_cache_just_as_an_awaited_one_does()
    {
        var (season, _, _) = await ArrangeSeasonAsync();
        await Stats.GetSeasonAsync(season.Id);

        var generation = StatsCache.Generation;

        // Nothing writes synchronously today; the day something does is not the day to find out.
        var db = Read();
        db.Players.Add(new Player { FirstName = "Synchroon", PreferredPosition = PlayerPosition.CB });
        db.SaveChanges();

        Assert.NotEqual(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_leaves_the_cache_alone()
    {
        await ArrangeSeasonAsync();
        var generation = StatsCache.Generation;

        // Zero rows: bumping would drop the reports for a write that never happened.
        await Read().SaveChangesAsync();

        Assert.Equal(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task A_players_figures_are_the_ones_already_built_for_the_season()
    {
        var (season, player, _) = await ArrangeSeasonAsync();

        var seasonStats = await Stats.GetSeasonAsync(season.Id);
        var playerStats = await Stats.GetPlayerAsync(player, season.Id);

        // Same object, not equal figures: a squad of twenty costs one cache entry, not twenty-one.
        Assert.Same(
            seasonStats.Value!.Stats.Players.Single(p => p.Player.Id == player.Id),
            playerStats.Value);
    }

    [Fact]
    public async Task A_player_outside_the_seasons_squad_still_gets_figures()
    {
        var (season, _, _) = await ArrangeSeasonAsync();

        // In nobody's squad, but the page is reachable for them, so this has to answer.
        var outsider = (await Players.CreateAsync(new Player
        {
            FirstName = "Vertrokken",
            PreferredPosition = PlayerPosition.GK
        })).Value!;

        var result = await Stats.GetPlayerAsync(outsider, season.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalMinutes);
        Assert.Equal(outsider.Id, result.Value.Player.Id);
    }

    [Fact]
    public async Task One_seasons_report_is_never_served_for_another()
    {
        var (season, _, _) = await ArrangeSeasonAsync();
        var other = await SeedSeasonAsync(covering: Now.AddYears(-2), isCurrent: false);

        var current = await Stats.GetSeasonAsync(season.Id);
        var earlier = await Stats.GetSeasonAsync(other.Id);

        Assert.Equal(1, current.Value!.Stats.Played);
        Assert.Equal(0, earlier.Value!.Stats.Played);

        // And "all seasons" is a third key, not either of them.
        var all = await Stats.GetSeasonAsync(null);
        Assert.Equal(1, all.Value!.Stats.Played);
        Assert.NotSame(current.Value, all.Value);
    }

    [Fact]
    public async Task A_report_built_while_a_write_lands_is_orphaned_rather_than_served()
    {
        var (season, _, game) = await ArrangeSeasonAsync();

        // The race the generation-in-the-key exists for. A cache that dropped entries instead
        // would serve this stale value until the next write.
        var key = StatsCache.KeyFor($"season:{season.Id}");

        game.ScoreHome = 7;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        StatsCache.Set(key, "the report that was already being built");

        var after = await Stats.GetSeasonAsync(season.Id);

        Assert.Equal(7, after.Value!.Stats.GoalsFor);
    }

    [Fact]
    public async Task Attendance_reads_the_register_against_the_squad_of_the_season_it_belongs_to()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        foreach (var player in players) await Squads.AddMemberAsync(season.Id, player.Id);

        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.Date.AddDays(-6) });
        await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now.Date.AddDays(-4),
            UnavailablePlayerIds = [players[0].Id]
        });
        await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now.Date.AddDays(-2),
            DidNotTakePlace = true
        });

        // Still ahead, so it is nobody's yet — and it is the service that has the clock to know that.
        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.Date.AddDays(3) });

        var result = await Stats.GetTrainingAttendanceAsync(season.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Held);
        Assert.Equal(1, result.Value.Cancelled);
        Assert.Equal(75, result.Value.Percentage);

        var perPlayer = await Stats.GetPlayerTrainingAttendanceAsync(players[0], season.Id);

        Assert.Equal(2, perPlayer.Value!.Held);
        Assert.Equal(1, perPlayer.Value.Missed);
    }
}
