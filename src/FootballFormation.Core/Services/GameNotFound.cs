namespace FootballFormation.Core.Services;

// One message for a game that is not there, whichever service looked. No id in it: another team's game has to read exactly like one that
// never existed, and a coach has no use for the number.
internal static class GameNotFound
{
    private const string Message = "Game not found";

    internal static Result For(ILogger logger, int gameId)
    {
        Log(logger, gameId);
        return Result.Failure(Message);
    }

    internal static Result<T> For<T>(ILogger logger, int gameId)
    {
        Log(logger, gameId);
        return Result.Failure<T>(Message);
    }

    private static void Log(ILogger logger, int gameId) => logger.LogWarning("Game {GameId} not found in the team in scope", gameId);
}
