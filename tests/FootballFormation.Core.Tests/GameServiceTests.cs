namespace FootballFormation.Core.Tests;

/// <summary>
/// Writing a game without disturbing what hangs off it. The pages hand these methods a Game they
/// loaded with its whole graph attached, which makes "save this row" and "save everything reachable
/// from this row" easy to confuse — and the difference is a season of lineups.
/// <para>
/// One read is pinned here too: a single game loads through the same <c>GameQueries</c> shapes the
/// live screen uses, and a level dropped from a shared chain fails silently.
/// </para>
/// </summary>
public class GameServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Loading_one_game_brings_back_its_lineups_and_both_players_on_a_goal()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)
        ]);
        await Games.AddGoalAsync(new GameGoal
        {
            GameId = game.Id,
            ScorerId = players[1].Id,
            AssisterId = players[2].Id,
            Minute = 12
        });

        var loaded = (await Games.GetByIdAsync(game.Id)).Value!;

        Assert.Equal([PeriodType.FirstHalf, PeriodType.SecondHalf], loaded.Periods.Select(p => p.PeriodType));
        Assert.Equal(players[0].Id, loaded.Periods[0].PlayerPositions.Single().Player!.Id);
        Assert.Equal(players[1].Id, loaded.Goals.Single().Scorer!.Id);
        Assert.Equal(players[2].Id, loaded.Goals.Single().Assister!.Id);
    }

    [Fact]
    public async Task Editing_a_game_leaves_its_lineups_goals_and_substitutions_alone()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Starter(players[1].Id, PlayerPosition.CM, slot: 1)
        ]);
        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[1].Id, Minute = 12 });

        var before = Read().GamePlayerPositions.OrderBy(pp => pp.Id)
            .Select(pp => new { pp.Id, pp.PlayerId, pp.Position, pp.SlotIndex })
            .ToList();
        var goalBefore = Read().GameGoals.Single().Id;

        // The page passes back the graph it rendered from, not a bare row.
        var loaded = (await Games.GetAllWithDetailsAsync(season.Id)).Value!.Single();
        loaded.Opponent = "Renamed FC";
        var result = await Games.UpdateAsync(loaded);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed FC", Read().Games.Single().Opponent);

        var after = Read().GamePlayerPositions.OrderBy(pp => pp.Id)
            .Select(pp => new { pp.Id, pp.PlayerId, pp.Position, pp.SlotIndex })
            .ToList();
        Assert.Equal(before, after);
        Assert.Equal(goalBefore, Read().GameGoals.Single().Id);
    }

    [Fact]
    public async Task Saving_a_lineup_replaces_the_previous_one_wholesale()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);

        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Starter(players[1].Id, PlayerPosition.CM, slot: 1)
        ]);
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[2].Id, PlayerPosition.ST, slot: 0)
        ]);

        var saved = Read().GamePlayerPositions.Where(pp => pp.GamePeriodId == period.Id).ToList();
        Assert.Equal(players[2].Id, Assert.Single(saved).PlayerId);
    }

    [Fact]
    public async Task A_player_cannot_be_listed_twice_in_the_same_period()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);

        // On the pitch and on the bench at once is the shape the unique index rules out. The
        // service reports it as a failure rather than letting a raw DbUpdateException escape.
        var result = await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Sub(players[0].Id)
        ]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_failed_lineup_save_leaves_the_previous_lineup_standing()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);

        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Starter(players[1].Id, PlayerPosition.CM, slot: 1)
        ]);

        // Delete-then-insert: without a transaction the delete would already have committed by the
        // time the insert fails, and the period would come out empty.
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 1)
        ]);

        Assert.Equal(2, Read().GamePlayerPositions.Count(pp => pp.GamePeriodId == period.Id));
    }

    [Fact]
    public async Task Recorded_timestamps_come_from_the_injected_clock()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        // Far from the wall clock, so a stray DateTime.UtcNow cannot pass by coincidence.
        var kickOff = new DateTime(2031, 5, 4, 10, 30, 0, DateTimeKind.Utc);
        Time.SetUtcNow(kickOff);

        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 12 });
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Well played" });

        Assert.Equal(kickOff, Read().GameGoals.Single().RecordedAt);
        Assert.Equal(kickOff, Read().GameComments.Single().CreatedAt);
    }

    [Fact]
    public async Task Editing_a_comment_stamps_EditedAt_from_the_injected_clock()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var comment = (await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "First" })).Value!;

        var editedAt = new DateTime(2031, 5, 4, 18, 0, 0, DateTimeKind.Utc);
        Time.SetUtcNow(editedAt);

        await Games.UpdateCommentAsync(comment.Id, "Second", isPublic: true);

        Assert.Equal(editedAt, Read().GameComments.Single().EditedAt);
    }

    [Fact]
    public async Task Publishing_a_comment_without_changing_it_is_not_an_edit()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var comment = (await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Same" })).Value!;

        Time.SetUtcNow(Now.AddHours(3));
        await Games.UpdateCommentAsync(comment.Id, "Same", isPublic: true);

        // The text is unchanged, so an "edited" marker would be a lie.
        var saved = Read().GameComments.Single();
        Assert.Null(saved.EditedAt);
        Assert.True(saved.IsPublic);
    }

    [Fact]
    public async Task Creating_a_game_without_a_season_resolves_one_from_its_date()
    {
        var season = await SeedSeasonAsync();

        var created = await Games.CreateAsync(TestData.Game(id: 0, seasonId: 0, date: season.StartDate.AddDays(30)));

        Assert.True(created.IsSuccess);
        Assert.Equal(season.Id, created.Value!.SeasonId);
    }

    /// <summary>
    /// Creating a game may save a season first, in its own context, so the two saves are two
    /// transactions and something can stop between them. What it leaves behind is an empty season,
    /// and this is why that is allowed to stand rather than being wrapped in machinery: a season is
    /// a gapless window, so the next attempt resolves to the one already there. See docs/patterns/transactions-and-writes.md.
    /// </summary>
    [Fact]
    public async Task A_game_scheduled_into_an_empty_season_joins_it_rather_than_making_a_second_one()
    {
        var season = await SeedSeasonAsync();
        var stranded = (await Seasons.GetOrCreateForDateAsync(season.EndDate.AddYears(1))).Value!;

        var created = await Games.CreateAsync(
            TestData.Game(id: 0, seasonId: 0, date: stranded.StartDate.AddDays(10)));

        Assert.Equal(stranded.Id, created.Value!.SeasonId);
        Assert.Equal(2, Read().Seasons.Count());
    }

    /// <summary>
    /// The counterpart to the touchline recount in <c>MatchGoalServiceTests</c>. Here the score is
    /// typed and the goal list is allowed to be shorter than it, so adding a scorer someone
    /// remembered afterwards must not rewrite a 3-1 as 1-0.
    /// </summary>
    [Fact]
    public async Task A_goal_added_after_the_match_leaves_a_hand_typed_scoreline_alone()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.SaveScoreAsync(game.Id, 3, 1);

        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 12 });

        var saved = Read().Games.Single();
        Assert.Equal(3, saved.ScoreHome);
        Assert.Equal(1, saved.ScoreAway);
    }
}
