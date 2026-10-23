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
        await SaveLineupAsync(period.Id, [
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

    /// The home page names the last result's scorers from this list, and with only the goal rows loaded the line quietly disappears.
    [Fact]
    public async Task Loading_all_games_brings_back_each_goals_scorer()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 12 });

        var loaded = (await Games.GetAllAsync(season.Id)).Value!;

        Assert.Equal(players[0].Id, loaded.Single().Goals.Single().Scorer!.Id);
    }

    [Fact]
    public async Task Editing_a_game_leaves_its_lineups_goals_and_substitutions_alone()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await SaveLineupAsync(period.Id, [
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
    public async Task Editing_a_game_from_a_list_loaded_before_kick_off_leaves_the_match_being_played_alone()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var stale = (await Games.GetAllAsync(season.Id)).Value!.Single();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(12));
        await Goals.LogGoalAsync(game.Id, scorerId: null, assisterId: null, isOwnGoal: false, isOpponentGoal: true);

        stale.FlagDuty = "vader van Niels";
        Assert.True((await Games.UpdateAsync(stale)).IsSuccess);

        var stored = Read().Games.Single();
        Assert.Equal("vader van Niels", stored.FlagDuty);
        Assert.Equal(MatchState.InProgress, stored.MatchState);
        Assert.NotNull(stored.ClockRunningSince);
        Assert.NotNull(stored.LivePeriodId);
        Assert.Equal(1, stored.ScoreAway);
    }

    [Fact]
    public async Task Editing_a_game_keeps_a_formation_changed_since_the_dialog_was_opened()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var stale = (await Games.GetAllAsync(season.Id)).Value!.Single();

        await Lineups.SaveFormationAsync(game.Id, FormationType.F352);

        stale.Opponent = "Renamed FC";
        Assert.True((await Games.UpdateAsync(stale)).IsSuccess);

        var stored = Read().Games.Single();
        Assert.Equal("Renamed FC", stored.Opponent);
        Assert.Equal(FormationType.F352, stored.FormationType);
    }

    [Fact]
    public async Task Splitting_a_game_into_quarters_gives_each_quarter_the_line_up_planned_for_its_half()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var halves = Read().GamePeriods.Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        await Lineups.SaveLineupsAsync(game.Id, new Dictionary<int, List<GamePlayerPosition>>
        {
            [halves[0].Id] = [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)],
            [halves[1].Id] = [TestData.Starter(players[1].Id, PlayerPosition.GK, slot: 0)]
        });

        game.SplitType = GameSplitType.Quarters;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        var quarters = Read().GamePeriods.Include(p => p.PlayerPositions).Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        Assert.Equal(
            [PeriodType.FirstQuarter, PeriodType.SecondQuarter, PeriodType.ThirdQuarter, PeriodType.FourthQuarter],
            quarters.Select(p => p.PeriodType));
        Assert.Equal(
            [players[0].Id, players[0].Id, players[1].Id, players[1].Id],
            quarters.Select(p => p.PlayerPositions.Single().PlayerId));
    }

    [Fact]
    public async Task Going_back_to_halves_keeps_the_line_up_each_half_opens_with()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(4);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, split: GameSplitType.Quarters))).Value!;
        var quarters = Read().GamePeriods.Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        await Lineups.SaveLineupsAsync(game.Id, quarters.Select((q, i) => (q.Id, i)).ToDictionary(
            entry => entry.Id,
            entry => new List<GamePlayerPosition> { TestData.Starter(players[entry.i].Id, PlayerPosition.GK, slot: 0) }));

        game.SplitType = GameSplitType.Halves;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        var halves = Read().GamePeriods.Include(p => p.PlayerPositions).Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        Assert.Equal([PeriodType.FirstHalf, PeriodType.SecondHalf], halves.Select(p => p.PeriodType));
        Assert.Equal([players[0].Id, players[2].Id], halves.Select(p => p.PlayerPositions.Single().PlayerId));
    }

    [Fact]
    public async Task A_match_that_has_kicked_off_keeps_its_split()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var before = Read().GamePeriods.Where(p => p.GameId == game.Id).Select(p => p.Id).ToList();
        await MatchClock.StartMatchAsync(game.Id);

        // The live events point at the periods by id, and a new split would delete them along with everything recorded against them.
        game.SplitType = GameSplitType.Quarters;
        var result = await Games.UpdateAsync(game);

        Assert.Equal("A match that has kicked off keeps the split it was played in", result.ErrorKey);
        Assert.Equal(before, Read().GamePeriods.Where(p => p.GameId == game.Id).Select(p => p.Id).ToList());
        Assert.Equal(GameSplitType.Halves, Read().Games.Single().SplitType);
    }

    [Fact]
    public async Task A_game_can_be_moved_to_another_of_its_teams_seasons()
    {
        var season = await SeedSeasonAsync();
        var earlier = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        game.SeasonId = earlier.Id;
        Assert.True((await Games.UpdateAsync(game)).IsSuccess);

        Assert.Equal(earlier.Id, Read().Games.Single().SeasonId);
    }

    [Fact]
    public async Task Saving_every_lineup_at_once_leaves_a_half_already_played_as_the_touchline_recorded_it()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var periods = Read().GamePeriods.Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        await SaveLineupAsync(periods[0].Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);
        await MatchClock.StartMatchAsync(game.Id);

        var result = await Lineups.SaveLineupsAsync(game.Id, new Dictionary<int, List<GamePlayerPosition>>
        {
            [periods[0].Id] = [TestData.Starter(players[1].Id, PlayerPosition.GK, slot: 0)],
            [periods[1].Id] = [TestData.Starter(players[2].Id, PlayerPosition.GK, slot: 0)]
        });

        Assert.True(result.IsSuccess);
        Assert.Equal([PeriodType.FirstHalf], result.Value);
        Assert.Equal([players[0].Id], Read().GamePlayerPositions.Where(pp => pp.GamePeriodId == periods[0].Id).Select(pp => pp.PlayerId));
        Assert.Equal([players[2].Id], Read().GamePlayerPositions.Where(pp => pp.GamePeriodId == periods[1].Id).Select(pp => pp.PlayerId));
    }

    [Fact]
    public async Task A_lineup_save_that_fails_part_way_leaves_every_period_as_it_was()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var periods = Read().GamePeriods.Where(p => p.GameId == game.Id).OrderBy(p => p.PeriodType).ToList();
        await SaveLineupAsync(periods[0].Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);

        // One player twice in a period breaks the unique (period, player) index, after the first period's old rows were deleted.
        var result = await Lineups.SaveLineupsAsync(game.Id, new Dictionary<int, List<GamePlayerPosition>>
        {
            [periods[0].Id] = [TestData.Starter(players[1].Id, PlayerPosition.GK, slot: 0)],
            [periods[1].Id] = [TestData.Starter(players[2].Id, PlayerPosition.GK, slot: 0), TestData.Sub(players[2].Id)]
        });

        Assert.True(result.IsFailure);
        Assert.Equal([players[0].Id], Read().GamePlayerPositions.Where(pp => pp.GamePeriodId == periods[0].Id).Select(pp => pp.PlayerId));
        Assert.Empty(Read().GamePlayerPositions.Where(pp => pp.GamePeriodId == periods[1].Id));
    }

    [Fact]
    public async Task A_lineup_save_naming_another_games_period_saves_nothing()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var other = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var mine = Read().GamePeriods.First(p => p.GameId == game.Id).Id;
        var theirs = Read().GamePeriods.First(p => p.GameId == other.Id).Id;

        var result = await Lineups.SaveLineupsAsync(game.Id, new Dictionary<int, List<GamePlayerPosition>>
        {
            [mine] = [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)],
            [theirs] = [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]
        });

        Assert.Equal("Period not found", result.Error);
        Assert.Empty(Read().GamePlayerPositions);
    }

    [Fact]
    public async Task A_games_match_day_arrangements_survive_a_round_trip()
    {
        var season = await SeedSeasonAsync();
        var game = TestData.Game(id: 0, seasonId: season.Id);
        game.MeetTime = new TimeSpan(10, 45, 0);
        game.WarmUpTime = new TimeSpan(11, 15, 0);
        game.DressingRoom = "12";
        game.FieldName = "Veld 3";
        game.SportsPark = "Sportpark De Watertoren";
        game.City = "Zaltbommel";
        game.DressingRoomDuty = "moeder van Fleur";
        game.FlagDuty = "vader van Niels";
        game.WashDuty = "ouder van Seb";

        await Games.CreateAsync(game);
        var loaded = Read().Games.Single();

        Assert.Equal(new TimeSpan(10, 45, 0), loaded.MeetTime);
        Assert.Equal(new TimeSpan(11, 15, 0), loaded.WarmUpTime);
        Assert.Equal("12", loaded.DressingRoom);
        Assert.Equal("Veld 3", loaded.FieldName);
        Assert.Equal("Sportpark De Watertoren", loaded.SportsPark);
        Assert.Equal("Zaltbommel", loaded.City);
        Assert.Equal("moeder van Fleur", loaded.DressingRoomDuty);
        Assert.Equal("vader van Niels", loaded.FlagDuty);
        Assert.Equal("ouder van Seb", loaded.WashDuty);
    }

    [Fact]
    public async Task A_game_created_without_match_day_arrangements_keeps_every_one_of_them_unset()
    {
        var season = await SeedSeasonAsync();

        await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id));
        var loaded = Read().Games.Single();

        Assert.Null(loaded.MeetTime);
        Assert.Null(loaded.WarmUpTime);
        Assert.Null(loaded.DressingRoom);
        Assert.Null(loaded.FieldName);
        Assert.Null(loaded.SportsPark);
        Assert.Null(loaded.City);
        Assert.Null(loaded.DressingRoomDuty);
        Assert.Null(loaded.FlagDuty);
        Assert.Null(loaded.WashDuty);
    }

    [Fact]
    public async Task Changing_the_formation_moves_each_starter_to_the_position_her_slot_is_now()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.LM, slot: 5),
            TestData.Starter(players[1].Id, PlayerPosition.RM, slot: 8),
            TestData.Sub(players[2].Id)
        ]);

        var result = await Lineups.SaveFormationAsync(game.Id, FormationType.F433);

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
        await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.LM, slot: 5),
            TestData.Sub(players[1].Id)
        ]);

        // Kicked off only once the line-up is in: SaveLineupsAsync leaves a half already played alone.
        period.StartedAtSeconds = 0;
        await Db.SaveChangesAsync();

        var before = Read().GamePlayerPositions.OrderBy(pp => pp.Id).Select(pp => new { pp.Id, pp.PlayerId }).ToList();

        await Lineups.SaveFormationAsync(game.Id, FormationType.F433);

        // Rewritten in place rather than replaced: a half run live is the record of who was on the pitch, and re-inserting it would
        // throw away the rows the touchline wrote and hand out new ids.
        Assert.Equal(before, Read().GamePlayerPositions.OrderBy(pp => pp.Id).Select(pp => new { pp.Id, pp.PlayerId }).ToList());
    }

    [Fact]
    public async Task Switching_to_a_smaller_shape_benches_the_starters_it_has_no_slot_for()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Db.GamePeriods.First(p => p.GameId == game.Id);
        await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 6),
            TestData.Starter(players[1].Id, PlayerPosition.ST, slot: 10)
        ]);

        // 3-3-2 stops at slot 8, so the second striker's slot is gone.
        await Lineups.SaveFormationAsync(game.Id, FormationType.F332);

        var lineup = Read().GamePlayerPositions.OrderBy(pp => pp.Id).ToList();
        Assert.Equal((PlayerPosition.RM, 6, false), (lineup[0].Position, lineup[0].SlotIndex, lineup[0].IsSubstitute));

        // Her own position, not the striker's slot she was taken out of — which needs Player loaded alongside the line-up.
        Assert.Equal((PlayerPosition.CM, null, true), (lineup[1].Position, lineup[1].SlotIndex, lineup[1].IsSubstitute));
    }

    [Fact]
    public async Task A_half_already_played_keeps_the_shape_it_was_played_in_rather_than_losing_a_starter_to_a_smaller_one()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        var period = Db.GamePeriods.First(p => p.GameId == game.Id);
        await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 6),
            TestData.Starter(players[1].Id, PlayerPosition.ST, slot: 10)
        ]);

        period.StartedAtSeconds = 0;
        await Db.SaveChangesAsync();

        await Lineups.SaveFormationAsync(game.Id, FormationType.F332);

        // Benching her would take the minutes she played off a match that is over, so the half is pinned to the eleven it was played in.
        Assert.Equal(FormationType.F442, Read().GamePeriods.Single(p => p.Id == period.Id).FormationTypeOverride);
        Assert.All(Read().GamePlayerPositions, entry => Assert.False(entry.IsSubstitute));
        Assert.Equal([6, 10], Read().GamePlayerPositions.OrderBy(pp => pp.Id).Select(pp => pp.SlotIndex));
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
        await SaveLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.CM, slot: 5)]);

        await Lineups.SaveFormationAsync(game.Id, FormationType.F433);

        // The builder offers one shape for the whole game, and an override outranks it — so it would read as the change not taking.
        Assert.All(Read().GamePeriods.Where(p => p.GameId == game.Id), p => Assert.Null(p.FormationTypeOverride));
        Assert.Equal(PlayerPosition.CM, Read().GamePlayerPositions.Single().Position);
    }

    [Fact]
    public async Task Changing_the_formation_of_a_game_that_is_gone_fails()
    {
        var result = await Lineups.SaveFormationAsync(404, FormationType.F433);

        Assert.True(result.IsFailure);
        Assert.Equal("Game not found", result.ErrorKey);
    }

    [Fact]
    public async Task Saving_the_lineups_of_a_game_that_is_gone_fails()
    {
        var result = await Lineups.SaveLineupsAsync(404, new Dictionary<int, List<GamePlayerPosition>>());

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

        await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Starter(players[1].Id, PlayerPosition.CM, slot: 1)
        ]);
        await SaveLineupAsync(period.Id, [
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
        var result = await SaveLineupAsync(period.Id, [
            TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0),
            TestData.Sub(players[0].Id)
        ]);

        Assert.True(result.IsFailure);
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
        await Comments.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Well played" });

        Assert.Equal(kickOff, Read().GameGoals.Single().RecordedAt);
        Assert.Equal(kickOff, Read().GameComments.Single().CreatedAt);
    }

    [Fact]
    public async Task Editing_a_comment_stamps_EditedAt_from_the_injected_clock()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var comment = (await Comments.AddCommentAsync(new GameComment { GameId = game.Id, Body = "First" })).Value!;

        var editedAt = new DateTime(2031, 5, 4, 18, 0, 0, DateTimeKind.Utc);
        Time.SetUtcNow(editedAt);

        await Comments.UpdateCommentAsync(comment.Id, "Second", isPublic: true);

        var saved = Read().GameComments.Single();
        Assert.Equal("Second", saved.Body);
        Assert.Equal(editedAt, saved.EditedAt);
    }

    [Fact]
    public async Task Publishing_a_comment_without_changing_it_is_not_an_edit()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var comment = (await Comments.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Same" })).Value!;

        Time.SetUtcNow(Now.AddHours(3));
        await Comments.UpdateCommentAsync(comment.Id, "Same", isPublic: true);

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
