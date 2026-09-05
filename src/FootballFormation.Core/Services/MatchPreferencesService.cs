namespace FootballFormation.Core.Services;

/// What saving the preferences did to the season's training sessions, so the page can say so.
public record TrainingSync(int Created, int Removed)
{
    public bool IsEmpty => Created == 0 && Removed == 0;
}

public class MatchPreferencesService(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchPreferencesService> logger)
{
    /// Created on first read, inheriting the most recent earlier season's settings rather than the hardcoded ones — game length and
    /// formation usually carry over, which is what keeps per-season storage from costing the user work.
    public Task<Result<MatchPreferences>> GetAsync(int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load preferences", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (seasonId <= 0)
                return Result.Failure<MatchPreferences>("No season selected");

            var prefs = await db.MatchPreferences.FirstOrDefaultAsync(p => p.SeasonId == seasonId, cancellationToken);
            if (prefs is not null) return Result.Success(prefs);

            // No row yet, so the season is what says which team to seed one for — and confirms it is the scope's, since seeding a row for
            // another team's season would both leak it and trip the one-per-season index against that team's existing row.
            var teamId = await db.Seasons.Where(s => s.Id == seasonId).Select(s => (int?)s.TeamId).FirstOrDefaultAsync(cancellationToken);
            if (teamId is null)
                return Result.Failure<MatchPreferences>("Season not found");

            prefs = await SeedForAsync(db, seasonId, teamId.Value, cancellationToken);
            db.MatchPreferences.Add(prefs);

            // The one read in the app that writes, so the save deliberately drops the page's token: everything above gives up having
            // written nothing, but from here the row exists in memory and is worth the one insert it costs.
            await db.SaveChangesAsync(CancellationToken.None);

            logger.LogInformation("Created match preferences for season {SeasonId} (ID: {Id})", seasonId, prefs.Id);
            return Result.Success(prefs);
        });

    /// Writes the season's training sessions too: the training days and the period are the whole description of when the team trains, so
    /// saving them is what creates the evenings they add up to.
    public Task<Result<TrainingSync>> SaveAsync(MatchPreferences prefs, CancellationToken cancellationToken = default) =>
        // "save the preferences", not "save preferences": resx keys are case-insensitive, and /settings already has a "Save Preferences"
        // button that would collide. See docs/known_issues/localization.md.
        ServiceOperation.RunAdminAsync(currentUser, logger, "save the preferences", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == prefs.SeasonId, cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot save preferences for season {SeasonId}: not found", prefs.SeasonId);
                return Result.Failure<TrainingSync>("Season not found");
            }

            // Materialised first: Season.Contains is date-only in memory, and comparing a TEXT date in SQL is what QueryTags is about.
            var periodResult = ValidateTrainingPeriod(prefs, season);
            if (periodResult.IsFailure) return periodResult.To<TrainingSync>();

            db.MatchPreferences.Update(prefs);
            var sync = await SyncTrainingsAsync(db, prefs, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved match preferences for season {SeasonId}: {Duration}min, {Split}, {Formation}, {MatchDay}, training {TrainingDays} "
                + "from {FirstTraining} to {LastTraining}, {Created} sessions created and {Removed} removed",
                prefs.SeasonId, prefs.GameDurationMinutes, prefs.DefaultSplitType, prefs.DefaultFormation, prefs.MatchDay,
                prefs.TrainingDays, prefs.FirstTrainingDate, prefs.LastTrainingDate, sync.Created, sync.Removed);
            return Result.Success(sync);
        });

    /// Both ends or no schedule: an open end means "the season's own window" everywhere else, and a session for every training day until
    /// the end of June is not what ticking a weekday asks for. Clearing either end therefore takes the generated evenings back out, which
    /// is the undo.
    private static async Task<TrainingSync> SyncTrainingsAsync(
        AppDbContext db, MatchPreferences prefs, CancellationToken cancellationToken)
    {
        // Only when the schedule itself moved. Re-running the diff on a save that changed the game length would re-create the evenings
        // the admin has since deleted, so an unrelated preference would quietly rewrite the training calendar.
        var stored = await db.MatchPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SeasonId == prefs.SeasonId, cancellationToken);
        if (stored is not null && DescribesTheSameSchedule(stored, prefs)) return new TrainingSync(0, 0);

        var scheduled = prefs.FirstTrainingDate is { } first && prefs.LastTrainingDate is { } last
            ? TrainingSchedule.DatesIn(first, last, prefs.TrainingDays)
            : [];

        // The rows, then compared in memory: a date filter in SQL would compare the TEXT they are stored as — see QueryTags.
        var existing = await db.Trainings
            .Where(t => t.SeasonId == prefs.SeasonId)
            .ToListAsync(cancellationToken);

        var alreadyEntered = existing.Select(t => t.Date.Date).ToHashSet();
        var created = scheduled
            .Where(date => !alreadyEntered.Contains(date))
            .Select(date => new Training { Date = date, SeasonId = prefs.SeasonId, TeamId = prefs.TeamId, FromSchedule = true })
            .ToList();

        // An evening with absences, a note or a cancellation on it outlives the window it was drawn from, and is the admin's to delete.
        var removed = existing
            .Where(t => t.IsUnusedSchedule && !scheduled.Contains(t.Date.Date))
            .ToList();

        db.Trainings.AddRange(created);
        db.Trainings.RemoveRange(removed);

        return new TrainingSync(created.Count, removed.Count);
    }

    /// Date-only and order-free, because neither a time nor the order the days were ticked in changes an evening the schedule produces.
    private static bool DescribesTheSameSchedule(MatchPreferences stored, MatchPreferences prefs) =>
        stored.FirstTrainingDate?.Date == prefs.FirstTrainingDate?.Date
        && stored.LastTrainingDate?.Date == prefs.LastTrainingDate?.Date
        && stored.TrainingDays.Order().SequenceEqual(prefs.TrainingDays.Order());

    /// Either end may be null — that is "the season's own window", which is what every row held before the period existed.
    private static Result ValidateTrainingPeriod(MatchPreferences prefs, Season season)
    {
        if (prefs.FirstTrainingDate is { } first && prefs.LastTrainingDate is { } last && last.Date < first.Date)
            return Result.Failure("The last training must not be before the first");

        foreach (var date in new[] { prefs.FirstTrainingDate, prefs.LastTrainingDate })
        {
            if (date is { } value && !season.Contains(value))
                return Result.Failure("The training period must fall inside season {0}", season.Name);
        }

        return Result.Success();
    }

    /// Kept inside the season's own window: scheduling the opening fixture of a future season must not propose a date from this one.
    public Task<Result<DateTime>> GetNextMatchDateAsync(
        int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "calculate next match date", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var prefsResult = await GetAsync(seasonId, cancellationToken);
            if (prefsResult.IsFailure)
                return prefsResult.To<DateTime>();

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
            if (season is null)
                return Result.Failure<DateTime>("Season not found");

            // The dates alone, and the latest picked from them: see GameOrdering.
            var seasonDates = await db.Games
                .Where(g => g.SeasonId == seasonId)
                .Select(g => g.Date)
                .ToListAsync(cancellationToken);

            var matchDay = prefsResult.Value!.MatchDay;
            var (referenceDate, stepPastReference) = ReferenceDate(season.StartDate.Date, seasonDates);

            var nextDate = CalculateNextMatchDay(referenceDate, matchDay, stepPastReference);

            // A late-entered result in a season already over must not propose a date past its end, which would belong to the next one.
            if (nextDate > season.EndDate.Date)
                nextDate = LastMatchDayOnOrBefore(season.EndDate.Date, matchDay);

            logger.LogDebug("Next match date for season {SeasonId}: {NextDate} (match day: {MatchDay})",
                seasonId, nextDate.ToString("yyyy-MM-dd"), matchDay);
            return Result.Success(nextDate);
        });

    /// Kept inside the season's training period — <see cref="MatchPreferences.FirstTrainingDate"/> to
    /// <see cref="MatchPreferences.LastTrainingDate"/>, each falling back to the season's own window when unset, which is what stops a
    /// July date being proposed to a team that trains from August. Falls back to the reference date itself while no training days are
    /// chosen, since there is then no weekday to land on.
    public Task<Result<DateTime>> GetNextTrainingDateAsync(
        int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "calculate the next training date", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var prefsResult = await GetAsync(seasonId, cancellationToken);
            if (prefsResult.IsFailure)
                return prefsResult.To<DateTime>();

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
            if (season is null)
                return Result.Failure<DateTime>("Season not found");

            // The dates alone, and compared in memory: see TrainingOrdering.
            var seasonDates = await db.Trainings
                .Where(t => t.SeasonId == seasonId)
                .Select(t => t.Date)
                .ToListAsync(cancellationToken);

            var prefs = prefsResult.Value!;
            var trainingDays = prefs.TrainingDays;
            var windowStart = prefs.FirstTrainingDate?.Date ?? season.StartDate.Date;
            var windowEnd = prefs.LastTrainingDate?.Date ?? season.EndDate.Date;

            DateTime nextDate;
            if (trainingDays.Count == 0)
            {
                var (referenceDate, stepPastReference) = ReferenceDate(windowStart, seasonDates);
                nextDate = stepPastReference ? referenceDate.AddDays(1) : referenceDate;

                // Both ends, not just the far one: an extra evening entered outside the period is allowed, and while it is still ahead of
                // us it becomes the reference the next date steps off — which would carry the answer out of the period with it.
                if (nextDate < windowStart) nextDate = windowStart;
                if (nextDate > windowEnd) nextDate = windowEnd;
            }
            else
            {
                var today = time.GetLocalNow().Date;
                nextDate = NextFreeDayIn(
                    today < windowStart ? windowStart : today, windowEnd, trainingDays,
                    [.. seasonDates.Select(d => d.Date)]);
            }

            logger.LogDebug("Next training date for season {SeasonId}: {NextDate} (training days: {TrainingDays}, {Start} to {End})",
                seasonId, nextDate.ToString("yyyy-MM-dd"), trainingDays, windowStart, windowEnd);
            return Result.Success(nextDate);
        });

    /// Where to measure the next date from, and whether the answer has to step past it — true when the reference is an entry already
    /// scheduled, since two must not land on the same day; false when it is today, which is itself a valid answer.
    private (DateTime Reference, bool StepPast) ReferenceDate(DateTime windowStart, List<DateTime> seasonDates)
    {
        var today = time.GetLocalNow().Date;
        var latest = seasonDates.Count > 0 ? seasonDates.Max().Date : (DateTime?)null;

        // Only step off the last entry while it is still ahead of us — a run entered in advance. Measuring from one already behind us
        // would open the dialog months back.
        var latestIsUpcoming = latest is not null && latest >= today;
        var reference = latestIsUpcoming ? latest!.Value : today;

        // A window we are not inside yet has no useful "today", so measure from its opening day.
        if (!latestIsUpcoming && reference < windowStart)
            reference = windowStart;

        return (reference, latestIsUpcoming);
    }

    /// The soonest of <paramref name="days"/> from <paramref name="from"/> that has no session on it yet, and never past the end of the
    /// period. Not "the day after the last one entered": with the period generated in full, that answers with its closing evening.
    /// <paramref name="days"/> must not be empty.
    ///
    /// Once every one is taken — the ordinary state after the period has generated them — it answers with the soonest that has a session
    /// instead, which is the next evening the team actually trains and what /settings names. Falling through to the last day of the
    /// period would put a date months out both on that caption and in the dialog, where accepting it writes a duplicate.
    private static DateTime NextFreeDayIn(
        DateTime from, DateTime windowEnd, List<DayOfWeek> days, HashSet<DateTime> alreadyEntered)
    {
        DateTime? soonestTaken = null;

        for (var date = from; date <= windowEnd; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek)) continue;
            if (!alreadyEntered.Contains(date)) return date;

            soonestTaken ??= date;
        }

        // Nothing left in the window at all: the period is behind us, and the clamp is the same one the match date uses.
        return soonestTaken ?? LastDayInOnOrBefore(windowEnd, days);
    }

    /// The latest of <paramref name="days"/> falling on or before <paramref name="date"/>. <paramref name="days"/> must not be empty.
    private static DateTime LastDayInOnOrBefore(DateTime date, List<DayOfWeek> days) =>
        Enumerable.Range(0, 7)
            .Select(offset => date.AddDays(-offset))
            .First(candidate => days.Contains(candidate.DayOfWeek));

    /// The latest <paramref name="matchDay"/> falling on or before <paramref name="date"/>.
    private static DateTime LastMatchDayOnOrBefore(DateTime date, DayOfWeek matchDay) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)matchDay + 7) % 7));

    /// Copied from the newest season before this one that has a row; then any season's newest; then the model's own defaults.
    private static async Task<MatchPreferences> SeedForAsync(
        AppDbContext db, int seasonId, int teamId, CancellationToken cancellationToken)
    {
        var startDate = await db.Seasons
            .Where(s => s.Id == seasonId)
            .Select(s => (DateTime?)s.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        // One preferences row per season, so this is a handful of rows. See SeasonOrdering.
        var byNewestSeason = (await db.MatchPreferences
            .Include(p => p.Season)
            .ToListAsync(cancellationToken))
            .Where(p => p.Season is not null)
            .OrderByDescending(p => p.Season!.StartDate)
            .ThenBy(p => p.SeasonId)
            .ToList();

        // A season with no start date has nothing to be "before", so it falls through to the newest row of any season.
        var source = byNewestSeason
            .FirstOrDefault(p => startDate is not null && p.Season!.StartDate.Date < startDate.Value.Date)
            ?? byNewestSeason.FirstOrDefault();

        return source?.CopyFor(seasonId, teamId) ?? new MatchPreferences { SeasonId = seasonId, TeamId = teamId };
    }

    /// <paramref name="stepPastReference"/> is true when the reference is a match already scheduled, since two games must not land on the
    /// same day; false when it is today, which is itself a valid answer.
    private static DateTime CalculateNextMatchDay(DateTime referenceDate, DayOfWeek matchDay, bool stepPastReference)
    {
        var startDate = stepPastReference ? referenceDate.AddDays(1) : referenceDate;
        var daysUntil = ((int)matchDay - (int)startDate.DayOfWeek + 7) % 7;

        return startDate.AddDays(daysUntil);
    }
}
