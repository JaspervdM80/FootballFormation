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
            var referenceDate = latestGame?.Date ?? DateTime.Today;
            var nextDate = CalculateNextMatchDay(referenceDate, matchDay, latestGame is not null);

            logger.LogDebug("Next match date calculated: {NextDate} (match day: {MatchDay})",
                nextDate.ToString("yyyy-MM-dd"), matchDay);
            return Result.Success(nextDate);
        });

    /// <summary>
    /// The next occurrence of <paramref name="matchDay"/> after the last game. With no games
    /// played yet, today counts as a valid match day; otherwise we always move forward.
    /// </summary>
    private static DateTime CalculateNextMatchDay(DateTime referenceDate, DayOfWeek matchDay, bool hasGames)
    {
        var startDate = hasGames ? referenceDate.AddDays(1) : referenceDate;
        var daysUntil = ((int)matchDay - (int)startDate.DayOfWeek + 7) % 7;

        if (daysUntil == 0 && hasGames)
            daysUntil = 7;

        return startDate.AddDays(daysUntil);
    }
}
