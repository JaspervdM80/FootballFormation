namespace FootballFormation.Core.Services;

public class SeasonService(
    IDbContextFactory<AppDbContext> dbFactory,
    IRawDbContextFactory rawDbFactory,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<SeasonService> logger)
{
    /// Newest first, and only the team in scope — the query filter does the scoping, and the season picker and the current-season
    /// fallbacks rely on the order.
    public Task<Result<List<Season>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load seasons", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var seasons = (await db.Seasons
                .AsNoTracking()
                .ToListAsync(cancellationToken))
                .NewestFirst();

            logger.LogDebug("Retrieved {Count} seasons", seasons.Count);
            return Result.Success(seasons);
        });

    /// The read-only sibling of <see cref="GetOrCreateForDateAsync"/>, for callers that must not create a season as a side effect — the
    /// game dialog reacting to a date the user may still cancel.
    public Task<Result<Season?>> FindForDateAsync(DateTime date, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "look up the season for that date", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var day = date.Date;

            // In memory so Season.Contains decides it — the one date-only definition of a window. See SeasonOrdering.
            var seasons = await db.Seasons.AsNoTracking().ToListAsync(cancellationToken);
            var season = seasons.NewestFirst().FirstOrDefault(s => s.Contains(day));

            return Result.Success(season);
        });

    /// Season windows are gapless, so this always resolves — creating one on the fly when a game is scheduled beyond those defined.
    public Task<Result<Season>> GetOrCreateForDateAsync(
        DateTime date, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "find the season for that date", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var day = date.Date;

            var lookup = await FindForDateAsync(day, cancellationToken);
            if (lookup.IsFailure) return lookup.To<Season>();
            if (lookup.Value is not null) return Result.Success(lookup.Value);

            var season = Season.CreateFor(day);
            season.TeamId = db.CurrentTeamId!.Value;

            // CreateFor always returns a full July–June window, so a date in a narrower gap would overlap its neighbours. Clamping means
            // auto-creation can only ever fill a hole, never straddle one. The neighbours are this team's — the filter scopes them.
            var existing = (await db.Seasons.AsNoTracking().ToListAsync(cancellationToken)).OldestFirst();

            var previous = existing.Where(s => s.EndDate.Date < day).MaxBy(s => s.EndDate.Date);

            if (previous is not null && previous.EndDate.Date >= season.StartDate.Date)
                season.StartDate = previous.EndDate.Date.AddDays(1);

            var following = existing.Where(s => s.StartDate.Date > day).MinBy(s => s.StartDate.Date);

            if (following is not null && following.StartDate.Date <= season.EndDate.Date)
                season.EndDate = following.StartDate.Date.AddDays(-1);

            db.Seasons.Add(season);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created season {SeasonName} for date {Date} (ID: {SeasonId})",
                season.Name, day.ToString("yyyy-MM-dd"), season.Id);
            return Result.Success(season);
        });

    public Task<Result<Season>> CreateAsync(Season season, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create season", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // The team in scope, not the caller's word for it: a season the admin cannot then see would be one they created for another team.
            season.TeamId = db.CurrentTeamId!.Value;

            var validation = await ValidateAsync(db, season, cancellationToken);
            if (validation.IsFailure) return validation.To<Season>();

            db.Seasons.Add(season);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created season {SeasonName} ({Start} - {End}) (ID: {SeasonId})",
                season.Name, season.StartDate.ToString("yyyy-MM-dd"),
                season.EndDate.ToString("yyyy-MM-dd"), season.Id);
            return Result.Success(season);
        });

    public Task<Result> UpdateAsync(Season season, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update season", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // A detached Update writes by key and never consults the query filter, so confirm the season is the scope's before it can
            // rewrite another team's dates. Setting TeamId back keeps the row on its team whatever the caller sent.
            if (!await db.Seasons.AnyAsync(s => s.Id == season.Id, cancellationToken))
                return SeasonNotInScope(season.Id);

            season.TeamId = db.CurrentTeamId!.Value;

            var validation = await ValidateAsync(db, season, cancellationToken);
            if (validation.IsFailure) return validation;

            db.Seasons.Update(season);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated season {SeasonName} (ID: {SeasonId})", season.Name, season.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        // "delete the season", not "delete season": resx keys are case-insensitive, and /settings already has a "Delete Season" confirm
        // that would collide. See docs/known_issues/localization.md.
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete the season", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // FirstOrDefault, not Find: Find bypasses the query filter, so it would fetch another team's season by id.
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot delete season {SeasonId}: not found", id);
                return Result.Failure("Season not found");
            }

            // The FK is Restrict, so refuse here rather than letting the caller hit a raw DbUpdateException.
            var gameCount = await db.Games.CountAsync(g => g.SeasonId == id, cancellationToken);
            if (gameCount > 0)
            {
                logger.LogWarning("Cannot delete season {SeasonName}: {Count} games still assigned",
                    season.Name, gameCount);
                return Result.Failure("Season {0} still has {1} games", season.Name, gameCount);
            }

            // Counted on the rows, not in SQL: IsUnusedSchedule is a computed property, and a season holds at most a few hundred.
            var trainings = await db.Trainings.Where(t => t.SeasonId == id).ToListAsync(cancellationToken);
            var recorded = trainings.Where(t => !t.IsUnusedSchedule).ToList();
            if (recorded.Count > 0)
            {
                logger.LogWarning("Cannot delete season {SeasonName}: {Count} trainings still assigned",
                    season.Name, recorded.Count);
                return Result.Failure("Season {0} still has {1} trainings", season.Name, recorded.Count);
            }

            if (season.IsCurrent)
            {
                logger.LogWarning("Cannot delete season {SeasonName}: it is the current season", season.Name);
                return Result.Failure("Season {0} is the current season", season.Name);
            }

            // The generated evenings nobody wrote anything on go with it. They are the schedule's rather than anybody's data, and
            // leaving them to the Restrict FK would make saving a training period the thing that locked the season in place.
            db.Trainings.RemoveRange(trainings);
            db.Seasons.Remove(season);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted season {SeasonName} (ID: {SeasonId}) and {Count} unused generated trainings",
                season.Name, season.Id, trainings.Count);
            return Result.Success();
        });

    public Task<Result> SetCurrentAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "switch season", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Only this team's seasons — the filter scopes the list, so the current-season flag is one row per team, not one per app.
            var seasons = await db.Seasons.ToListAsync(cancellationToken);
            var target = seasons.FirstOrDefault(s => s.Id == id);

            if (target is null)
            {
                logger.LogWarning("Cannot set current season {SeasonId}: not found", id);
                return Result.Failure("Season not found");
            }

            // One SaveChanges, so "exactly one current season" is never briefly broken.
            foreach (var season in seasons) season.IsCurrent = season.Id == id;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Current season set to {SeasonName} (ID: {SeasonId})", target.Name, target.Id);
            return Result.Success();
        });

    /// A repair for databases written before ValidateAsync checked for gaps, not a rule: it only ever moves a start date earlier, and
    /// never changes which season a game belongs to, since <see cref="Game.SeasonId"/> lives on the game. Scoped to the team in scope.
    public Task<Result<int>> CloseSeasonGapsAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "close season gaps", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Result.Success(await CloseGapsInAsync(db, cancellationToken));
        });

    /// The boot form: every team's gaps, each closed within its own chain of seasons. Two teams sharing a calendar is fine; two teams
    /// with different windows must not have the space between them read as one team's gap.
    public Task<Result<int>> CloseSeasonGapsForEveryTeamAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "close season gaps", cancellationToken, async () =>
        {
            var closed = 0;
            foreach (var teamId in await AllTeamIdsAsync(cancellationToken))
            {
                await using var db = StampedContextFor(teamId);
                closed += await CloseGapsInAsync(db, cancellationToken);
            }

            return Result.Success(closed);
        });

    /// Runs every boot so a fresh install — whose migration backfill found no games to derive seasons from — still has one to fall back
    /// on, for every team, not only the one the boot scope happens to resolve.
    public Task<Result<int>> EnsureEveryTeamHasCurrentSeasonAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "prepare seasons", cancellationToken, async () =>
        {
            var teamIds = await AllTeamIdsAsync(cancellationToken);
            foreach (var teamId in teamIds)
            {
                await using var db = StampedContextFor(teamId);
                await EnsureCurrentInAsync(db, cancellationToken);
            }

            return Result.Success(teamIds.Count);
        });

    /// The team-in-scope form, for tests and any caller that already has one team's context.
    public Task<Result<Season>> EnsureCurrentSeasonAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "prepare seasons", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Result.Success(await EnsureCurrentInAsync(db, cancellationToken));
        });

    private async Task<int> CloseGapsInAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var seasons = (await db.Seasons.ToListAsync(cancellationToken)).OldestFirst();
        var closed = 0;

        for (int i = 1; i < seasons.Count; i++)
        {
            var previous = seasons[i - 1];
            var season = seasons[i];
            var expectedStart = previous.EndDate.Date.AddDays(1);

            // Only a genuine gap. An overlap is a different problem and is left well alone.
            if (season.StartDate.Date <= expectedStart) continue;

            logger.LogWarning(
                "Closing gap before season {SeasonName}: start moved from {Old} to {New}",
                season.Name, season.StartDate.ToString("yyyy-MM-dd"), expectedStart.ToString("yyyy-MM-dd"));

            season.StartDate = expectedStart;
            closed++;
        }

        if (closed > 0) await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    private async Task<Season> EnsureCurrentInAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var current = await db.Seasons.FirstOrDefaultAsync(s => s.IsCurrent, cancellationToken);
        if (current is not null) return current;

        var newest = (await db.Seasons.ToListAsync(cancellationToken)).NewestFirst().FirstOrDefault();
        if (newest is not null)
        {
            newest.IsCurrent = true;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Marked season {SeasonName} as current (ID: {SeasonId})", newest.Name, newest.Id);
            return newest;
        }

        var season = Season.CreateFor(time.GetLocalNow().Date);
        season.TeamId = db.CurrentTeamId!.Value;
        season.IsCurrent = true;
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded first season {SeasonName} (ID: {SeasonId})", season.Name, season.Id);
        return season;
    }

    /// A raw context stamped to one team by hand — the boot loops have no cookie to resolve a current team from, so they name each one.
    private AppDbContext StampedContextFor(int teamId)
    {
        var db = rawDbFactory.CreateDbContext();
        db.CurrentTeamId = teamId;
        return db;
    }

    private async Task<List<int>> AllTeamIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = rawDbFactory.CreateDbContext();
        return await db.Teams.AsNoTracking().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(cancellationToken);
    }

    private Result SeasonNotInScope(int id)
    {
        logger.LogWarning("Cannot update season {SeasonId}: not in scope", id);
        return Result.Failure("Season not found");
    }

    /// Rules the dialog deliberately does not enforce, so any caller gets them.
    private async Task<Result> ValidateAsync(AppDbContext db, Season season, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(season.Name))
        {
            logger.LogWarning("Rejected season {SeasonId}: name is empty", season.Id);
            return Result.Failure("Season name is required");
        }

        if (season.EndDate.Date <= season.StartDate.Date)
        {
            logger.LogWarning("Rejected season {SeasonName}: end date is not after start date", season.Name);
            return Result.Failure("The end date must be after the start date");
        }

        // AsNoTracking so validating a season the caller is about to Update() cannot pull a second instance of the row into the change
        // tracker. Compared in memory — see SeasonOrdering. The filter scopes it to this team's other seasons.
        var others = (await db.Seasons
            .AsNoTracking()
            .Where(s => s.Id != season.Id)
            .ToListAsync(cancellationToken))
            .OldestFirst();

        var overlapping = others.FirstOrDefault(s =>
            s.StartDate.Date <= season.EndDate.Date && s.EndDate.Date >= season.StartDate.Date);

        if (overlapping is not null)
        {
            logger.LogWarning("Rejected season {SeasonName}: overlaps {OtherSeason}",
                season.Name, overlapping.Name);
            return Result.Failure("These dates overlap season {0}", overlapping.Name);
        }

        // A gap is as damaging as an overlap: every date must map to exactly one season, so a hole leaves the game dialog finding none
        // and offering an empty squad.
        var previous = others
            .Where(s => s.EndDate.Date < season.StartDate.Date)
            .MaxBy(s => s.EndDate.Date);

        if (previous is not null && previous.EndDate.Date.AddDays(1) != season.StartDate.Date)
        {
            var expected = previous.EndDate.Date.AddDays(1);
            logger.LogWarning("Rejected season {SeasonName}: leaves a gap after {OtherSeason}",
                season.Name, previous.Name);
            return Result.Failure(
                "This leaves a gap after season {0} — it should start on {1}",
                previous.Name, expected.ToString("dd-MM-yyyy"));
        }

        var following = others
            .Where(s => s.StartDate.Date > season.EndDate.Date)
            .MinBy(s => s.StartDate.Date);

        if (following is not null && following.StartDate.Date.AddDays(-1) != season.EndDate.Date)
        {
            var expected = following.StartDate.Date.AddDays(-1);
            logger.LogWarning("Rejected season {SeasonName}: leaves a gap before {OtherSeason}",
                season.Name, following.Name);
            return Result.Failure(
                "This leaves a gap before season {0} — it should end on {1}",
                following.Name, expected.ToString("dd-MM-yyyy"));
        }

        return Result.Success();
    }
}
