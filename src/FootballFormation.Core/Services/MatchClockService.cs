using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Services;

/// Half time is the only stoppage: a line-up planned for the middle of a half never reaches this service, and there is deliberately no
/// pause — a clock a stray tap could stop is a clock the season's minutes cannot be trusted from.
public class MatchClockService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchClockService> logger)
{
    /// Injected rather than <see cref="DateTime.UtcNow"/> so the half arithmetic can be driven to an exact instant under test — it is
    /// the part of the live match most likely to be silently wrong, and a season's statistics depend on it.
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public Task<Result<Game>> StartMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the match",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.NotStarted)
                return Result.Failure<Game>("This match has already been started");

            var first = game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
            if (first is null) return Result.Failure<Game>("This game has no line-up to play");

            game.MatchState = MatchState.InProgress;
            game.ClockAccumulatedSeconds = 0;
            game.ClockRunningSince = UtcNow;
            game.LivePeriodId = first.Id;
            first.StartedAtSeconds = 0;
            first.EndedAtSeconds = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started live match {GameId} in the {Half} with line-up {PeriodId}",
                gameId, first.PeriodType.Half(), first.Id);
            return Result.Success(game);
        }, change: LiveMatchEvent.KickOff);

    /// The clock stops and no half is live until the next kicks off.
    public Task<Result<Game>> EndHalfAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "end the half",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = game.LiveHalf();
            if (current is null) return Result.Failure<Game>("No half is being played");

            BankClock(game);
            current.EndedAtSeconds = game.ClockAccumulatedSeconds;
            game.LivePeriodId = null;

            await CarryLineupToNextHalfAsync(db, game, current, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Ended the {Half} of game {GameId} at {Seconds}s",
                current.PeriodType.Half(), gameId, current.EndedAtSeconds);
            return Result.Success(game);
        });

    /// A next half never planned defaults to whoever finished this one, so the break opens on the same eleven for the coach to adjust
    /// rather than an empty pitch. Only when it is empty: a distinct half planned in the builder is left as it stands.
    private static async Task CarryLineupToNextHalfAsync(
        AppDbContext db, Game game, GamePeriod ended, CancellationToken cancellationToken)
    {
        var next = game.NextHalf();
        if (next is null) return;

        await db.Entry(next).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);
        if (next.PlayerPositions.Count > 0) return;

        await db.Entry(ended).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);
        foreach (var pos in ended.PlayerPositions)
            next.PlayerPositions.Add(new GamePlayerPosition
            {
                GamePeriodId = next.Id,
                PlayerId = pos.PlayerId,
                Position = pos.Position,
                SlotIndex = pos.SlotIndex,
                IsSubstitute = pos.IsSubstitute
            });
    }

    /// <see cref="Game.NextHalf"/> decides which line-up opens it, skipping any planned for the middle of the half just played.
    public Task<Result<Game>> StartNextHalfAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the next half",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.LivePeriodId is not null)
                return Result.Failure<Game>("End the current half first");

            var next = game.NextHalf();
            if (next is null)
                return Result.Failure<Game>("Both halves have been played — finish the match instead");

            // Read before the clock moves on and the next half becomes the last-played one: this is the half just finished, whose final
            // pitch the next half's line-up is a change from.
            var previous = game.CurrentOrLastHalf();

            BankClock(game);
            next.StartedAtSeconds = game.ClockAccumulatedSeconds;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;
            game.ClockRunningSince = UtcNow;

            await RecordHalfTimeChangesAsync(db, game, previous, next, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started the {Half} of game {GameId} at {Seconds}s",
                next.PeriodType.Half(), gameId, next.StartedAtSeconds);
            return Result.Success(game);
        });

    /// Recorded at the restart second so the timeline shows the half-time swaps; minutes are unaffected because the next half rewinds each
    /// sub back to its starter, crediting the leaver no second-half time. An injured player is already off, so she is left out.
    private async Task RecordHalfTimeChangesAsync(
        AppDbContext db, Game game, GamePeriod? previous, GamePeriod next, CancellationToken cancellationToken)
    {
        if (previous is null) return;

        await db.Entry(previous).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);
        await db.Entry(next).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

        var injured = (await db.GameInjuries
            .Where(i => i.GameId == game.Id)
            .Select(i => i.PlayerId)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var swap in LineupDiff.Swaps(previous, next, injured))
        {
            db.GameSubstitutions.Add(new GameSubstitution
            {
                GameId = game.Id,
                GamePeriodId = next.Id,
                PlayerOffId = swap.PlayerOffId,
                PlayerOnId = swap.PlayerOnId,
                AtSeconds = next.StartedAtSeconds!.Value,
                RecordedAt = UtcNow,
                SlotIndex = swap.SlotIndex,
                Position = swap.Position
            });
        }
    }

    public Task<Result<Game>> FinishMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "finish the match",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState == MatchState.NotStarted)
                return Result.Failure<Game>("This match has not been started");

            BankClock(game);

            var current = game.LiveHalf();
            if (current is not null) current.EndedAtSeconds = game.ClockAccumulatedSeconds;

            game.LivePeriodId = null;
            game.MatchState = MatchState.Finished;

            // Recounted here rather than through MatchGoalService: this is the recount that settles the game for the season.
            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
            game.CountScoreFrom(goals);

            // The other place a match becomes part of the record.
            await StandingInjuries.RecordAsync(db, game, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Result.Success(game);
        }, change: LiveMatchEvent.FullTime);

    /// Corrects a match played with the whistle pressed late. Only the end of a half moves: every goal, substitution and injury keeps the
    /// second it was recorded on, so a shortened half re-times what it contains without touching a row, and the second half stays where it
    /// actually restarted. A null length leaves that half as it stands.
    public Task<Result<Game>> AdjustHalfLengthsAsync(
        int gameId, int? firstHalfMinutes, int? secondHalfMinutes, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "correct the half lengths",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            // A running clock is still the source of truth for the half it is timing, and moving an end under it would be overwritten by
            // the next whistle anyway.
            if (game.MatchState != MatchState.Finished)
                return Result.Failure<Game>("Only a finished match can have its half lengths corrected");

            var lastRecorded = await LastRecordedSecondsAsync(db, gameId, cancellationToken);
            var corrected = new List<(GamePeriod Half, int End)>();

            foreach (var (type, requested) in new[]
                     {
                         (PeriodType.FirstHalf, firstHalfMinutes),
                         (PeriodType.SecondHalf, secondHalfMinutes)
                     })
            {
                if (requested is not { } minutes) continue;

                // The half is named nowhere in these messages: UiFeedback translates a failure's template but not its arguments, so a
                // half name handed in as one would reach a Dutch screen in English.
                if (game.PlayedHalf(type) is not { } half)
                    return Result.Failure<Game>("That half was never played");

                if (minutes < 1)
                    return Result.Failure<Game>("A half has to last at least a minute");

                var end = half.StartedAtSeconds!.Value + (minutes * 60);

                if (lastRecorded.GetValueOrDefault(half.Id) > end)
                    return Result.Failure<Game>(
                        "That half still has something recorded after {0} minutes", minutes);

                if (NextKickOffAfter(game, half) is { } restart && end > restart)
                    return Result.Failure<Game>("A half cannot run past the restart of the next one");

                corrected.Add((half, end));
            }

            foreach (var (half, end) in corrected) half.EndedAtSeconds = end;

            // The banked clock is the last whistle on the elapsed axis every event's AtSeconds is anchored to — not the played duration,
            // which is Game.PlayedDurationSeconds and is the sum of the halves whatever the break between them cost.
            game.ClockAccumulatedSeconds = game.Periods.Max(p => p.EndedAtSeconds ?? 0);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Corrected the half lengths of game {GameId} to {First} and {Second} minutes",
                gameId, firstHalfMinutes, secondHalfMinutes);
            return Result.Success(game);
        });

    /// The latest second anything was recorded against each half, so a whistle is never moved in front of a goal or a substitution the
    /// half already contains — which would credit playing time past the change that ended it.
    private static async Task<Dictionary<int, int>> LastRecordedSecondsAsync(
        AppDbContext db, int gameId, CancellationToken cancellationToken)
    {
        var goals = await db.GameGoals
            .Where(g => g.GameId == gameId && g.GamePeriodId != null && g.AtSeconds != null)
            .Select(g => new { PeriodId = g.GamePeriodId!.Value, Seconds = g.AtSeconds!.Value })
            .ToListAsync(cancellationToken);

        var substitutions = await db.GameSubstitutions
            .Where(s => s.GameId == gameId)
            .Select(s => new { PeriodId = s.GamePeriodId, Seconds = s.AtSeconds })
            .ToListAsync(cancellationToken);

        var injuries = await db.GameInjuries
            .Where(i => i.GameId == gameId)
            .Select(i => new { PeriodId = i.GamePeriodId, Seconds = i.AtSeconds })
            .ToListAsync(cancellationToken);

        return goals.Concat(substitutions).Concat(injuries)
            .GroupBy(e => e.PeriodId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.Seconds));
    }

    /// Null for the last half played — otherwise the second the next one kicked off, which no earlier half may be stretched past.
    private static int? NextKickOffAfter(Game game, GamePeriod half) => game.Periods
        .Where(p => p.StartedAtSeconds > half.StartedAtSeconds)
        .Min(p => p.StartedAtSeconds);

    /// Every state change calls this first, so no seconds are lost or double-counted between the anchor and the banked total.
    private void BankClock(Game game)
    {
        game.ClockAccumulatedSeconds = game.ElapsedSecondsAt(UtcNow);
        game.ClockRunningSince = null;
    }

    private Result<Game> NotFound(int gameId)
    {
        logger.LogWarning("Live match {GameId} not found", gameId);
        return LiveMatchQueries.GameNotFound<Game>(gameId);
    }
}
