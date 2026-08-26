namespace FootballFormation.Core.Services;

public class TrainingService(
    IDbContextFactory<AppDbContext> dbFactory,
    SeasonService seasons,
    ICurrentUser currentUser,
    ILogger<TrainingService> logger)
{
    /// A null <paramref name="seasonId"/> loads every season. Admin-only even though it reads nothing: who missed a training, and the
    /// note saying why, is the one thing in this app that is not public — see docs/models/training.md.
    public Task<Result<List<Training>>> GetAllAsync(
        int? seasonId = null, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "load the trainings", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var trainings = (await db.Trainings
                .AsNoTracking()
                .Where(t => seasonId == null || t.SeasonId == seasonId)
                .ToListAsync(cancellationToken))
                .NewestFirst();

            logger.LogDebug("Retrieved {Count} trainings for season {SeasonId}", trainings.Count, seasonId);
            return Result.Success(trainings);
        });

    public Task<Result<Training>> CreateAsync(Training training, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create the training", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Resolved here rather than at the call site, the same as GameService.CreateAsync: SeasonId 0 is the dialog's "by date".
            if (training.SeasonId == 0)
            {
                var seasonResult = await seasons.GetOrCreateForDateAsync(training.Date, cancellationToken);
                if (seasonResult.IsFailure) return seasonResult.To<Training>();

                training.SeasonId = seasonResult.Value!.Id;
            }

            db.Trainings.Add(training);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created training on {Date} in season {SeasonId} (ID: {TrainingId})",
                training.Date.ToString("yyyy-MM-dd"), training.SeasonId, training.Id);
            return Result.Success(training);
        });

    public Task<Result> UpdateAsync(Training training, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update the training", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Setting State rather than DbSet.Update, for the reason spelled out in GameService.UpdateAsync.
            db.Entry(training).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated training on {Date} in season {SeasonId} (ID: {TrainingId})",
                training.Date.ToString("yyyy-MM-dd"), training.SeasonId, training.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete the training", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var training = await db.Trainings.FindAsync([id], cancellationToken);
            if (training is null)
            {
                logger.LogWarning("Cannot delete training {TrainingId}: not found", id);
                return Result.Failure("Training not found");
            }

            db.Trainings.Remove(training);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted training on {Date} (ID: {TrainingId})",
                training.Date.ToString("yyyy-MM-dd"), training.Id);
            return Result.Success();
        });
}
