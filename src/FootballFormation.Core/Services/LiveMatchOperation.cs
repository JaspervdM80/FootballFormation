namespace FootballFormation.Core.Services;

/// <see cref="ServiceOperation.RunAdminAsync{T}"/> plus one notification on success. Part of the shape rather than a line at the end of
/// every method, so a spectator's screen going stale cannot be reintroduced by forgetting a call.
internal static class LiveMatchOperation
{

    internal static Task<Result<T>> RunAdminAsync<T>(
        LiveMatchNotifier notifier, int gameId, ICurrentUser currentUser, ILogger logger, string action,
        CancellationToken cancellationToken, Func<Task<Result<T>>> operation) =>
        // Inside the wrapper, not after it: a subscriber that throws is a failed operation, as it was when each method notified itself.
        ServiceOperation.RunAdminAsync(currentUser, logger, action, cancellationToken, async () =>
        {
            var result = await operation();
            if (result.IsSuccess) notifier.Notify(gameId);
            return result;
        });

    /// For a write with no value to hand back: the operation still answers with the game id, which it may only learn by doing the work —
    /// undoing a substitution identified by its own id, say.
    internal static async Task<Result> RunAdminAsync(
        LiveMatchNotifier notifier, ICurrentUser currentUser, ILogger logger, string action,
        CancellationToken cancellationToken, Func<Task<Result<int>>> operation) =>
        await ServiceOperation.RunAdminAsync(currentUser, logger, action, cancellationToken, async () =>
        {
            var result = await operation();
            if (result.IsSuccess) notifier.Notify(result.Value);
            return result;
        });
}
