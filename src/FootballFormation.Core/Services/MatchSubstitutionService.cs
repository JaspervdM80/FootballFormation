using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Who stands where once a match is under way: a substitution, where a slot and a position change
/// hands between the pitch and the bench and the swap is written down with the minute it happened;
/// a straight position swap between two players already on; and a player going off hurt, with or
/// without somebody to take her place.
/// <para>
/// The most intricate write in the app, and the reason it sits on its own: the lineup and the
/// record of the change go in with one <c>SaveChanges</c>, because every minutes report is built
/// from the two agreeing. It asks the clock only how far the game has run
/// (<see cref="Game.ElapsedSecondsAt"/>), so it stays independent of <see cref="MatchClockService"/>.
/// </para>
/// <para>
/// An injury lives here rather than in a service of its own because it is the same write: it takes
/// a player off the pitch, and half the time it brings one on.
/// </para>
/// </summary>
public class MatchSubstitutionService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchSubstitutionService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Brings <paramref name="playerOnId"/> on for <paramref name="playerOffId"/> in the half
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

            var half = game.LiveHalf();
            if (half is null)
                return Result.Failure<GameSubstitution>("No half is being played");

            await db.Entry(half).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var taken = TakeOffThePitch(half, playerOffId);
            if (taken.IsFailure) return taken.To<GameSubstitution>();

            var slot = taken.Value;
            var brought = BringOnThePitch(half, playerOnId, slot);
            if (brought.IsFailure) return brought.To<GameSubstitution>();

            var sub = new GameSubstitution
            {
                GameId = gameId,
                GamePeriodId = half.Id,
                PlayerOffId = playerOffId,
                PlayerOnId = playerOnId,
                AtSeconds = game.ElapsedSecondsAt(UtcNow),
                // From this service's clock, not the entity initializer's wall-clock default —
                // otherwise a match driven to an exact instant under test still records the real
                // time here, and AtSeconds and RecordedAt describe different afternoons.
                RecordedAt = UtcNow,
                SlotIndex = slot.Index,
                Position = slot.Position
            };
            db.GameSubstitutions.Add(sub);

            // One SaveChanges: the lineup change and the record of it must never diverge.
            await db.SaveChangesAsync(cancellationToken);

            await db.Entry(sub).Reference(s => s.PlayerOff).LoadAsync(cancellationToken);
            await db.Entry(sub).Reference(s => s.PlayerOn).LoadAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: {Off} off, {On} on at {Seconds}s in the {Half}",
                gameId, playerOffId, playerOnId, sub.AtSeconds, half.PeriodType.Half());

            return Result.Success(sub);
        });

    /// <summary>
    /// Swaps the pitch slots — and with them the positions — of two players who are both already
    /// on. Nobody enters or leaves the match, so this writes no <see cref="GameSubstitution"/>:
    /// the rows exist to say who was on the pitch when, and a shuffle changes none of that.
    /// <para>
    /// What it costs is the position half of the minutes report. <c>GameMinutesReport</c> reads the
    /// lineup as it finally stands and rewinds only substitution rows, so after a swap each player
    /// is credited the position they moved <em>into</em> for the whole half — including the
    /// minutes before the swap. Totals are unaffected; only the split by position is.
    /// </para>
    /// </summary>
    public Task<Result> SwapPositionsAsync(
        int gameId, int playerAId, int playerBId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "swap the positions",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerAId == playerBId)
                return Result.Failure<int>("A player cannot swap positions with themselves");

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<int>(gameId);

            var half = game.LiveHalf();
            if (half is null)
                return Result.Failure<int>("No half is being played");

            await db.Entry(half).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var a = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerAId);
            var b = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerBId);

            if (a is null || a.IsSubstitute || b is null || b.IsSubstitute)
                return Result.Failure<int>("Both players have to be on the pitch to swap positions");

            (a.SlotIndex, b.SlotIndex) = (b.SlotIndex, a.SlotIndex);
            (a.Position, b.Position) = (b.Position, a.Position);

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: {A} and {B} swapped positions in the {Half}",
                gameId, playerAId, playerBId, half.PeriodType.Half());

            return Result.Success(gameId);
        });

    /// <summary>
    /// Takes <paramref name="playerId"/> off the pitch hurt, in the half currently being played,
    /// and records the minute — which is what stops the rest of the match counting towards her
    /// availability (<see cref="Game.AvailableMinutesFor"/>).
    /// <para>
    /// <paramref name="replacementPlayerId"/> is optional: name someone and this is an ordinary
    /// substitution with an injury recorded beside it; leave it out and the team plays on a player
    /// short.
    /// </para>
    /// </summary>
    public Task<Result<GameInjury>> MarkInjuredAsync(
        int gameId, int playerId, int? replacementPlayerId = null,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "record the injury",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (replacementPlayerId == playerId)
                return Result.Failure<GameInjury>("A player cannot be substituted for themselves");

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameInjury>(gameId);

            var half = game.LiveHalf();
            if (half is null)
                return Result.Failure<GameInjury>("No half is being played");

            // Before the lineup is touched: the unique index refuses the second row anyway, but as
            // a constraint violation rather than as something to read on a phone.
            if (await db.GameInjuries.AnyAsync(
                    i => i.GameId == gameId && i.PlayerId == playerId, cancellationToken))
                return Result.Failure<GameInjury>("That player is already marked injured");

            await db.Entry(half).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var taken = TakeOffThePitch(half, playerId);
            if (taken.IsFailure) return taken.To<GameInjury>();

            var slot = taken.Value;
            var atSeconds = game.ElapsedSecondsAt(UtcNow);

            if (replacementPlayerId is { } replacementId)
            {
                var brought = BringOnThePitch(half, replacementId, slot);
                if (brought.IsFailure) return brought.To<GameInjury>();

                db.GameSubstitutions.Add(new GameSubstitution
                {
                    GameId = gameId,
                    GamePeriodId = half.Id,
                    PlayerOffId = playerId,
                    PlayerOnId = replacementId,
                    AtSeconds = atSeconds,
                    RecordedAt = UtcNow,
                    SlotIndex = slot.Index,
                    Position = slot.Position
                });
            }

            var injury = new GameInjury
            {
                GameId = gameId,
                GamePeriodId = half.Id,
                PlayerId = playerId,
                AtSeconds = atSeconds,
                RecordedAt = UtcNow,
                SlotIndex = slot.Index,
                Position = slot.Position
            };
            db.GameInjuries.Add(injury);

            // One SaveChanges, for the same reason a substitution's is: a lineup that says she is
            // off and no row saying why would credit her the whole half back on the next report.
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: {Player} off injured at {Seconds}s in the {Half}, {Replacement}",
                gameId, playerId, atSeconds, half.PeriodType.Half(),
                replacementPlayerId is { } on ? $"replaced by {on}" : "not replaced");

            return Result.Success(injury);
        });

    /// <summary>
    /// Undoes an injury: the record goes, and when nobody came on for her she goes back into the
    /// slot she left. A replaced injury is undone through its substitution instead, which holds the
    /// slot and takes the injury with it.
    /// </summary>
    public Task<Result> RemoveInjuryAsync(int injuryId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "undo the injury",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var injury = await db.GameInjuries.FindAsync([injuryId], cancellationToken);
            if (injury is null) return Result.Failure<int>("Injury not found");

            // The pairing Game.WasReplaced spells out: same half, same player, same second.
            var replaced = await db.GameSubstitutions.AnyAsync(
                s => s.GamePeriodId == injury.GamePeriodId
                     && s.PlayerOffId == injury.PlayerId
                     && s.AtSeconds == injury.AtSeconds,
                cancellationToken);

            if (!replaced)
            {
                var positions = await db.GamePlayerPositions
                    .Where(pp => pp.GamePeriodId == injury.GamePeriodId)
                    .ToListAsync(cancellationToken);

                // Nothing on this screen takes an empty slot over, but the formation screen can:
                // saving a line-up there is delete-and-reinsert, mid-match included.
                if (positions.Any(pp => !pp.IsSubstitute && pp.SlotIndex == injury.SlotIndex))
                    return Result.Failure<int>("Somebody else is in that place now");

                if (positions.FirstOrDefault(pp => pp.PlayerId == injury.PlayerId) is { } entry)
                {
                    entry.SlotIndex = injury.SlotIndex;
                    entry.Position = injury.Position;
                    entry.IsSubstitute = false;
                }
            }

            db.GameInjuries.Remove(injury);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Undid injury {InjuryId} in game {GameId}", injuryId, injury.GameId);
            return Result.Success(injury.GameId);
        });

    /// <summary>
    /// Undoes a substitution. Only the most recent one in its half can go, because reversing an
    /// older swap would fight every change made on that slot since.
    /// <para>
    /// An injury recorded for the player who came off goes with it — the two rows were one tap, and
    /// leaving the injury behind would clip her availability for a change that no longer happened.
    /// </para>
    /// </summary>
    public Task<Result> RemoveSubstitutionAsync(int subId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "undo the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var sub = await db.GameSubstitutions.FindAsync([subId], cancellationToken);
            if (sub is null) return Result.Failure<int>("Substitution not found");

            // A double substitution is two taps in a row, and AtSeconds is whole seconds, so the
            // two changes routinely share one. The id settles which came second; without it both
            // pass for "most recent" and undoing the earlier leaves two players in the same slot.
            var isNewest = !await db.GameSubstitutions
                .AnyAsync(s => s.GamePeriodId == sub.GamePeriodId
                               && (s.AtSeconds > sub.AtSeconds
                                   || (s.AtSeconds == sub.AtSeconds && s.Id > sub.Id)),
                          cancellationToken);
            if (!isNewest)
                return Result.Failure<int>("Only the most recent substitution of a half can be undone");

            var positions = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == sub.GamePeriodId)
                .ToListAsync(cancellationToken);

            var on = positions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOnId);
            var off = positions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOffId);

            // Where the incoming player is standing *now*, not where they came on. A position swap
            // moves a slot without writing a row here, so the slot this substitution recorded may
            // belong to somebody else by now — handing it back would put two players in it and
            // leave another empty. The recorded slot is only the fallback for a missing row.
            var slot = on?.SlotIndex ?? sub.SlotIndex;
            var position = on is { IsSubstitute: false } ? on.Position : sub.Position;

            if (on is not null)
            {
                on.SlotIndex = null;
                on.IsSubstitute = true;
            }

            if (off is not null)
            {
                off.SlotIndex = slot;
                off.Position = position;
                off.IsSubstitute = false;
            }

            var injury = await db.GameInjuries.FirstOrDefaultAsync(
                i => i.GamePeriodId == sub.GamePeriodId
                     && i.PlayerId == sub.PlayerOffId
                     && i.AtSeconds == sub.AtSeconds,
                cancellationToken);
            if (injury is not null) db.GameInjuries.Remove(injury);

            db.GameSubstitutions.Remove(sub);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Undid substitution {SubId} in game {GameId}", subId, sub.GameId);
            return Result.Success(sub.GameId);
        });

    /// <summary>The place a player was holding, handed to whoever takes it over.</summary>
    private readonly record struct PitchSlot(int? Index, PlayerPosition Position);

    /// <summary>
    /// Frees the place <paramref name="playerId"/> was standing in and benches her. Shared by the
    /// substitution and the injury, which differ only in whether anybody takes the place over.
    /// </summary>
    private static Result<PitchSlot> TakeOffThePitch(GamePeriod half, int playerId)
    {
        var off = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerId);
        if (off is null || off.IsSubstitute)
            return Result.Failure<PitchSlot>("That player is not on the pitch");

        var slot = new PitchSlot(off.SlotIndex, off.Position);

        off.SlotIndex = null;
        off.IsSubstitute = true;

        return Result.Success(slot);
    }

    private static Result BringOnThePitch(GamePeriod half, int playerId, PitchSlot slot)
    {
        var on = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == playerId);
        if (on is null)
        {
            // Not benched for this half — someone who turned up late, or a lineup that was never
            // filled in. Adding them is friendlier than refusing the change mid-match.
            on = new GamePlayerPosition { GamePeriodId = half.Id, PlayerId = playerId };
            half.PlayerPositions.Add(on);
        }
        else if (!on.IsSubstitute)
        {
            return Result.Failure("That player is already on the pitch");
        }

        on.SlotIndex = slot.Index;
        on.Position = slot.Position;
        on.IsSubstitute = false;

        return Result.Success();
    }
}
