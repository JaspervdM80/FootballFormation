namespace FootballFormation.Core.Services;

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

            prefs = await SeedForAsync(db, seasonId, cancellationToken);
            db.MatchPreferences.Add(prefs);

            // The one read in the app that writes, so the save deliberately drops the page's token: everything above gives up having
            // written nothing, but from here the row exists in memory and is worth the one insert it costs.
            await db.SaveChangesAsync(CancellationToken.None);

            logger.LogInformation("Created match preferences for season {SeasonId} (ID: {Id})", seasonId, prefs.Id);
            return Result.Success(prefs);
        });

    public Task<Result> SaveAsync(MatchPreferences prefs, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save preferences", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            db.MatchPreferences.Update(prefs);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Saved match preferences for season {SeasonId}: {Duration}min, {Split}, {Formation}, {MatchDay}",
                prefs.SeasonId, prefs.GameDurationMinutes, prefs.DefaultSplitType, prefs.DefaultFormation, prefs.MatchDay);
            return Result.Success();
        });

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
            var today = time.GetLocalNow().Date;
            var lastGame = seasonDates.Count > 0 ? seasonDates.Max().Date : (DateTime?)null;

            // Only step off the last game while it is still ahead of us — a run of fixtures entered in advance. Measuring from one
            // already behind us would open the dialog months back.
            var lastGameIsUpcoming = lastGame is not null && lastGame >= today;
            var referenceDate = lastGameIsUpcoming ? lastGame!.Value : today;

            // A season we are not in yet has no useful "today", so measure from its opening day.
            if (!lastGameIsUpcoming && referenceDate < season.StartDate.Date)
                referenceDate = season.StartDate.Date;

            var nextDate = CalculateNextMatchDay(referenceDate, matchDay, lastGameIsUpcoming);

            // A late-entered result in a season already over must not propose a date past its end, which would belong to the next one.
            if (nextDate > season.EndDate.Date)
                nextDate = LastMatchDayOnOrBefore(season.EndDate.Date, matchDay);

            logger.LogDebug("Next match date for season {SeasonId}: {NextDate} (match day: {MatchDay})",
                seasonId, nextDate.ToString("yyyy-MM-dd"), matchDay);
            return Result.Success(nextDate);
        });

    /// The latest <paramref name="matchDay"/> falling on or before <paramref name="date"/>.
    private static DateTime LastMatchDayOnOrBefore(DateTime date, DayOfWeek matchDay) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)matchDay + 7) % 7));

    /// Copied from the newest season before this one that has a row; then any season's newest; then the model's own defaults.
    private static async Task<MatchPreferences> SeedForAsync(
        AppDbContext db, int seasonId, CancellationToken cancellationToken)
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

        return source?.CopyFor(seasonId) ?? new MatchPreferences { SeasonId = seasonId };
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
