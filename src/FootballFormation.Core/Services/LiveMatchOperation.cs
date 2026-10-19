namespace FootballFormation.Core.Services;

/// <see cref="ServiceOperation.RunAdminAsync{T}"/> plus one notification on success. Part of the shape rather than a line at the end of
/// every method, so a spectator's screen going stale cannot be reintroduced by forgetting a call.
internal static class LiveMatchOperation
{

    internal static Task<Result<T>> RunAdminAsync<T>(
        LiveMatchNotifier notifier, ICurrentTeam currentTeam, int gameId, ICurrentUser currentUser, ILogger logger, string action,
        CancellationToken cancellationToken, Func<Task<Result<T>>> operation,
        LiveMatchEvent change = LiveMatchEvent.Other) =>
        // Inside the wrapper, not after it: a subscriber that throws is a failed operation, as it was when each method notified itself.
        ServiceOperation.RunAdminAsync(currentUser, logger, action, cancellationToken, async () =>
        {
            var result = await operation();
            if (result.IsSuccess) await NotifyAsync(notifier, currentTeam, gameId, change);
            return result;
        });

    /// For a write with no value to hand back: the operation still answers with the game id, which it may only learn by doing the work —
    /// undoing a substitution identified by its own id, say.
    internal static async Task<Result> RunAdminAsync(
        LiveMatchNotifier notifier, ICurrentTeam currentTeam, ICurrentUser currentUser, ILogger logger, string action,
        CancellationToken cancellationToken, Func<Task<Result<int>>> operation,
        LiveMatchEvent change = LiveMatchEvent.Other) =>
        await ServiceOperation.RunAdminAsync(currentUser, logger, action, cancellationToken, async () =>
        {
            var result = await operation();
            if (result.IsSuccess) await NotifyAsync(notifier, currentTeam, result.Value, change);
            return result;
        });

    // The team in scope is the game's own: the write only found the game through the context that team stamped.
    private static async Task NotifyAsync(LiveMatchNotifier notifier, ICurrentTeam currentTeam, int gameId, LiveMatchEvent change)
    {
        if (await currentTeam.GetIdAsync() is { } teamId) notifier.Notify(new LiveMatchChange(teamId, gameId, change));
    }
}
