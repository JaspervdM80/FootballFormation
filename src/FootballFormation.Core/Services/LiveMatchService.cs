using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Runs a match as it happens: the clock, the period the team is currently playing, goals, and
/// substitutions. Goal storage itself is delegated to <see cref="GameService"/> — the only thing
/// added here is stamping the live minute and keeping the scoreline in step with the logged goals.
/// </summary>
public class LiveMatchService(
    IDbContextFactory<AppDbContext> dbFactory,
    GameService games,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<LiveMatchService> logger)
{
    /// <summary>
    /// The clock every match-time decision reads. Injected rather than taken straight from
    /// <see cref="DateTime.UtcNow"/> so the period and substitution arithmetic can be driven to an
    /// exact instant under test — it is the part of this service most likely to be silently wrong,
    /// and a season's statistics depend on it.
    /// </summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Everything the live screen renders, in one round trip: the periods with their lineups and
    /// players, the goals, and the substitutions with both players named.
    /// </summary>
    public Task<Result<Game>> GetLiveAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load live match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

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
                .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

            if (game is null)
            {
                logger.LogWarning("Live match {GameId} not found", gameId);
                return Result.Failure<Game>("Game with ID {0} not found", gameId);
            }

            return Result.Success(game);
        });

    /// <summary>
    /// The match the home page should point at, or null on an ordinary day. That is whatever is
    /// being played right now, and otherwise today's fixture — from the moment the day starts,
    /// through the match, until the final score is in — so match day is signposted all day rather
    /// than only between kick-off and the last whistle.
    /// </summary>
    public Task<Result<Game?>> GetTodaysMatchAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "find today's match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // A match in progress wins whatever the calendar says: it can have been kicked off
            // before midnight, and it is the one someone standing at a pitch is watching. Nothing
            // stops two being in progress at once, so the most recent by date wins.
            var game = (await db.Games
                .AsNoTracking()
                .Where(g => g.MatchState == MatchState.InProgress)
                .ToListAsync(cancellationToken))
                .NewestFirst()
                .FirstOrDefault();

            if (game is not null) return Result.Success<Game?>(game);

            // Games carry a date but no kick-off time, so "today" is the whole calendar day.
            // A double-header shows the one still to be played before the one already done.
            var today = time.GetLocalNow().Date;
            var tomorrow = today.AddDays(1);

            // The one date comparison left in SQL, so this does not read every game ever played on
            // each home-page hit. The tag is what says so out loud — see QueryTags.
            game = (await db.Games
                .AsNoTracking()
                .TagWith(QueryTags.ComparesDatesInSql)
                .Where(g => g.Date >= today && g.Date < tomorrow)
                .ToListAsync(cancellationToken))
                .OrderBy(g => g.MatchState == MatchState.Finished)
                .ThenBy(g => g.Date)
                .ThenBy(g => g.Id)
                .FirstOrDefault();

            return Result.Success(game);
        });

    public Task<Result<Game>> StartMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "start match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.NotStarted)
                return Result.Failure<Game>("This match has already been started");

            var first = game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
            if (first is null) return Result.Failure<Game>("This game has no periods to play");

            game.MatchState = MatchState.InProgress;
            game.ClockAccumulatedSeconds = 0;
            game.ClockRunningSince = UtcNow;
            game.LivePeriodId = first.Id;
            first.StartedAtSeconds = 0;
            first.EndedAtSeconds = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started live match {GameId} at period {PeriodId}", gameId, first.Id);
            return Notified(game);
        });

    public Task<Result<Game>> PauseClockAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "pause the clock", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (!game.IsClockRunning) return Result.Failure<Game>("The clock is not running");

            BankClock(game);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Paused clock for game {GameId} at {Seconds}s",
                gameId, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    public Task<Result<Game>> ResumeClockAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "resume the clock", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.IsClockRunning) return Result.Failure<Game>("The clock is already running");
            if (game.LivePeriodId is null)
                return Result.Failure<Game>("Start the next period before resuming the clock");

            game.ClockRunningSince = UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Resumed clock for game {GameId} at {Seconds}s",
                gameId, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    /// <summary>Whistles the current period off. The clock stops and no period is live until the next one starts.</summary>
    public Task<Result<Game>> EndPeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "end the period", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = CurrentPeriod(game);
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            BankClock(game);
            current.EndedAtSeconds = game.ClockAccumulatedSeconds;
            game.LivePeriodId = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Ended period {PeriodId} of game {GameId} at {Seconds}s",
                current.Id, gameId, current.EndedAtSeconds);
            return Notified(game);
        });

    public Task<Result<Game>> StartNextPeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "start the next period", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
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
            game.ClockRunningSince = UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started period {PeriodId} of game {GameId} at {Seconds}s",
                next.Id, gameId, next.StartedAtSeconds);
            return Notified(game);
        });

    /// <summary>
    /// Rolls straight from the current period into the next one without stopping the clock, for the
    /// quarter boundaries that are not a real break (see <see cref="PeriodTypeExtensions.IsFollowedByBreak"/>).
    /// The lineup changes over, the running time does not.
    /// </summary>
    public Task<Result<Game>> AdvancePeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "start the next period", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = CurrentPeriod(game);
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            var next = NextPeriod(game);
            if (next is null)
                return Result.Failure<Game>("Every period has been played — finish the match instead");

            // Both ends read the same instant, so no seconds fall between the two periods. The
            // clock anchor is deliberately left alone: it must keep running through the change.
            var elapsed = game.ElapsedSecondsAt(UtcNow);
            current.EndedAtSeconds = elapsed;
            next.StartedAtSeconds = elapsed;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Game {GameId} rolled from period {From} into {To} at {Seconds}s",
                gameId, current.Id, next.Id, elapsed);
            return Notified(game);
        });

    public Task<Result<Game>> FinishMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "finish the match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState == MatchState.NotStarted)
                return Result.Failure<Game>("This match has not been started");

            BankClock(game);

            var current = CurrentPeriod(game);
            if (current is not null) current.EndedAtSeconds = game.ClockAccumulatedSeconds;

            game.LivePeriodId = null;
            game.MatchState = MatchState.Finished;

            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
            game.ScoreHome = Game.CountOurGoals(goals);
            game.ScoreAway = Game.CountTheirGoals(goals);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Notified(game);
        });

    /// <param name="scorerId">Null for an opponent goal — we do not track their players.</param>
    public Task<Result<GameGoal>> LogGoalAsync(
        int gameId, int? scorerId, int? assisterId, bool isOwnGoal, bool isOpponentGoal,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "log the goal", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Periods included: the minute follows the scoreboard clock, which is measured from
            // the half being played rather than from kick-off.
            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return Result.Failure<GameGoal>("Game with ID {0} not found", gameId);

            if (scorerId is null && !isOpponentGoal)
                return Result.Failure<GameGoal>("A goal for us needs a scorer");

            var clock = MatchClockReport.Build(
                game, game.CurrentOrLastPeriod(), game.ElapsedSecondsAt(UtcNow));

            var goal = new GameGoal
            {
                GameId = gameId,
                ScorerId = scorerId,
                AssisterId = assisterId,
                // The minute the clock showed, so an over-running first half does not push every
                // second-half goal out by the overrun. Stoppage time counts on past the cap rather
                // than pinning several goals to the same minute.
                Minute = clock.Minute,
                IsOwnGoal = isOwnGoal,
                IsOpponentGoal = isOpponentGoal
            };

            var added = await games.AddGoalAsync(goal, cancellationToken);
            if (added.IsFailure) return added;

            await SyncScoreAsync(db, gameId, cancellationToken);
            notifier.Notify(gameId);
            return added;
        });

    /// <summary>Removes a goal and pulls the scoreline back in step with what is left.</summary>
    public Task<Result> RemoveGoalAsync(
        int gameId, int goalId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove the goal", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var removed = await games.RemoveGoalAsync(goalId, cancellationToken);
            if (removed.IsFailure) return removed;

            await SyncScoreAsync(db, gameId, cancellationToken);
            notifier.Notify(gameId);
            return Result.Success();
        });

    /// <summary>
    /// Brings <paramref name="playerOnId"/> on for <paramref name="playerOffId"/> in the period
    /// currently being played: the outgoing player's slot and position change hands, and the swap
    /// is recorded with the minute it happened.
    /// </summary>
    public Task<Result<GameSubstitution>> SubstituteAsync(
        int gameId, int playerOffId, int playerOnId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "make the substitution", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerOffId == playerOnId)
                return Result.Failure<GameSubstitution>("A player cannot be substituted for themselves");

            var game = await LoadWithPeriodsAsync(db, gameId, cancellationToken);
            if (game is null) return Result.Failure<GameSubstitution>("Game with ID {0} not found", gameId);

            var period = CurrentPeriod(game);
            if (period is null)
                return Result.Failure<GameSubstitution>("No period is currently being played");

            await db.Entry(period).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

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
                AtSeconds = game.ElapsedSecondsAt(UtcNow),
                // From this service's clock, not the entity initializer's wall-clock default —
                // otherwise a match driven to an exact instant under test still records the real
                // time here, and AtSeconds and RecordedAt describe different afternoons.
                RecordedAt = UtcNow,
                SlotIndex = slot,
                Position = position
            };
            db.GameSubstitutions.Add(sub);

            // One SaveChanges: the lineup change and the record of it must never diverge.
            await db.SaveChangesAsync(cancellationToken);

            await db.Entry(sub).Reference(s => s.PlayerOff).LoadAsync(cancellationToken);
            await db.Entry(sub).Reference(s => s.PlayerOn).LoadAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: {Off} off, {On} on at {Seconds}s in period {PeriodId}",
                gameId, playerOffId, playerOnId, sub.AtSeconds, period.Id);

            notifier.Notify(gameId);
            return Result.Success(sub);
        });

    /// <summary>
    /// Undoes a substitution. Only the most recent one in its period can go, because reversing an
    /// older swap would fight every change made on that slot since.
    /// </summary>
    public Task<Result> RemoveSubstitutionAsync(int subId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "undo the substitution", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var sub = await db.GameSubstitutions.FindAsync([subId], cancellationToken);
            if (sub is null) return Result.Failure("Substitution not found");

            var isNewest = !await db.GameSubstitutions
                .AnyAsync(s => s.GamePeriodId == sub.GamePeriodId && s.AtSeconds > sub.AtSeconds, cancellationToken);
            if (!isNewest)
                return Result.Failure("Only the most recent substitution of a period can be undone");

            var positions = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == sub.GamePeriodId)
                .ToListAsync(cancellationToken);

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
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Undid substitution {SubId} in game {GameId}", subId, sub.GameId);
            notifier.Notify(sub.GameId);
            return Result.Success();
        });

    /// <summary>
    /// Moves the time run so far out of the anchor and into the banked total, leaving the clock
    /// stopped. Every state change calls this first so no seconds are lost or double-counted.
    /// </summary>
    private void BankClock(Game game)
    {
        game.ClockAccumulatedSeconds = game.ElapsedSecondsAt(UtcNow);
        game.ClockRunningSince = null;
    }

    private static GamePeriod? CurrentPeriod(Game game) =>
        game.LivePeriodId is null ? null : game.Periods.FirstOrDefault(p => p.Id == game.LivePeriodId);

    /// <summary>The first period that has not been kicked off yet, in playing order.</summary>
    private static GamePeriod? NextPeriod(Game game) =>
        game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault(p => p.StartedAtSeconds is null);

    private static Task<Game?> LoadWithPeriodsAsync(
        AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.Include(g => g.Periods).FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

    /// <summary>Rewrites the scoreline from the logged goals, so the live score is never guessed at.</summary>
    private static async Task SyncScoreAsync(AppDbContext db, int gameId, CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([gameId], cancellationToken);
        if (game is null) return;

        var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
        game.ScoreHome = Game.CountOurGoals(goals);
        game.ScoreAway = Game.CountTheirGoals(goals);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Result<Game> Notified(Game game)
    {
        notifier.Notify(game.Id);
        return Result.Success(game);
    }

    private Result<Game> NotFound(int gameId)
    {
        logger.LogWarning("Live match {GameId} not found", gameId);
        return Result.Failure<Game>("Game with ID {0} not found", gameId);
    }
}
