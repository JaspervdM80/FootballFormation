using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// The clock and the run of play: kick-off, the period changes and the final whistle.
/// <para>
/// There is no pause: the clock runs from kick-off until the period is whistled off, and only a
/// period boundary stops it. A youth match is not paused at the touchline, and a clock that could
/// be stopped by a stray tap is a clock the season's minutes cannot be trusted from.
/// </para>
/// <para>
/// This is where the arithmetic a season's statistics are built on lives — the banked seconds and
/// the started/ended marks on each period are what <c>GameMinutesReport</c> later credits players
/// with — so it is the piece of the live match kept on its own and driven to exact instants under
/// test.
/// </para>
/// </summary>
public class MatchClockService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchClockService> logger)
{
    /// <summary>
    /// The clock every match-time decision reads. Injected rather than taken straight from
    /// <see cref="DateTime.UtcNow"/> so the period arithmetic can be driven to an exact instant
    /// under test — it is the part of the live match most likely to be silently wrong, and a
    /// season's statistics depend on it.
    /// </summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public Task<Result<Game>> StartMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start match",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
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
            return Result.Success(game);
        });

    /// <summary>Whistles the current period off. The clock stops and no period is live until the next one starts.</summary>
    public Task<Result<Game>> EndPeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "end the period",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = game.LivePeriod();
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            BankClock(game);
            current.EndedAtSeconds = game.ClockAccumulatedSeconds;
            game.LivePeriodId = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Ended period {PeriodId} of game {GameId} at {Seconds}s",
                current.Id, gameId, current.EndedAtSeconds);
            return Result.Success(game);
        });

    public Task<Result<Game>> StartNextPeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the next period",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.LivePeriodId is not null)
                return Result.Failure<Game>("End the current period first");

            var next = game.NextPeriod();
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
            return Result.Success(game);
        });

    /// <summary>
    /// Rolls straight from the current period into the next one without stopping the clock, for the
    /// quarter boundaries that are not a real break (see <see cref="PeriodTypeExtensions.IsFollowedByBreak"/>).
    /// The lineup changes over, the running time does not — minus the swaps play has already
    /// answered, which <see cref="KeepLiveArrivalsOn"/> drops.
    /// </summary>
    public Task<Result<Game>> AdvancePeriodAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the next period",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = game.LivePeriod();
            if (current is null) return Result.Failure<Game>("No period is currently being played");

            var next = game.NextPeriod();
            if (next is null)
                return Result.Failure<Game>("Every period has been played — finish the match instead");

            await db.Entry(current).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);
            await db.Entry(next).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);
            var liveChanges = await db.GameSubstitutions
                .Where(s => s.GamePeriodId == current.Id)
                .ToListAsync(cancellationToken);

            KeepLiveArrivalsOn(current, next, liveChanges);

            // Both ends read the same instant, so no seconds fall between the two periods. The
            // clock anchor is deliberately left alone: it must keep running through the change.
            var elapsed = game.ElapsedSecondsAt(UtcNow);
            current.EndedAtSeconds = elapsed;
            next.StartedAtSeconds = elapsed;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;

            // Nothing this app still does can stop a live period's clock, but a row stored by a
            // build that had a pause button can be in exactly that state — and rolling on to the
            // next line-up while the clock stayed frozen would bank no minutes for the rest of the
            // half. Restarting the anchor from the banked total is what "the clock keeps running"
            // means, and it is a no-op for every game that was not left paused.
            game.ClockRunningSince ??= UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Game {GameId} rolled from period {From} into {To} at {Seconds}s",
                gameId, current.Id, next.Id, elapsed);
            return Result.Success(game);
        });

    /// <summary>
    /// Keeps the players brought on during the period that is ending on the pitch for the next one.
    /// <para>
    /// The line-up for <paramref name="next"/> was written before the match. Where play has already
    /// answered one of its swaps — the player it takes off went off live, and somebody came on for
    /// them — carrying it out would pull that substitute straight back off for an arrival nobody is
    /// waiting for, and an injury replacement would last exactly one quarter. So the swap is
    /// dropped: the player who came on takes the place the plan's arrival was to have, and that
    /// arrival goes to the bench.
    /// </para>
    /// <para>
    /// Which swaps those are is <see cref="PlannedChangesReport"/>'s answer, not a second opinion
    /// formed here — the live screen lists exactly the ones it does not drop, directly above the
    /// button that calls this, and a card promising one thing while the button does another is
    /// worse than either behaviour on its own.
    /// </para>
    /// </summary>
    private static void KeepLiveArrivalsOn(
        GamePeriod current, GamePeriod next, List<GameSubstitution> liveChanges)
    {
        foreach (var swap in PlannedChangesReport.Swaps(current, next, liveChanges).Overtaken)
        {
            // Never null in this half of the split — see PlannedSwaps.
            var stayingOn = swap.Off!;

            // The slot the plan's arrival was taking, or the one the substitute already holds when
            // the next line-up names nobody for it.
            var slot = swap.On?.SlotIndex ?? stayingOn.SlotIndex;
            var position = swap.On?.Position ?? stayingOn.Position;

            foreach (var displaced in next.PlayerPositions
                .Where(p => !p.IsSubstitute && p.SlotIndex == slot).ToList())
            {
                displaced.SlotIndex = null;
                displaced.IsSubstitute = true;
            }

            var entry = next.PlayerPositions.FirstOrDefault(p => p.PlayerId == stayingOn.PlayerId);
            if (entry is null)
            {
                // Someone who was not in the next line-up at all — a late arrival, or a bench that
                // was only filled in for the first quarter.
                entry = new GamePlayerPosition { GamePeriodId = next.Id, PlayerId = stayingOn.PlayerId };
                next.PlayerPositions.Add(entry);
            }

            entry.SlotIndex = slot;
            entry.Position = position;
            entry.IsSubstitute = false;
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

            var current = game.LivePeriod();
            if (current is not null) current.EndedAtSeconds = game.ClockAccumulatedSeconds;

            game.LivePeriodId = null;
            game.MatchState = MatchState.Finished;

            // Recounted here rather than through MatchGoalService: this is the recount that settles
            // the game, and from here it counts towards the season.
            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
            game.CountScoreFrom(goals);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Result.Success(game);
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

    private Result<Game> NotFound(int gameId)
    {
        logger.LogWarning("Live match {GameId} not found", gameId);
        return LiveMatchQueries.GameNotFound<Game>(gameId);
    }
}
