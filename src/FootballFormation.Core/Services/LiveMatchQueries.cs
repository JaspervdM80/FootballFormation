namespace FootballFormation.Core.Services;

/// The load every touchline write starts from, described once for the three services that share it: the game with its planned line-ups,
/// tracked so it can be written back.
internal static class LiveMatchQueries
{
    internal static Task<Game?> LoadWithPeriodsAsync(
        this AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.WithPeriods().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
}
