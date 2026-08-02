using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Runs a match as it happens: the clock, the period the team is currently playing, goals, and
/// substitutions. Goal storage itself is delegated to <see cref="GameService"/> — the only thing
/// added here is stamping the live minute and keeping the scoreline in step with the logged goals.
/// </summary>
public class LiveMatchService(
    AppDbContext db,
    GameService games,
    LiveMatchNotifier notifier,
    ILogger<LiveMatchService> logger)
{
    /// <summary>
    /// Everything the live screen renders, in one round trip: the periods with their lineups and
    /// players, the goals, and the substitutions with both players named.
    /// </summary>
    public Task<Result<Game>> GetLiveAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "load live match", async () =>
        {
            // No tracking, and it matters: a spectator's circuit keeps one scoped DbContext for
            // its whole life, so a tracked Game would keep returning the score, clock and state
            // from its first load while newly inserted goals appeared alongside them. Identity
            // resolution keeps the shared Player rows as single instances.
            var game = await db.Games
                .AsNoTrackingWithIdentityResolution()
                .Include(g => g.Periods.OrderBy(p => p.PeriodType))
                    .ThenInclude(p => p.PlayerPositions)
                        .ThenInclude(pp => pp.Player)
                .Include(g => g.Goals)
                    .ThenInclude(gl => gl.Scorer)
                .Include(g => g.Goals)
                    .ThenInclude(gl => gl.Assister)
                .Include(g => g.Substitutions)
                    .ThenInclude(s => s.PlayerOff)
                .Include(g => g.Substitutions)
                    .ThenInclude(s => s.PlayerOn)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game is null)
            {
                logger.LogWarning("Live match {GameId} not found", gameId);
                return Result.Failure<Game>($"Game with ID {gameId} not found");
            }

            return Result.Success(game);
        });

    /// <summary>
    /// The match being played right now, or null when there is none. Nothing stops two games being
    /// in progress at once, so the most recent by date wins — that is the one someone standing at a
    /// pitch is watching.
    /// </summary>
    public Task<Result<Game?>> GetInProgressAsync() =>
        ServiceOperation.RunAsync(logger, "find the match in progress", async () =>
        {
            var game = await db.Games
                .AsNoTracking()
                .Where(g => g.MatchState == MatchState.InProgress)
                .OrderByDescending(g => g.Date)
                .FirstOrDefaultAsync();

            return Result.Success(game);
        });

    public Task<Result<Game>> StartMatchAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "start match", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.NotStarted)
                return Result.Failure<Game>("This match has already been started");

            var first = game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
            if (first is null) return Result.Failure<Game>("This game has no periods to play");

            game.MatchState = MatchState.InProgress;
            game.ClockAccumulatedSeconds = 0;
            game.ClockRunningSince = DateTime.UtcNow;
            game.LivePeriodId = first.Id;
            first.StartedAtSeconds = 0;
            first.EndedAtSeconds = null;

            await db.SaveChangesAsync();
            logger.LogInformation("Started live match {GameId} at period {PeriodId}", gameId, first.Id);
            return Notified(game);
        });

    public Task<Result<Game>> PauseClockAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "pause the clock", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            if (!game.IsClockRunning) return Result.Failure<Game>("The clock is not running");

            BankClock(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Paused clock for game {GameId} at {Seconds}s",
                gameId, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    public Task<Result<Game>> ResumeClockAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "resume the clock", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.IsClockRunning) return Result.Failure<Game>("The clock is already running");
            if (game.LivePeriodId is null)
                return Result.Failure<Game>("Start the next period before resuming the clock");

            game.ClockRunningSince = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("Resumed clock for game {GameId} at {Seconds}s",
                gameId, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    /// <summary>Whistles the current period off. The clock stops and no period is live until the next one starts.</summary>
    public Task<Result<Game>> EndPeriodAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "end the period", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            var current = CurrentPeriod(game);
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            BankClock(game);
            current.EndedAtSeconds = game.ClockAccumulatedSeconds;
            game.LivePeriodId = null;

            await db.SaveChangesAsync();
            logger.LogInformation("Ended period {PeriodId} of game {GameId} at {Seconds}s",
                current.Id, gameId, current.EndedAtSeconds);
            return Notified(game);
        });

    public Task<Result<Game>> StartNextPeriodAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "start the next period", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.LivePeriodId is not null)
                return Result.Failure<Game>("End the current period first");

            var next = NextPeriod(game);
            if (next is null)
                return Result.Failure<Game>("Every period has been played — finish the match instead");

            BankClock(game);
            next.StartedAtSeconds = game.ClockAccumulatedSeconds;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;
            game.ClockRunningSince = DateTime.UtcNow;

            await db.SaveChangesAsync();
            logger.LogInformation("Started period {PeriodId} of game {GameId} at {Seconds}s",
                next.Id, gameId, next.StartedAtSeconds);
            return Notified(game);
        });

    /// <summary>
    /// Rolls straight from the current period into the next one without stopping the clock, for the
    /// quarter boundaries that are not a real break (see <see cref="PeriodTypeExtensions.IsFollowedByBreak"/>).
    /// The lineup changes over, the running time does not.
    /// </summary>
    public Task<Result<Game>> AdvancePeriodAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "start the next period", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            var current = CurrentPeriod(game);
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            var next = NextPeriod(game);
            if (next is null)
                return Result.Failure<Game>("Every period has been played — finish the match instead");

            // Both ends read the same instant, so no seconds fall between the two periods. The
            // clock anchor is deliberately left alone: it must keep running through the change.
            var elapsed = game.ElapsedSecondsAt(DateTime.UtcNow);
            current.EndedAtSeconds = elapsed;
            next.StartedAtSeconds = elapsed;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;

            await db.SaveChangesAsync();
            logger.LogInformation("Game {GameId} rolled from period {From} into {To} at {Seconds}s",
                gameId, current.Id, next.Id, elapsed);
            return Notified(game);
        });

    public Task<Result<Game>> FinishMatchAsync(int gameId) =>
        ServiceOperation.RunAsync(logger, "finish the match", async () =>
        {
            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return NotFound(gameId);

            if (game.MatchState == MatchState.NotStarted)
                return Result.Failure<Game>("This match has not been started");

            BankClock(game);

            var current = CurrentPeriod(game);
            if (current is not null) current.EndedAtSeconds = game.ClockAccumulatedSeconds;

            game.LivePeriodId = null;
            game.MatchState = MatchState.Finished;

            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync();
            game.ScoreHome = Game.CountOurGoals(goals);
            game.ScoreAway = Game.CountTheirGoals(goals);

            await db.SaveChangesAsync();
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    /// <param name="scorerId">Null for an opponent goal — we do not track their players.</param>
    public Task<Result<GameGoal>> LogGoalAsync(
        int gameId, int? scorerId, int? assisterId, bool isOwnGoal, bool isOpponentGoal) =>
        ServiceOperation.RunAsync(logger, "log the goal", async () =>
        {
            var game = await db.Games.FindAsync(gameId);
            if (game is null) return Result.Failure<GameGoal>($"Game with ID {gameId} not found");

            if (scorerId is null && !isOpponentGoal)
                return Result.Failure<GameGoal>("A goal for us needs a scorer");

            var goal = new GameGoal
            {
                GameId = gameId,
                ScorerId = scorerId,
                AssisterId = assisterId,
                // The minute the clock showed. Minute 0 reads oddly on a timeline, so the first
                // minute of play is 1' — matching how football scorelines are written.
                Minute = (game.ElapsedSecondsAt(DateTime.UtcNow) / 60) + 1,
                IsOwnGoal = isOwnGoal,
                IsOpponentGoal = isOpponentGoal
            };

            var added = await games.AddGoalAsync(goal);
            if (added.IsFailure) return added;

            await SyncScoreAsync(gameId);
            notifier.Notify(gameId);
            return added;
        });

    /// <summary>Removes a goal and pulls the scoreline back in step with what is left.</summary>
    public Task<Result> RemoveGoalAsync(int gameId, int goalId) =>
        ServiceOperation.RunAsync(logger, "remove the goal", async () =>
        {
            var removed = await games.RemoveGoalAsync(goalId);
            if (removed.IsFailure) return removed;

            await SyncScoreAsync(gameId);
            notifier.Notify(gameId);
            return Result.Success();
        });

    /// <summary>
    /// Brings <paramref name="playerOnId"/> on for <paramref name="playerOffId"/> in the period
    /// currently being played: the outgoing player's slot and position change hands, and the swap
    /// is recorded with the minute it happened.
    /// </summary>
    public Task<Result<GameSubstitution>> SubstituteAsync(int gameId, int playerOffId, int playerOnId) =>
        ServiceOperation.RunAsync(logger, "make the substitution", async () =>
        {
            if (playerOffId == playerOnId)
                return Result.Failure<GameSubstitution>("A player cannot be substituted for themselves");

            var game = await LoadWithPeriodsAsync(gameId);
            if (game is null) return Result.Failure<GameSubstitution>($"Game with ID {gameId} not found");

            var period = CurrentPeriod(game);
            if (period is null)
                return Result.Failure<GameSubstitution>("No period is currently being played");

            await db.Entry(period).Collection(p => p.PlayerPositions).LoadAsync();

            var off = period.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerOffId);
            if (off is null || off.IsSubstitute)
                return Result.Failure<GameSubstitution>("That player is not on the pitch");

            var slot = off.SlotIndex;
            var position = off.Position;

            off.SlotIndex = null;
            off.IsSubstitute = true;

            var on = period.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerOnId);
            if (on is null)
            {
                // Not benched for this period — someone who turned up late, or a lineup that was
                // never filled in. Adding them is friendlier than refusing the change mid-match.
                on = new GamePlayerPosition { GamePeriodId = period.Id, PlayerId = playerOnId };
                period.PlayerPositions.Add(on);
            }
            else if (!on.IsSubstitute)
            {
                return Result.Failure<GameSubstitution>("That player is already on the pitch");
            }

            on.SlotIndex = slot;
            on.Position = position;
            on.IsSubstitute = false;

            var sub = new GameSubstitution
            {
                GameId = gameId,
                GamePeriodId = period.Id,
                PlayerOffId = playerOffId,
                PlayerOnId = playerOnId,
                AtSeconds = game.ElapsedSecondsAt(DateTime.UtcNow),
                SlotIndex = slot,
                Position = position
            };
            db.GameSubstitutions.Add(sub);

            // One SaveChanges: the lineup change and the record of it must never diverge.
            await db.SaveChangesAsync();

            await db.Entry(sub).Reference(s => s.PlayerOff).LoadAsync();
            await db.Entry(sub).Reference(s => s.PlayerOn).LoadAsync();

            logger.LogInformation("Game {GameId}: {Off} off, {On} on at {Seconds}s in period {PeriodId}",
                gameId, playerOffId, playerOnId, sub.AtSeconds, period.Id);

            notifier.Notify(gameId);
            return Result.Success(sub);
        });

    /// <summary>
    /// Undoes a substitution. Only the most recent one in its period can go, because reversing an
    /// older swap would fight every change made on that slot since.
    /// </summary>
    public Task<Result> RemoveSubstitutionAsync(int subId) =>
        ServiceOperation.RunAsync(logger, "undo the substitution", async () =>
        {
            var sub = await db.GameSubstitutions.FindAsync(subId);
            if (sub is null) return Result.Failure("Substitution not found");

            var isNewest = !await db.GameSubstitutions
                .AnyAsync(s => s.GamePeriodId == sub.GamePeriodId && s.AtSeconds > sub.AtSeconds);
            if (!isNewest)
                return Result.Failure("Only the most recent substitution of a period can be undone");

            var positions = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == sub.GamePeriodId)
                .ToListAsync();

            var on = positions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOnId);
            var off = positions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOffId);

            if (on is not null)
            {
                on.SlotIndex = null;
                on.IsSubstitute = true;
            }

            if (off is not null)
            {
                off.SlotIndex = sub.SlotIndex;
                off.Position = sub.Position;
                off.IsSubstitute = false;
            }

            db.GameSubstitutions.Remove(sub);
            await db.SaveChangesAsync();

            logger.LogInformation("Undid substitution {SubId} in game {GameId}", subId, sub.GameId);
            notifier.Notify(sub.GameId);
            return Result.Success();
        });

    /// <summary>
    /// Moves the time run so far out of the anchor and into the banked total, leaving the clock
    /// stopped. Every state change calls this first so no seconds are lost or double-counted.
    /// </summary>
    private static void BankClock(Game game)
    {
        game.ClockAccumulatedSeconds = game.ElapsedSecondsAt(DateTime.UtcNow);
        game.ClockRunningSince = null;
    }

    private static GamePeriod? CurrentPeriod(Game game) =>
        game.LivePeriodId is null ? null : game.Periods.FirstOrDefault(p => p.Id == game.LivePeriodId);

    /// <summary>The first period that has not been kicked off yet, in playing order.</summary>
    private static GamePeriod? NextPeriod(Game game) =>
        game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault(p => p.StartedAtSeconds is null);

    private Task<Game?> LoadWithPeriodsAsync(int gameId) =>
        db.Games.Include(g => g.Periods).FirstOrDefaultAsync(g => g.Id == gameId);

    /// <summary>Rewrites the scoreline from the logged goals, so the live score is never guessed at.</summary>
    private async Task SyncScoreAsync(int gameId)
    {
        var game = await db.Games.FindAsync(gameId);
        if (game is null) return;

        var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync();
        game.ScoreHome = Game.CountOurGoals(goals);
        game.ScoreAway = Game.CountTheirGoals(goals);
        await db.SaveChangesAsync();
    }

    private Result<Game> Notified(Game game)
    {
        notifier.Notify(game.Id);
        return Result.Success(game);
    }

    private Result<Game> NotFound(int gameId)
    {
        logger.LogWarning("Live match {GameId} not found", gameId);
        return Result.Failure<Game>($"Game with ID {gameId} not found");
    }
}
