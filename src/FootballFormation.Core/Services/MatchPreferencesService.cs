using FootballFormation.Core.Models;
using FootballFormation.Core.Data;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class MatchPreferencesService(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchPreferencesService> logger)
{
    /// <summary>
    /// The defaults for one season, created on first read. A brand-new season inherits the most
    /// recent earlier season's settings rather than the hardcoded ones — game length and formation
    /// usually carry over, so inheriting is what keeps per-season storage from costing the user work.
    /// </summary>
    public Task<Result<MatchPreferences>> GetAsync(int seasonId) =>
        ServiceOperation.RunAsync(logger, "load preferences", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (seasonId <= 0)
                return Result.Failure<MatchPreferences>("No season selected");

            var prefs = await db.MatchPreferences.FirstOrDefaultAsync(p => p.SeasonId == seasonId);
            if (prefs is not null) return Result.Success(prefs);

            prefs = await SeedForAsync(db, seasonId);
            db.MatchPreferences.Add(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Created match preferences for season {SeasonId} (ID: {Id})", seasonId, prefs.Id);
            return Result.Success(prefs);
        });

    public Task<Result> SaveAsync(MatchPreferences prefs) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save preferences", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            db.MatchPreferences.Update(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Saved match preferences for season {SeasonId}: {Duration}min, {Split}, {Formation}, {MatchDay}",
                prefs.SeasonId, prefs.GameDurationMinutes, prefs.DefaultSplitType, prefs.DefaultFormation, prefs.MatchDay);
            return Result.Success();
        });

    /// <summary>
    /// The next match date for <paramref name="seasonId"/>, on that season's match day. Only that
    /// season's games count, and the answer is kept inside its window — scheduling the opening
    /// fixture of a future season must not propose a date from the season we are living in.
    /// </summary>
    public Task<Result<DateTime>> GetNextMatchDateAsync(int seasonId) =>
        ServiceOperation.RunAsync(logger, "calculate next match date", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var prefsResult = await GetAsync(seasonId);
            if (prefsResult.IsFailure)
                return prefsResult.To<DateTime>();

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId);
            if (season is null)
                return Result.Failure<DateTime>("Season not found");

            // The dates alone, and the latest picked from them: see GameOrdering.
            var seasonDates = await db.Games
                .Where(g => g.SeasonId == seasonId)
                .Select(g => g.Date)
                .ToListAsync();

            var matchDay = prefsResult.Value!.MatchDay;
            var today = time.GetLocalNow().Date;
            var lastGame = seasonDates.Count > 0 ? seasonDates.Max().Date : (DateTime?)null;

            // Only step off the last game while it is still ahead of us — that is the case this
            // was written for, a run of fixtures entered in advance. Once the last game is behind
            // us, measuring from it proposes a date in the past (a season's final game would have
            // the dialog opening months back), so today becomes the reference instead.
            var lastGameIsUpcoming = lastGame is not null && lastGame >= today;
            var referenceDate = lastGameIsUpcoming ? lastGame!.Value : today;

            // A season we are not in yet has no useful "today", so measure from its opening day.
            if (!lastGameIsUpcoming && referenceDate < season.StartDate.Date)
                referenceDate = season.StartDate.Date;

            var nextDate = CalculateNextMatchDay(referenceDate, matchDay, lastGameIsUpcoming);

            // Filling in a season that is already over — a late-entered result, say — must not
            // propose a date past its end, which would belong to the next season. Fall back to the
            // last match day the season had.
            if (nextDate > season.EndDate.Date)
                nextDate = LastMatchDayOnOrBefore(season.EndDate.Date, matchDay);

            logger.LogDebug("Next match date for season {SeasonId}: {NextDate} (match day: {MatchDay})",
                seasonId, nextDate.ToString("yyyy-MM-dd"), matchDay);
            return Result.Success(nextDate);
        });

    /// <summary>The latest <paramref name="matchDay"/> falling on or before <paramref name="date"/>.</summary>
    private static DateTime LastMatchDayOnOrBefore(DateTime date, DayOfWeek matchDay) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)matchDay + 7) % 7));

    /// <summary>
    /// A fresh row for a season, copied from the newest season <em>before</em> it that has one.
    /// Falls back to the newest row of any season, then to the model's own defaults.
    /// </summary>
    private static async Task<MatchPreferences> SeedForAsync(AppDbContext db, int seasonId)
    {
        var startDate = await db.Seasons
            .Where(s => s.Id == seasonId)
            .Select(s => (DateTime?)s.StartDate)
            .FirstOrDefaultAsync();

        // One preferences row per season, so this is a handful of rows. See SeasonOrdering.
        var byNewestSeason = (await db.MatchPreferences
            .Include(p => p.Season)
            .ToListAsync())
            .Where(p => p.Season is not null)
            .OrderByDescending(p => p.Season!.StartDate)
            .ThenBy(p => p.SeasonId)
            .ToList();

        // A season with no start date of its own has nothing to be "before", so it falls
        // straight through to the newest row of any season.
        var source = byNewestSeason
            .FirstOrDefault(p => startDate is not null && p.Season!.StartDate.Date < startDate.Value.Date)
            ?? byNewestSeason.FirstOrDefault();

        return source?.CopyFor(seasonId) ?? new MatchPreferences { SeasonId = seasonId };
    }

    /// <summary>
    /// The next occurrence of <paramref name="matchDay"/> on or after <paramref name="referenceDate"/>.
    /// </summary>
    /// <param name="stepPastReference">
    /// True when the reference is a match already scheduled, so the answer has to be a later date —
    /// two games must not land on the same day. False when the reference is today, which is itself
    /// a valid answer if today happens to be the match day.
    /// </param>
    private static DateTime CalculateNextMatchDay(DateTime referenceDate, DayOfWeek matchDay, bool stepPastReference)
    {
        var startDate = stepPastReference ? referenceDate.AddDays(1) : referenceDate;
        var daysUntil = ((int)matchDay - (int)startDate.DayOfWeek + 7) % 7;

        return startDate.AddDays(daysUntil);
    }
}
