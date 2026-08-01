using FootballFormation.Core.Models;
using FootballFormation.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class MatchPreferencesService(AppDbContext db, ILogger<MatchPreferencesService> logger)
{
    public Task<Result<MatchPreferences>> GetAsync() =>
        ServiceOperation.RunAsync(logger, "load preferences", async () =>
        {
            var prefs = await db.MatchPreferences.FirstOrDefaultAsync();
            if (prefs is not null) return Result.Success(prefs);

            prefs = new MatchPreferences();
            db.MatchPreferences.Add(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Created default match preferences (ID: {Id})", prefs.Id);
            return Result.Success(prefs);
        });

    public Task<Result> SaveAsync(MatchPreferences prefs) =>
        ServiceOperation.RunAsync(logger, "save preferences", async () =>
        {
            db.MatchPreferences.Update(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Saved match preferences: {Duration}min, {Split}, {Formation}, {MatchDay}",
                prefs.GameDurationMinutes, prefs.DefaultSplitType, prefs.DefaultFormation, prefs.MatchDay);
            return Result.Success();
        });

    public Task<Result<DateTime>> GetNextMatchDateAsync() =>
        ServiceOperation.RunAsync(logger, "calculate next match date", async () =>
        {
            var prefsResult = await GetAsync();
            if (prefsResult.IsFailure)
                return Result.Failure<DateTime>(prefsResult.Error!);

            var latestGame = await db.Games
                .OrderByDescending(g => g.Date)
                .FirstOrDefaultAsync();

            var matchDay = prefsResult.Value!.MatchDay;
            var today = DateTime.Today;
            var lastGame = latestGame?.Date.Date;

            // Only step off the last game while it is still ahead of us — that is the case this
            // was written for, a run of fixtures entered in advance. Once the last game is behind
            // us, measuring from it proposes a date in the past (a season's final game would have
            // the dialog opening months back), so today becomes the reference instead.
            var lastGameIsUpcoming = lastGame is not null && lastGame >= today;
            var referenceDate = lastGameIsUpcoming ? lastGame!.Value : today;
            var nextDate = CalculateNextMatchDay(referenceDate, matchDay, lastGameIsUpcoming);

            logger.LogDebug("Next match date calculated: {NextDate} (match day: {MatchDay})",
                nextDate.ToString("yyyy-MM-dd"), matchDay);
            return Result.Success(nextDate);
        });

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
