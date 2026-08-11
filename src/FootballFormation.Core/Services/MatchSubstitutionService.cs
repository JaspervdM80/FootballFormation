using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Substitutions during a match: a slot and a position change hands between two players, and the
/// swap is written down with the minute it happened.
/// <para>
/// The most intricate write in the app, and the reason it sits on its own: the lineup and the
/// record of the change go in with one <c>SaveChanges</c>, because every minutes report is built
/// from the two agreeing. It asks the clock only how far the game has run
/// (<see cref="Game.ElapsedSecondsAt"/>), so it stays independent of <see cref="MatchClockService"/>.
/// </para>
/// </summary>
public class MatchSubstitutionService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchSubstitutionService> logger)
{
    /// <summary>The match clock — injected, for the reason <see cref="MatchClockService"/> gives.</summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Brings <paramref name="playerOnId"/> on for <paramref name="playerOffId"/> in the period
    /// currently being played: the outgoing player's slot and position change hands, and the swap
    /// is recorded with the minute it happened.
    /// </summary>
    public Task<Result<GameSubstitution>> SubstituteAsync(
        int gameId, int playerOffId, int playerOnId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "make the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerOffId == playerOnId)
                return Result.Failure<GameSubstitution>("A player cannot be substituted for themselves");

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameSubstitution>(gameId);

            var period = game.LivePeriod();
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

            return Result.Success(sub);
        });

    /// <summary>
    /// Undoes a substitution. Only the most recent one in its period can go, because reversing an
    /// older swap would fight every change made on that slot since.
    /// </summary>
    public Task<Result> RemoveSubstitutionAsync(int subId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "undo the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var sub = await db.GameSubstitutions.FindAsync([subId], cancellationToken);
            if (sub is null) return Result.Failure<int>("Substitution not found");

            var isNewest = !await db.GameSubstitutions
                .AnyAsync(s => s.GamePeriodId == sub.GamePeriodId && s.AtSeconds > sub.AtSeconds, cancellationToken);
            if (!isNewest)
                return Result.Failure<int>("Only the most recent substitution of a period can be undone");

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
            return Result.Success(sub.GameId);
        });
}
