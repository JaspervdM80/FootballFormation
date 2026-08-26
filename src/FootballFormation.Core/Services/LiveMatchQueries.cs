namespace FootballFormation.Core.Services;

/// The load every touchline write starts from, described once for the three services that share it: the game with its planned line-ups,
/// tracked so it can be written back.
internal static class LiveMatchQueries
{
    internal static Task<Game?> LoadWithPeriodsAsync(
        this AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.WithPeriods().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

    /// One message for a game that is not there, so all three services say the same thing.
    internal static Result<T> GameNotFound<T>(int gameId) =>
        Result.Failure<T>("Game with ID {0} not found", gameId);
}
