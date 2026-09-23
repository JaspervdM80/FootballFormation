using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Services;

/// An injury lives here rather than in a service of its own because it is the same write: it takes a player off the pitch, and half the
/// time it brings one on. Only <see cref="Game.ElapsedSecondsAt"/> is needed from the clock, so this stays free of MatchClockService.
public class MatchSubstitutionService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchSubstitutionService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

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

    /// The break's substitution: the next half is being set up, not played, so this edits its line-up and records nothing — the change
    /// becomes a real <see cref="GameSubstitution"/> only when <c>StartNextHalfAsync</c> materialises the difference from the half just
    /// finished. Refused once that half has kicked off, when a change is a recorded substitution again.
    public Task<Result> PlanBreakSubstitutionAsync(
        int gameId, int playerOffId, int playerOnId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "change the half-time line-up",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerOffId == playerOnId)
                return Result.Failure<int>("A player cannot be substituted for themselves");

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<int>(gameId);

            if (game.LiveHalf() is not null)
                return Result.Failure<int>("End the current half first");

            var next = game.NextHalf();
            if (next is null)
                return Result.Failure<int>("No half is waiting to be set up");

            await db.Entry(next).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var taken = TakeOffThePitch(next, playerOffId);
            if (taken.IsFailure) return taken.To<int>();

            var brought = BringOnThePitch(next, playerOnId, taken.Value);
            if (brought.IsFailure) return brought.To<int>();

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: planned {On} on for {Off} in the {Half}",
                gameId, playerOnId, playerOffId, next.PeriodType.Half());

            return Result.Success(gameId);
        });

    /// Writes no <see cref="GameSubstitution"/> — nobody enters or leaves. The cost is that GameMinutesReport rewinds substitutions
    /// only, so after a swap each player is credited the position she moved into for the whole half. Totals are unaffected.
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

    /// The recorded minute is what stops the rest of the match counting towards her availability — see <see cref="Game.AvailableMinutesFor"/>.
    /// A null <paramref name="replacementPlayerId"/> means the team plays on a player short.
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

    /// Only restores the slot when nobody came on: a replaced injury is undone through its substitution, which takes the injury with it.
    public Task<Result> RemoveInjuryAsync(int injuryId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "undo the injury",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var injury = await db.GameInjuries.FindAsync([injuryId], cancellationToken);
            if (injury is null || !await db.GameInScopeAsync(injury.GameId, cancellationToken))
                return Result.Failure<int>("Injury not found");

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

                // Belt and braces: nothing takes the freed slot now that SavePeriodLineupAsync refuses a half already played.
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

    /// Any substitution can go, so long as the player it brought on is still on the pitch: while she is, undoing follows her to wherever she
    /// stands now and hands that slot back, no matter how many other changes came after on other slots. Once a later change has taken her
    /// off again, that later one has to be undone first, or the rewind would fight it. An injury recorded for the same player at the same
    /// second goes with the substitution — one tap wrote both.
    public Task<Result> RemoveSubstitutionAsync(int subId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "undo the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var sub = await db.GameSubstitutions.FindAsync([subId], cancellationToken);
            if (sub is null || !await db.GameInScopeAsync(sub.GameId, cancellationToken))
                return Result.Failure<int>("Substitution not found");

            var half = await db.GamePeriods
                .Include(p => p.PlayerPositions)
                .FirstOrDefaultAsync(p => p.Id == sub.GamePeriodId, cancellationToken);
            if (half is null) return Result.Failure<int>("Substitution not found");

            var reversed = ReverseLineup(half, sub);
            if (reversed.IsFailure) return reversed.To<int>();

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

    /// A substitution forgotten at the touchline, entered afterwards into a half already kicked off. Laid over the line-up as it stands now,
    /// so it is refused when either player takes part in a later change in that half — rewinding past it would put her in two places.
    public Task<Result<GameSubstitution>> AddSubstitutionAsync(
        int gameId, PeriodType half, int playerOffId, int playerOnId, int minute, bool injured,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "add the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerOffId == playerOnId)
                return Result.Failure<GameSubstitution>("A player cannot be substituted for themselves");

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameSubstitution>(gameId);

            if (game.PlayedHalf(half) is not { StartedAtSeconds: { } start } period)
                return Result.Failure<GameSubstitution>("That half was never played");

            await db.Entry(game).Collection(g => g.Substitutions).LoadAsync(cancellationToken);
            await db.Entry(game).Collection(g => g.Injuries).LoadAsync(cancellationToken);

            var end = period.EndedAtSeconds ?? game.ElapsedSecondsAt(UtcNow);
            var atSeconds = Math.Clamp(MatchClockReport.ElapsedForMinute(game, minute), start, Math.Max(start, end));

            var involved = new[] { playerOffId, playerOnId };
            if (game.Substitutions.Any(s => s.GamePeriodId == period.Id && s.AtSeconds > atSeconds
                                            && (involved.Contains(s.PlayerOffId) || involved.Contains(s.PlayerOnId)))
                || game.Injuries.Any(i => i.GamePeriodId == period.Id && i.AtSeconds > atSeconds && involved.Contains(i.PlayerId)))
                return Result.Failure<GameSubstitution>("Undo the later substitution first");

            if (injured && game.Injuries.Any(i => i.PlayerId == playerOffId))
                return Result.Failure<GameSubstitution>("That player is already marked injured");

            if (injured && await PlaysInALaterHalfAsync(db, game, period, playerOffId, cancellationToken))
                return Result.Failure<GameSubstitution>("She played on in a later half");

            await db.Entry(period).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var taken = TakeOffThePitch(period, playerOffId);
            if (taken.IsFailure) return taken.To<GameSubstitution>();

            var slot = taken.Value;
            var brought = BringOnThePitch(period, playerOnId, slot);
            if (brought.IsFailure) return brought.To<GameSubstitution>();

            var sub = new GameSubstitution
            {
                GameId = gameId,
                GamePeriodId = period.Id,
                PlayerOffId = playerOffId,
                PlayerOnId = playerOnId,
                AtSeconds = atSeconds,
                RecordedAt = UtcNow,
                SlotIndex = slot.Index,
                Position = slot.Position
            };
            db.GameSubstitutions.Add(sub);

            if (injured)
            {
                db.GameInjuries.Add(new GameInjury
                {
                    GameId = gameId,
                    GamePeriodId = period.Id,
                    PlayerId = playerOffId,
                    AtSeconds = atSeconds,
                    RecordedAt = UtcNow,
                    SlotIndex = slot.Index,
                    Position = slot.Position
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Game {GameId}: added {Off} off{Injured}, {On} on at {Seconds}s in the {Half}",
                gameId, playerOffId, injured ? " injured" : "", playerOnId, atSeconds, half.Half());

            return Result.Success(sub);
        });

    /// Reverses the change and lays the corrected one over it; refused once a later change has moved the incoming player. A paired injury
    /// moves with it, since Game.WasReplaced pairs the two on the same second.
    public Task<Result> EditSubstitutionAsync(
        int subId, int playerOffId, int playerOnId, int atSeconds, bool injured,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "edit the substitution",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (playerOffId == playerOnId)
                return Result.Failure<int>("A player cannot be substituted for themselves");

            var sub = await db.GameSubstitutions.FindAsync([subId], cancellationToken);
            if (sub is null || !await db.GameInScopeAsync(sub.GameId, cancellationToken))
                return Result.Failure<int>("Substitution not found");

            var game = await db.LoadWithPeriodsAsync(sub.GameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<int>(sub.GameId);

            await db.Entry(game).Collection(g => g.Injuries).LoadAsync(cancellationToken);
            var injury = game.InjuryFor(sub);
            if (injured && game.Injuries.Any(i => i.PlayerId == playerOffId && i != injury))
                return Result.Failure<int>("That player is already marked injured");

            var half = game.Periods.FirstOrDefault(p => p.Id == sub.GamePeriodId);
            if (half is null) return Result.Failure<int>("Substitution not found");

            if (injured && await PlaysInALaterHalfAsync(db, game, half, playerOffId, cancellationToken))
                return Result.Failure<int>("She played on in a later half");

            await db.Entry(half).Collection(p => p.PlayerPositions).LoadAsync(cancellationToken);

            var reversed = ReverseLineup(half, sub);
            if (reversed.IsFailure) return reversed.To<int>();

            var taken = TakeOffThePitch(half, playerOffId);
            if (taken.IsFailure) return taken.To<int>();

            var slot = taken.Value;
            var brought = BringOnThePitch(half, playerOnId, slot);
            if (brought.IsFailure) return brought.To<int>();

            // Kept inside its own half: an edited minute past the end of the period — the whistle, or the live clock — would credit playing
            // time that was never on it, and reads on the timeline as a change to a half it was never part of.
            var start = half.StartedAtSeconds ?? 0;
            var end = half.EndedAtSeconds ?? game.ElapsedSecondsAt(UtcNow);
            sub.AtSeconds = Math.Clamp(atSeconds, start, Math.Max(start, end));
            sub.PlayerOffId = playerOffId;
            sub.PlayerOnId = playerOnId;
            sub.SlotIndex = slot.Index;
            sub.Position = slot.Position;

            if (!injured && injury is not null)
            {
                db.GameInjuries.Remove(injury);
            }
            else if (injured)
            {
                if (injury is null)
                {
                    injury = new GameInjury { GameId = sub.GameId, RecordedAt = UtcNow };
                    db.GameInjuries.Add(injury);
                }

                injury.GamePeriodId = sub.GamePeriodId;
                injury.PlayerId = playerOffId;
                injury.AtSeconds = sub.AtSeconds;
                injury.SlotIndex = slot.Index;
                injury.Position = slot.Position;
            }

            await db.SaveChangesAsync(cancellationToken);

            await db.Entry(sub).Reference(s => s.PlayerOff).LoadAsync(cancellationToken);
            await db.Entry(sub).Reference(s => s.PlayerOn).LoadAsync(cancellationToken);

            logger.LogInformation("Edited substitution {SubId} in game {GameId}: {Off} off{Injured}, {On} on at {Seconds}s",
                subId, sub.GameId, playerOffId, injured ? " injured" : "", playerOnId, sub.AtSeconds);
            return Result.Success(sub.GameId);
        });

    /// Puts the line-up back to before a substitution: benches whoever came on, from wherever she now stands — a later position swap can
    /// have moved her, and the slot she holds now is the one to hand back — and restores the player who came off to it. Refused unless both
    /// still stand where this substitution left them — she on the pitch, he off it — because otherwise a later change moved one of them and
    /// reversing this one would relocate her, or seat two players in one slot.
    private static Result ReverseLineup(GamePeriod half, GameSubstitution sub)
    {
        var on = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOnId);
        var off = half.PlayerPositions.FirstOrDefault(pp => pp.PlayerId == sub.PlayerOffId);

        if (on is null || on.IsSubstitute || off is { IsSubstitute: false })
            return Result.Failure("Undo the later substitution first");

        var slot = new PitchSlot(on.SlotIndex, on.Position);
        on.SlotIndex = null;
        on.IsSubstitute = true;

        if (off is not null)
        {
            off.SlotIndex = slot.Index;
            off.Position = slot.Position;
            off.IsSubstitute = false;
        }

        return Result.Success();
    }

    /// An injury ends her availability, so time played after it would read as over 100% utilisation. Being taken off at a later half's
    /// restart is the break's change, not playing on.
    private static async Task<bool> PlaysInALaterHalfAsync(
        AppDbContext db, Game game, GamePeriod half, int playerId, CancellationToken cancellationToken)
    {
        var later = game.Periods
            .Where(p => p.StartedAtSeconds > half.StartedAtSeconds)
            .ToDictionary(p => p.Id, p => p.StartedAtSeconds!.Value);
        if (later.Count == 0) return false;

        var laterIds = later.Keys.ToList();
        if (await db.GamePlayerPositions.AnyAsync(
                pp => laterIds.Contains(pp.GamePeriodId) && pp.PlayerId == playerId && !pp.IsSubstitute, cancellationToken))
            return true;

        var laterSubs = await db.GameSubstitutions
            .Where(s => laterIds.Contains(s.GamePeriodId) && (s.PlayerOnId == playerId || s.PlayerOffId == playerId))
            .ToListAsync(cancellationToken);

        return laterSubs.Any(s => s.PlayerOnId == playerId || s.AtSeconds > later[s.GamePeriodId]);
    }

    private readonly record struct PitchSlot(int? Index, PlayerPosition Position);

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
