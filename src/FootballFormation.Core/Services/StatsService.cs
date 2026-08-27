using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Services;

// Paired because guest status is per season, so the figures are only correct against these squads.
public sealed record SeasonStatsView(SeasonStats Stats, SeasonSquads Squads);

// One entry covers all three statistics pages: SeasonStatsReport builds its per-player figures by
// calling PlayerStatsReport unchanged. See docs/patterns/service-structure.md.
public class StatsService(
    GameService games,
    SeasonSquadService squads,
    TrainingService trainings,
    TimeProvider time,
    StatsCache cache,
    ILogger<StatsService> logger)
{
    // seasonId null covers every season.
    public Task<Result<SeasonStatsView>> GetSeasonAsync(
        int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the statistics", cancellationToken, async () =>
        {
            // Before the load, never after — see StatsCache.KeyFor.
            var key = cache.KeyFor($"season:{seasonId}");

            if (cache.TryGet<SeasonStatsView>(key, out var hit)) return Result.Success(hit);

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<SeasonStatsView>();

            var (allGames, allSquads) = inputs.Value!;

            // The squad, not everyone on file, or a past season shows today's squad.
            var view = new SeasonStatsView(
                SeasonStatsReport.Build(allSquads.AllPlayers, allGames, allSquads),
                allSquads);

            cache.Set(key, view);

            logger.LogDebug("Built statistics for season {SeasonId} over {Count} games",
                seasonId, allGames.Count);
            return Result.Success(view);
        });

    public Task<Result<PlayerStats>> GetPlayerAsync(
        Player player, int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the player statistics", cancellationToken, async () =>
        {
            var seasonResult = await GetSeasonAsync(seasonId, cancellationToken);
            if (seasonResult.IsFailure) return seasonResult.To<PlayerStats>();

            var existing = seasonResult.Value!.Stats.Players.FirstOrDefault(p => p.Player.Id == player.Id);
            if (existing is not null) return Result.Success(existing);

            // In no squad of this season, but the page is reachable for anyone on file.
            var key = cache.KeyFor($"player:{player.Id}:{seasonId}");

            if (cache.TryGet<PlayerStats>(key, out var hit)) return Result.Success(hit);

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<PlayerStats>();

            var (allGames, allSquads) = inputs.Value!;
            var stats = PlayerStatsReport.Build(player, allGames, allSquads);

            cache.Set(key, stats);
            return Result.Success(stats);
        });

    /// No admin guard of its own, and no cache entry: TrainingService.GetAllAsync is the guard, and caching the result here would hand
    /// a later reader the attendance without passing it. Absences are the one read that is not public — see docs/models/training.md.
    public Task<Result<TrainingAttendance>> GetTrainingAttendanceAsync(
        int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the training attendance", cancellationToken, async () =>
        {
            var inputs = await LoadTrainingsAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<TrainingAttendance>();

            var (allTrainings, allSquads) = inputs.Value!;
            return Result.Success(
                TrainingAttendanceReport.Build(allTrainings, allSquads, time.GetLocalNow().Date));
        });

    public Task<Result<PlayerTrainingAttendance>> GetPlayerTrainingAttendanceAsync(
        Player player, int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the training attendance", cancellationToken, async () =>
        {
            var inputs = await LoadTrainingsAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<PlayerTrainingAttendance>();

            var (allTrainings, allSquads) = inputs.Value!;
            return Result.Success(
                TrainingAttendanceReport.BuildFor(player, allTrainings, allSquads, time.GetLocalNow().Date));
        });

    private async Task<Result<(List<Game> Games, SeasonSquads Squads)>> LoadAsync(
        int? seasonId, CancellationToken cancellationToken)
    {
        var squadsResult = await squads.GetSquadsAsync(seasonId, cancellationToken);
        if (squadsResult.IsFailure) return squadsResult.To<(List<Game>, SeasonSquads)>();

        var gamesResult = await games.GetAllWithDetailsAsync(seasonId, cancellationToken);
        if (gamesResult.IsFailure) return gamesResult.To<(List<Game>, SeasonSquads)>();

        return Result.Success((gamesResult.Value!, squadsResult.Value!));
    }

    private async Task<Result<(List<Training> Trainings, SeasonSquads Squads)>> LoadTrainingsAsync(
        int? seasonId, CancellationToken cancellationToken)
    {
        var trainingsResult = await trainings.GetAllAsync(seasonId, cancellationToken);
        if (trainingsResult.IsFailure) return trainingsResult.To<(List<Training>, SeasonSquads)>();

        var squadsResult = await squads.GetSquadsAsync(seasonId, cancellationToken);
        if (squadsResult.IsFailure) return squadsResult.To<(List<Training>, SeasonSquads)>();

        return Result.Success((trainingsResult.Value!, squadsResult.Value!));
    }
}
