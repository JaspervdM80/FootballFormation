namespace FootballFormation.Core.Tests;

/// The pages hand these methods a Game loaded with its whole graph attached, which makes "save this row" and "save everything reachable
/// from this row" easy to confuse — and the difference is a season of line-ups.
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
    public async Task Changing_the_formation_moves_each_starter_to_the_position_her_slot_is_now()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.LM, slot: 5),
            TestData.Starter(players[1].Id, PlayerPosition.RM, slot: 8),
            TestData.Sub(players[2].Id)
        ]);

        var result = await Games.SaveFormationAsync(game.Id, FormationType.F433);

        Assert.True(result.IsSuccess);
        Assert.Equal(FormationType.F433, Read().Games.Single().FormationType);

        // The pitch would draw the new shape either way; these are what the playing-time table and the position statistics read.
        var lineup = Read().GamePlayerPositions.OrderBy(pp => pp.Id).ToList();
        Assert.Equal((PlayerPosition.CM, 5), (lineup[0].Position, lineup[0].SlotIndex));
        Assert.Equal((PlayerPosition.LW, 8), (lineup[1].Position, lineup[1].SlotIndex));
        Assert.Equal((PlayerPosition.CM, null, true), (lineup[2].Position, lineup[2].SlotIndex, lineup[2].IsSubstitute));
    }

    [Fact]
    public async Task Changing_the_formation_keeps_the_lineup_a_half_was_actually_played_with()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Db.GamePeriods.First(p => p.GameId == game.Id);
        period.StartedAtSeconds = 0;
        await Db.SaveChangesAsync();
        await Games.SavePeriodLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.LM, slot: 5),
            TestData.Sub(players[1].Id)
        ]);

        var before = Read().GamePlayerPositions.OrderBy(pp => pp.Id).Select(pp => new { pp.Id, pp.PlayerId }).ToList();

        await Games.SaveFormationAsync(game.Id, FormationType.F433);

        // Rewritten in place rather than replaced: a half run live is the record of who was on the pitch, and re-inserting it would
        // throw away the rows the touchline wrote and hand out new ids.
        Assert.Equal(before, Read().GamePlayerPositions.OrderBy(pp => pp.Id).Select(pp => new { pp.Id, pp.PlayerId }).ToList());
    }

    [Fact]
    public async Task Changing_the_formation_clears_a_shape_a_period_had_of_its_own()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Db.GamePeriods.First(p => p.GameId == game.Id);
        period.FormationTypeOverride = FormationType.F532;
        await Db.SaveChangesAsync();

        // Slot 5 is the first central midfielder in 5-3-2 and a left midfielder in the game's own 4-4-2 — the override is the shape she
        // was standing in, so it is the one she has to be moved out of.
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 5)]);

        await Games.SaveFormationAsync(game.Id, FormationType.F433);

        // The builder offers one shape for the whole game, and an override outranks it — so it would read as the change not taking.
        Assert.All(Read().GamePeriods.Where(p => p.GameId == game.Id), p => Assert.Null(p.FormationTypeOverride));
        Assert.Equal(PlayerPosition.CM, Read().GamePlayerPositions.Single().Position);
    }

    [Fact]
    public async Task Changing_the_formation_of_a_game_that_is_gone_fails()
    {
        var result = await Games.SaveFormationAsync(404, FormationType.F433);

        Assert.True(result.IsFailure);
        Assert.Equal("Game not found", result.ErrorKey);
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

        // On the pitch and on the bench at once — reported as a failure rather than a raw DbUpdateException.
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

        // Delete-then-insert: without a transaction the delete commits before the insert fails, and the period comes out empty.
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

    /// Creating a game may save a season first in its own context, so an interruption leaves an empty season behind. That is allowed to
    /// stand because windows are gapless, so the next attempt resolves to it. See docs/patterns/transactions-and-writes.md.
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

    /// The counterpart to the touchline recount: here the score is typed and the goal list may be shorter, so adding a scorer someone
    /// remembered afterwards must not rewrite a 3-1 as 1-0.
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
