using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class MatchPreferencesService(AppDbContext db, ILogger<MatchPreferencesService> logger)
{
    public async Task<Result<MatchPreferences>> GetAsync()
    {
        try
        {
            var prefs = await db.MatchPreferences.FirstOrDefaultAsync();
            if (prefs is not null) return Result.Success(prefs);

            prefs = new MatchPreferences();
            db.MatchPreferences.Add(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Created default match preferences (ID: {Id})", prefs.Id);
            return Result.Success(prefs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve match preferences");
            return Result.Failure<MatchPreferences>("Failed to load preferences");
        }
    }

    public async Task<Result> SaveAsync(MatchPreferences prefs)
    {
        try
        {
            db.MatchPreferences.Update(prefs);
            await db.SaveChangesAsync();

            logger.LogInformation("Saved match preferences: {Duration}min, {Split}, {Formation}, {MatchDay}",
                prefs.GameDurationMinutes, prefs.DefaultSplitType, prefs.DefaultFormation, prefs.MatchDay);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save match preferences");
            return Result.Failure("Failed to save preferences");
        }
    }

    public async Task<Result<DateTime>> GetNextMatchDateAsync()
    {
        try
        {
            var prefsResult = await GetAsync();
            if (prefsResult.IsFailure)
                return Result.Failure<DateTime>(prefsResult.Error!);

            var prefs = prefsResult.Value!;
            var latestGame = await db.Games
                .OrderByDescending(g => g.Date)
                .FirstOrDefaultAsync();

            var referenceDate = latestGame?.Date ?? DateTime.Today;
            var nextDate = CalculateNextMatchDay(referenceDate, prefs.MatchDay, latestGame is not null);

            logger.LogDebug("Next match date calculated: {NextDate} (match day: {MatchDay})",
                nextDate.ToString("yyyy-MM-dd"), prefs.MatchDay);
            return Result.Success(nextDate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate next match date");
            return Result.Failure<DateTime>("Failed to calculate next match date");
        }
    }

    private static DateTime CalculateNextMatchDay(DateTime referenceDate, DayOfWeek matchDay, bool hasGames)
    {
        var startDate = hasGames ? referenceDate.AddDays(1) : referenceDate;
        var daysUntil = ((int)matchDay - (int)startDate.DayOfWeek + 7) % 7;

        if (daysUntil == 0 && !hasGames)
            return startDate;

        if (daysUntil == 0)
            daysUntil = 7;

        return startDate.AddDays(daysUntil);
    }
}
