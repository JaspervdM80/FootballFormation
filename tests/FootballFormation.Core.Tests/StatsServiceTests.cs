using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The cached statistics. Two rules matter and they pull against each other: the report must be
/// reused between reads, and it must not survive a write by a single read. Everything here is one
/// or the other, or the seam between them.
/// </summary>
public class StatsServiceTests : ServiceTestBase
{
    /// <summary>A season with one squad member and one finished game they played the whole of.</summary>
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

        // A lineup in both halves, so GameMinutesReport has something to reconstruct and the
        // player ends the match with minutes rather than an empty report.
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

        // A scoreline on a match nobody ran live is enough to make it complete (Game.IsComplete),
        // which is the state every figure here depends on.
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

        // Raw SQL on purpose: it is the one write that does *not* go through SaveChanges, so it
        // changes the data without StatsCacheInvalidator noticing. That makes it the only way to
        // prove the second read never looked at the database — a real write would invalidate, and
        // an unchanged answer would prove nothing.
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

        // A user has nothing to do with anyone's minutes. It still counts: the alternative is a
        // list of which writes matter, and being wrong about that list is a stale figure nobody
        // can explain. A needless rebuild costs milliseconds.
        Assert.True((await Users.CreateAsync("Nieuw", "nieuw", "x!Password1", UserRole.Admin)).IsSuccess);

        Assert.NotEqual(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task A_synchronous_write_drops_the_cache_just_as_an_awaited_one_does()
    {
        var (season, _, _) = await ArrangeSeasonAsync();
        await Stats.GetSeasonAsync(season.Id);

        var generation = StatsCache.Generation;

        // SaveChanges, not SaveChangesAsync. Nothing in the app writes this way today, and the
        // interceptor overrides both anyway — the day something does is not the day to find out
        // the statistics quietly stopped noticing writes.
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

        // No tracked changes, so SaveChanges reports zero rows. Bumping here would throw the
        // reports away for a write that never happened.
        await Read().SaveChangesAsync();

        Assert.Equal(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task A_players_figures_are_the_ones_already_built_for_the_season()
    {
        var (season, player, _) = await ArrangeSeasonAsync();

        var seasonStats = await Stats.GetSeasonAsync(season.Id);
        var playerStats = await Stats.GetPlayerAsync(player, season.Id);

        // Same object, not merely equal figures. SeasonStatsReport builds its per-player entries by
        // calling PlayerStatsReport unchanged, so /players/{id}/stats can read the report /stats
        // already cached — which is why a squad of twenty costs one cache entry and not twenty-one.
        Assert.Same(
            seasonStats.Value!.Stats.Players.Single(p => p.Player.Id == player.Id),
            playerStats.Value);
    }

    [Fact]
    public async Task A_player_outside_the_seasons_squad_still_gets_figures()
    {
        var (season, _, _) = await ArrangeSeasonAsync();

        // On file, in nobody's squad — someone who left, or who is reached from another season.
        // The page is reachable for them, so the service has to answer rather than fail.
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

        // The race the generation-in-the-key exists for. Take the key the way StatsService does,
        // before loading; let a write land; then store under it. A cache that invalidated by
        // dropping entries would serve this stale value until the next write — here it goes
        // somewhere no later reader looks.
        var key = StatsCache.KeyFor($"season:{season.Id}");

        game.ScoreHome = 7;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        StatsCache.Set(key, "the report that was already being built");

        var after = await Stats.GetSeasonAsync(season.Id);

        Assert.Equal(7, after.Value!.Stats.GoalsFor);
    }
}
