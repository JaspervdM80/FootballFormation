namespace FootballFormation.Core.Tests;

/// The numbers a season's statistics are later built from, which is why the match is driven to exact instants rather than a wall clock.
public class MatchClockServiceTests : LiveMatchTestBase
{

    [Fact]
    public async Task Starting_a_match_puts_the_first_half_on_the_pitch_with_a_zeroed_clock()
    {
        var game = await SeedGameAsync();

        var result = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(result.IsSuccess);
        var started = await ReloadAsync(game.Id);
        Assert.Equal(MatchState.InProgress, started.MatchState);
        Assert.Equal(0, started.ClockAccumulatedSeconds);
        Assert.True(started.IsClockRunning);
        Assert.Equal(started.Periods.OrderBy(p => p.PeriodType).First().Id, started.LivePeriodId);
        Assert.Equal(0, started.Periods.OrderBy(p => p.PeriodType).First().StartedAtSeconds);
    }

    [Fact]
    public async Task A_match_cannot_be_started_twice()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var second = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(second.IsFailure);
        Assert.Equal("This match has already been started", second.Error);
    }

    [Fact]
    public async Task A_game_with_no_line_up_at_all_cannot_kick_off()
    {
        var teamId = EnsureScopedTeam();
        var season = Season.CreateFor(KickOff);
        season.TeamId = teamId;
        Db.Seasons.Add(season);
        await Db.SaveChangesAsync();

        var game = new Game { Opponent = "X", Date = KickOff.Date, TeamId = teamId, SeasonId = season.Id };
        Db.Games.Add(game);
        await Db.SaveChangesAsync();

        var result = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This game has no line-up to play", result.Error);
    }

    [Fact]
    public async Task An_unknown_game_fails_rather_than_throwing()
    {
        var result = await MatchClock.StartMatchAsync(999);

        Assert.True(result.IsFailure);
        Assert.Contains("999", result.Error);
    }

    [Fact]
    public async Task The_clock_runs_from_kick_off_until_the_half_is_whistled_off()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        // There is nothing that stops it in between: no pause, no resume. Whatever the touchline
        // does with the screen, the seconds the season's minutes are built from keep counting.
        Time.Advance(TimeSpan.FromMinutes(7));

        var running = await ReloadAsync(game.Id);
        Assert.True(running.IsClockRunning);
        Assert.Equal(420, running.ElapsedSecondsAt(Time.GetUtcNow().UtcDateTime));
    }

    [Fact]
    public async Task Ending_a_half_stops_the_clock_and_leaves_nothing_live()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);

        var ended = await ReloadAsync(game.Id);
        Assert.Null(ended.LivePeriodId);
        Assert.False(ended.IsClockRunning);
        Assert.Equal(1800, ended.Periods.OrderBy(p => p.PeriodType).First().EndedAtSeconds);
    }

    [Fact]
    public async Task The_second_half_starts_where_the_first_left_off_and_the_break_costs_nothing()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));      // half time
        await MatchClock.StartNextHalfAsync(game.Id);

        var second = await ReloadAsync(game.Id);
        var periods = second.Periods.OrderBy(p => p.PeriodType).ToList();

        Assert.Equal(1800, periods[1].StartedAtSeconds);
        Assert.Equal(periods[1].Id, second.LivePeriodId);
        Assert.True(second.IsClockRunning);
        // Half time added no match time.
        Assert.Equal(1800, second.ClockAccumulatedSeconds);
    }

    [Fact]
    public async Task The_next_half_cannot_start_before_the_current_one_ends()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var result = await MatchClock.StartNextHalfAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("End the current half first", result.Error);
    }

    /// The second quarter's line-up is a plan the coach works through by hand and the clock never stops for it, so the whistle after the
    /// first half hands over to the third quarter.
    [Fact]
    public async Task The_second_half_of_a_quarters_game_starts_at_the_third_quarter()
    {
        var game = await SeedGameAsync(GameSplitType.Quarters);
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        Assert.True((await MatchClock.StartNextHalfAsync(game.Id)).IsSuccess);

        var second = await ReloadAsync(game.Id);
        var periods = second.Periods.OrderBy(p => p.PeriodType).ToList();

        Assert.Equal(periods[2].Id, second.LivePeriodId);
        Assert.Equal(1800, periods[2].StartedAtSeconds);
        // The first half ran as one period, so its second line-up was never kicked off — which is
        // what keeps GameMinutesReport from crediting it a quarter nobody played.
        Assert.Null(periods[1].StartedAtSeconds);
    }

    [Fact]
    public async Task A_quarters_game_has_no_third_half_left_to_start()
    {
        var game = await SeedGameAsync(GameSplitType.Quarters);
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        await MatchClock.StartNextHalfAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);

        var result = await MatchClock.StartNextHalfAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Both halves have been played — finish the match instead", result.Error);
    }

    [Fact]
    public async Task Finishing_closes_the_running_half_and_writes_the_score_from_the_goals()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var players = await PlayersAsync();
        Time.Advance(TimeSpan.FromMinutes(10));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, null, null, false, true);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, true, false);   // own goal

        await MatchClock.FinishMatchAsync(game.Id);

        var finished = await ReloadAsync(game.Id);
        Assert.Equal(MatchState.Finished, finished.MatchState);
        Assert.Null(finished.LivePeriodId);
        Assert.False(finished.IsClockRunning);
        Assert.Equal(1, finished.ScoreHome);
        Assert.Equal(2, finished.ScoreAway);   // their goal plus our own goal
        Assert.Equal(1200, finished.Periods.OrderBy(p => p.PeriodType).First().EndedAtSeconds);
    }

    [Fact]
    public async Task A_match_that_never_started_cannot_be_finished()
    {
        var game = await SeedGameAsync();

        var result = await MatchClock.FinishMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This match has not been started", result.Error);
    }

    [Fact]
    public async Task Starting_the_second_half_records_the_planned_change_as_a_substitution()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        // The second half is planned differently from the first: the bench player takes the starter's slot.
        await BenchForSecondHalfAsync(game.Id, starterOffId: players[1].Id, subOnId: players[2].Id);

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        Assert.True((await MatchClock.StartNextHalfAsync(game.Id)).IsSuccess);

        Db.ChangeTracker.Clear();
        var sub = Assert.Single(await Db.GameSubstitutions.Where(s => s.GameId == game.Id).ToListAsync());
        Assert.Equal(players[1].Id, sub.PlayerOffId);
        Assert.Equal(players[2].Id, sub.PlayerOnId);

        // Stamped at the restart, and against the half it opens — which is what puts it after the half-time line on the timeline and
        // rewinds it out of the second half's minutes.
        var second = await Db.GamePeriods.FirstAsync(p => p.GameId == game.Id && p.PeriodType == PeriodType.SecondHalf);
        Assert.Equal(1800, sub.AtSeconds);
        Assert.Equal(second.Id, sub.GamePeriodId);
    }

    [Fact]
    public async Task Starting_the_second_half_unchanged_records_no_substitution()
    {
        // SeedGameAsync lays the two halves out identically, so nothing changed at the break.
        var game = await SeedGameAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);
        await MatchClock.StartNextHalfAsync(game.Id);

        Db.ChangeTracker.Clear();
        Assert.Empty(await Db.GameSubstitutions.Where(s => s.GameId == game.Id).ToListAsync());
    }

    [Fact]
    public async Task Ending_a_half_carries_the_line_up_into_an_unplanned_next_half()
    {
        var game = await SeedGameAsync();
        await ClearSecondHalfLineupAsync(game.Id);

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);

        Db.ChangeTracker.Clear();
        var first = await SecondHalfLineupAsync(game.Id, PeriodType.FirstHalf);
        var second = await SecondHalfLineupAsync(game.Id, PeriodType.SecondHalf);

        // The same eleven continue, standing where they finished, ready for the coach to change at the break.
        Assert.Equal(
            first.Select(p => (p.PlayerId, p.SlotIndex, p.IsSubstitute)).OrderBy(x => x.PlayerId),
            second.Select(p => (p.PlayerId, p.SlotIndex, p.IsSubstitute)).OrderBy(x => x.PlayerId));
    }

    [Fact]
    public async Task Ending_a_half_leaves_a_planned_next_half_as_it_stands()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await BenchForSecondHalfAsync(game.Id, starterOffId: players[1].Id, subOnId: players[2].Id);

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndHalfAsync(game.Id);

        Db.ChangeTracker.Clear();
        var second = await SecondHalfLineupAsync(game.Id, PeriodType.SecondHalf);

        // The planned swap is untouched: the incoming player still starts, the outgoing one is still benched.
        Assert.False(second.First(p => p.PlayerId == players[2].Id).IsSubstitute);
        Assert.True(second.First(p => p.PlayerId == players[1].Id).IsSubstitute);
    }

    /// The whistle nobody pressed on time. Only the end of the half moves: the restart stays where it actually happened, so everything
    /// recorded in the second half still reads the minute it was scored on.
    [Fact]
    public async Task Correcting_a_half_moves_its_whistle_and_nothing_that_was_recorded_in_it()
    {
        var game = await PlayedWithTheClockLeftRunningAsync();
        var before = await ReloadAsync(game.Id);
        var secondHalfStart = before.Periods.Single(p => p.PeriodType == PeriodType.SecondHalf).StartedAtSeconds;

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 37, secondHalfMinutes: 38);

        Assert.True(result.IsSuccess);
        var corrected = await ReloadAsync(game.Id);
        var first = corrected.Periods.Single(p => p.PeriodType == PeriodType.FirstHalf);
        var second = corrected.Periods.Single(p => p.PeriodType == PeriodType.SecondHalf);

        Assert.Equal(37 * 60, first.EndedAtSeconds);
        Assert.Equal(secondHalfStart, second.StartedAtSeconds);
        Assert.Equal(second.StartedAtSeconds + (38 * 60), second.EndedAtSeconds);

        // The banked total is what the final whistle left behind, so it has to follow the half it was taken from.
        Assert.Equal(second.EndedAtSeconds, corrected.ClockAccumulatedSeconds);
    }

    /// The reason this exists at all: the overrun was credited to whoever was on the pitch, and went on into the season's utilisation.
    [Fact]
    public async Task Shortening_a_half_takes_back_the_minutes_it_never_ran()
    {
        var game = await PlayedWithTheClockLeftRunningAsync();
        var players = await PlayersAsync();

        var inflated = GameMinutesReport.Build(await LoadForMinutesAsync(game.Id)).SecondsFor(players[1].Id);

        await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 37, secondHalfMinutes: 38);

        var corrected = GameMinutesReport.Build(await LoadForMinutesAsync(game.Id)).SecondsFor(players[1].Id);
        Assert.Equal((45 + 40) * 60, inflated);
        Assert.Equal((37 + 38) * 60, corrected);
    }

    [Fact]
    public async Task A_half_cannot_be_whistled_off_before_something_it_already_contains()
    {
        var game = await PlayedWithTheClockLeftRunningAsync();
        var players = await PlayersAsync();

        // A goal in the 40th minute of the first half — the half cannot now be 30 minutes long.
        await Db.GameGoals.AddAsync(new GameGoal
        {
            GameId = game.Id,
            ScorerId = players[1].Id,
            GamePeriodId = (await ReloadAsync(game.Id)).Periods.Single(p => p.PeriodType == PeriodType.FirstHalf).Id,
            AtSeconds = 40 * 60
        });
        await Db.SaveChangesAsync();

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 30, secondHalfMinutes: null);

        Assert.True(result.IsFailure);
        Assert.Equal("That half still has something recorded after {0} minutes", result.ErrorKey);
    }

    [Fact]
    public async Task A_half_cannot_be_stretched_past_the_restart_of_the_next()
    {
        var game = await PlayedWithTheClockLeftRunningAsync();

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 50, secondHalfMinutes: null);

        Assert.True(result.IsFailure);
        Assert.Equal("A half cannot run past the restart of the next one", result.Error);
    }

    /// While the clock is still running it owns the half it is timing, and the next whistle would write over the correction anyway.
    [Fact]
    public async Task A_match_still_being_played_keeps_its_half_lengths()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 25, secondHalfMinutes: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Only a finished match can have its half lengths corrected", result.Error);
    }

    /// The dialog's field cannot go below one, but the rule belongs at the service — a zero-length half would credit nobody the time
    /// they were on the pitch for.
    [Fact]
    public async Task A_half_cannot_be_corrected_to_no_time_at_all()
    {
        var game = await PlayedWithTheClockLeftRunningAsync();

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: 0, secondHalfMinutes: null);

        Assert.True(result.IsFailure);
        Assert.Equal("A half has to last at least a minute", result.Error);
    }

    [Fact]
    public async Task A_half_that_was_never_played_has_no_length_to_correct()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.FinishMatchAsync(game.Id);

        var result = await MatchClock.AdjustHalfLengthsAsync(game.Id, firstHalfMinutes: null, secondHalfMinutes: 30);

        Assert.True(result.IsFailure);
        Assert.Equal("That half was never played", result.Error);
    }

    /// A 60-minute game run with the whistle pressed late twice: a first half of 45 and a second of 40.
    private async Task<Game> PlayedWithTheClockLeftRunningAsync()
    {
        var game = await SeedGameAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(45));
        await MatchClock.EndHalfAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(10));
        await MatchClock.StartNextHalfAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(40));
        await MatchClock.FinishMatchAsync(game.Id);

        return game;
    }

    private async Task<Game> LoadForMinutesAsync(int gameId)
    {
        Db.ChangeTracker.Clear();
        return await Db.Games
            .Include(g => g.Periods).ThenInclude(p => p.PlayerPositions)
            .Include(g => g.Substitutions)
            .Include(g => g.Injuries)
            .FirstAsync(g => g.Id == gameId);
    }

    private Task<List<GamePlayerPosition>> SecondHalfLineupAsync(int gameId, PeriodType type) =>
        Db.GamePlayerPositions
            .Where(pp => pp.GamePeriod.GameId == gameId && pp.GamePeriod.PeriodType == type)
            .ToListAsync();

    private async Task ClearSecondHalfLineupAsync(int gameId)
    {
        var positions = await Db.GamePlayerPositions
            .Where(pp => pp.GamePeriod.GameId == gameId && pp.GamePeriod.PeriodType == PeriodType.SecondHalf)
            .ToListAsync();
        Db.GamePlayerPositions.RemoveRange(positions);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// Rewrites the second half so a bench player starts in a first-half starter's slot — the between-halves change the touchline never
    /// entered as a substitution, which is exactly what kicking off the half now records.
    private async Task BenchForSecondHalfAsync(int gameId, int starterOffId, int subOnId)
    {
        var second = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.GameId == gameId && p.PeriodType == PeriodType.SecondHalf);

        var off = second.PlayerPositions.First(pp => pp.PlayerId == starterOffId && !pp.IsSubstitute);
        var on = second.PlayerPositions.First(pp => pp.PlayerId == subOnId && pp.IsSubstitute);

        (on.SlotIndex, on.Position, on.IsSubstitute) = (off.SlotIndex, off.Position, false);
        (off.SlotIndex, off.IsSubstitute) = (null, true);

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }
}
