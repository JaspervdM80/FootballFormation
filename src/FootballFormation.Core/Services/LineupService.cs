namespace FootballFormation.Core.Services;

// The builder's planning: the shape, each period's line-up, and the earlier line-up it can start from.
public class LineupService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<LineupService> logger)
{
    /// Null when no earlier game this season has a line-up that would fit — see <see cref="Game.PreviousFullLineup"/>.
    public Task<Result<Game?>> GetPreviousFullLineupAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the previous line-up", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

            if (game is null) return GameNotFound.For<Game?>(logger, gameId);

            var seasonGames = await db.Games
                .AsNoTracking()
                .Where(g => g.SeasonId == game.SeasonId)
                .WithNamedLineups()
                .ToListAsync(cancellationToken);

            var previous = game.PreviousFullLineup(seasonGames);

            logger.LogDebug("Previous full line-up for game {GameId}: {PreviousGameId}", gameId, previous?.Id);
            return Result.Success(previous);
        });

    /// Everyone keeps the slot she was standing in, and her recorded position follows that slot into the new shape — the pitch reads the
    /// slot, but the playing-time table and the position statistics read what is stored on the entry. The line-ups are edited in place
    /// rather than taken from the caller, so a half already played keeps what the touchline recorded.
    public Task<Result> SaveFormationAsync(
        int gameId, FormationType formation, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "change the formation", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games
                .Include(g => g.Periods)
                .ThenInclude(p => p.PlayerPositions)
                // Reshaping reads a benched player's own position off her, and lazy loading is off.
                .ThenInclude(pp => pp.Player)
                .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

            if (game is null) return GameNotFound.For(logger, gameId);

            var slots = FormationSlots.For(formation);

            foreach (var period in game.Periods)
            {
                var from = FormationSlots.For(period.FormationTypeOverride ?? game.FormationType);

                // A smaller shape benches the starters it has no slot for, which on a half already played would take minutes off a
                // match that is over. That half keeps the shape it was played in, the way SaveLineupsAsync leaves its line-up alone.
                if (period.HasKickedOff && slots.Length < from.Length)
                {
                    period.FormationTypeOverride ??= game.FormationType;
                    continue;
                }

                FormationSlots.Reshape(period.PlayerPositions, from, slots);

                // The shape belongs to the whole game, so an override left on a period would quietly outrank the one just picked.
                period.FormationTypeOverride = null;
            }

            // Last: every line above reads it as the shape being left.
            game.FormationType = formation;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Changed formation to {Formation} for game {GameId}", formation, gameId);
            return Result.Success();
        });

    // One transaction, so a failure part-way leaves the plan as it was. A half already played is the touchline's record, and the page's
    // copy can be an hour stale, so it is skipped rather than replaced and handed back for the page to name.
    public Task<Result<List<PeriodType>>> SaveLineupsAsync(
        int gameId, IReadOnlyDictionary<int, List<GamePlayerPosition>> lineups, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save lineup", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games.WithPeriods().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
            if (game is null) return GameNotFound.For<List<PeriodType>>(logger, gameId);

            var periods = game.Periods.ToDictionary(p => p.Id);
            var strays = lineups.Keys.Where(id => !periods.ContainsKey(id)).ToList();
            if (strays.Count > 0)
            {
                logger.LogWarning("Cannot save lineups for game {GameId}: periods {PeriodIds} are not its own", gameId, strays);
                return Result.Failure<List<PeriodType>>("Period not found");
            }

            var played = lineups.Keys.Select(id => periods[id]).Where(p => p.HasKickedOff).ToList();
            var toSave = lineups.Where(entry => !periods[entry.Key].HasKickedOff).ToDictionary();

            await ReplaceLineupsAsync(db, toSave, cancellationToken);

            logger.LogInformation("Saved lineups for game {GameId}: {Saved} periods, {Played} already played and left alone",
                gameId, toSave.Count, played.Count);
            return Result.Success(played.Select(p => p.PeriodType).Order().ToList());
        });

    // Delete-then-insert needs every period or none, or a failed insert leaves a period with no line-up at all.
    private static async Task ReplaceLineupsAsync(
        AppDbContext db, IReadOnlyDictionary<int, List<GamePlayerPosition>> lineups, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var periodIds = lineups.Keys.ToList();
        var existing = await db.GamePlayerPositions
            .Where(pp => periodIds.Contains(pp.GamePeriodId))
            .ToListAsync(cancellationToken);

        db.GamePlayerPositions.RemoveRange(existing);
        await db.SaveChangesAsync(cancellationToken);

        // Fresh entities with Id = 0 — reusing tracked IDs trips the UNIQUE constraint.
        foreach (var (periodId, positions) in lineups)
        {
            foreach (var pos in positions)
            {
                db.GamePlayerPositions.Add(new GamePlayerPosition
                {
                    GamePeriodId = periodId,
                    PlayerId = pos.PlayerId,
                    Position = pos.Position,
                    SlotIndex = pos.IsSubstitute ? null : pos.SlotIndex,
                    IsSubstitute = pos.IsSubstitute
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
