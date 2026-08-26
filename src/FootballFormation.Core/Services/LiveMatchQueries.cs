namespace FootballFormation.Core.Services;

/// <summary>
/// The load every touchline write starts from, described once for the three services that share
/// it: the game with its planned line-ups, tracked so it can be written back. The clock marks the
/// half's line-up as it starts and ends, a goal takes its minute from the half being played, and a
/// substitution moves a slot inside that half's line-up.
/// </summary>
internal static class LiveMatchQueries
{
    internal static Task<Game?> LoadWithPeriodsAsync(
        this AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.WithPeriods().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

    /// <summary>One message for a game that isn't there, so all three services say the same thing.</summary>
    internal static Result<T> GameNotFound<T>(int gameId) =>
        Result.Failure<T>("Game with ID {0} not found", gameId);
}
