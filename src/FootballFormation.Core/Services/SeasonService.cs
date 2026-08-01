using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class SeasonService(AppDbContext db, ILogger<SeasonService> logger)
{
    /// <summary>Newest first — the season picker and the current-season fallbacks rely on it.</summary>
    public Task<Result<List<Season>>> GetAllAsync() =>
        ServiceOperation.RunAsync(logger, "load seasons", async () =>
        {
            var seasons = await db.Seasons
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} seasons", seasons.Count);
            return Result.Success(seasons);
        });

    /// <summary>Falls back to the newest season when no row is flagged, so a hand-edited or
    /// half-migrated database still yields something usable.</summary>
    public Task<Result<Season>> GetCurrentAsync() =>
        ServiceOperation.RunAsync(logger, "load current season", async () =>
        {
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsCurrent)
                ?? await db.Seasons.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync();

            if (season is null)
            {
                logger.LogWarning("No seasons defined");
                return Result.Failure<Season>("No seasons defined");
            }

            return Result.Success(season);
        });

    /// <summary>
    /// The season covering <paramref name="date"/>, or null when none is defined yet. Read-only
    /// sibling of <see cref="GetOrCreateForDateAsync"/>, for callers that must not create a season
    /// as a side effect — e.g. the game dialog reacting to a date the user may still cancel.
    /// </summary>
    public Task<Result<Season?>> FindForDateAsync(DateTime date) =>
        ServiceOperation.RunAsync(logger, "look up the season for that date", async () =>
        {
            var day = date.Date;

            var season = await db.Seasons
                .FirstOrDefaultAsync(s => s.StartDate <= day && s.EndDate >= day);

            return Result.Success(season);
        });

    /// <summary>
    /// The season covering <paramref name="date"/>, created on the fly when a game is scheduled
    /// beyond the seasons defined so far. Season windows are gapless, so this always resolves.
    /// </summary>
    public Task<Result<Season>> GetOrCreateForDateAsync(DateTime date) =>
        ServiceOperation.RunAsync(logger, "find the season for that date", async () =>
        {
            var day = date.Date;

            var lookup = await FindForDateAsync(day);
            if (lookup.IsFailure) return Result.Failure<Season>(lookup.Error!);
            if (lookup.Value is not null) return Result.Success(lookup.Value);

            var season = Season.CreateFor(day);
            db.Seasons.Add(season);
            await db.SaveChangesAsync();

            logger.LogInformation("Created season {SeasonName} for date {Date} (ID: {SeasonId})",
                season.Name, day.ToString("yyyy-MM-dd"), season.Id);
            return Result.Success(season);
        });

    public Task<Result<Season>> CreateAsync(Season season) =>
        ServiceOperation.RunAsync(logger, "create season", async () =>
        {
            var validation = await ValidateAsync(season);
            if (validation.IsFailure) return Result.Failure<Season>(validation.Error!);

            db.Seasons.Add(season);
            await db.SaveChangesAsync();

            logger.LogInformation("Created season {SeasonName} ({Start} - {End}) (ID: {SeasonId})",
                season.Name, season.StartDate.ToString("yyyy-MM-dd"),
                season.EndDate.ToString("yyyy-MM-dd"), season.Id);
            return Result.Success(season);
        });

    public Task<Result> UpdateAsync(Season season) =>
        ServiceOperation.RunAsync(logger, "update season", async () =>
        {
            var validation = await ValidateAsync(season);
            if (validation.IsFailure) return validation;

            db.Seasons.Update(season);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated season {SeasonName} (ID: {SeasonId})", season.Name, season.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id) =>
        ServiceOperation.RunAsync(logger, "delete season", async () =>
        {
            var season = await db.Seasons.FindAsync(id);
            if (season is null)
            {
                logger.LogWarning("Cannot delete season {SeasonId}: not found", id);
                return Result.Failure("Season not found");
            }

            // The FK is Restrict, so refuse here with something readable rather than letting the
            // caller hit a raw DbUpdateException.
            var gameCount = await db.Games.CountAsync(g => g.SeasonId == id);
            if (gameCount > 0)
            {
                logger.LogWarning("Cannot delete season {SeasonName}: {Count} games still assigned",
                    season.Name, gameCount);
                return Result.Failure($"Season {season.Name} still has {gameCount} games");
            }

            if (season.IsCurrent)
            {
                logger.LogWarning("Cannot delete season {SeasonName}: it is the current season", season.Name);
                return Result.Failure($"Season {season.Name} is the current season");
            }

            db.Seasons.Remove(season);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted season {SeasonName} (ID: {SeasonId})", season.Name, season.Id);
            return Result.Success();
        });

    public Task<Result> SetCurrentAsync(int id) =>
        ServiceOperation.RunAsync(logger, "switch season", async () =>
        {
            var seasons = await db.Seasons.ToListAsync();
            var target = seasons.FirstOrDefault(s => s.Id == id);

            if (target is null)
            {
                logger.LogWarning("Cannot set current season {SeasonId}: not found", id);
                return Result.Failure("Season not found");
            }

            // One SaveChanges, so "exactly one current season" is never briefly broken.
            foreach (var season in seasons) season.IsCurrent = season.Id == id;
            await db.SaveChangesAsync();

            logger.LogInformation("Current season set to {SeasonName} (ID: {SeasonId})", target.Name, target.Id);
            return Result.Success();
        });

    /// <summary>
    /// Idempotent startup guard. Runs on every boot so a fresh install — whose migration backfill
    /// found no games to derive seasons from — still has a current season to fall back on.
    /// </summary>
    public Task<Result<Season>> EnsureCurrentSeasonAsync() =>
        ServiceOperation.RunAsync(logger, "prepare seasons", async () =>
        {
            var current = await db.Seasons.FirstOrDefaultAsync(s => s.IsCurrent);
            if (current is not null) return Result.Success(current);

            var newest = await db.Seasons.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync();
            if (newest is not null)
            {
                newest.IsCurrent = true;
                await db.SaveChangesAsync();

                logger.LogInformation("Marked season {SeasonName} as current (ID: {SeasonId})",
                    newest.Name, newest.Id);
                return Result.Success(newest);
            }

            var season = Season.CreateFor(DateTime.Today);
            season.IsCurrent = true;
            db.Seasons.Add(season);
            await db.SaveChangesAsync();

            logger.LogInformation("Seeded first season {SeasonName} (ID: {SeasonId})", season.Name, season.Id);
            return Result.Success(season);
        });

    /// <summary>Rules the dialog deliberately does not enforce, so any caller gets them.</summary>
    private async Task<Result> ValidateAsync(Season season)
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

        var overlapping = await db.Seasons
            .Where(s => s.Id != season.Id)
            .Where(s => s.StartDate <= season.EndDate && s.EndDate >= season.StartDate)
            .FirstOrDefaultAsync();

        if (overlapping is not null)
        {
            logger.LogWarning("Rejected season {SeasonName}: overlaps {OtherSeason}",
                season.Name, overlapping.Name);
            return Result.Failure($"These dates overlap season {overlapping.Name}");
        }

        return Result.Success();
    }
}
