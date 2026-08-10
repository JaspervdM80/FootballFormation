using FootballFormation.Core.Security;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// The shape of a write made while a match is being played: everything
/// <see cref="ServiceOperation.RunAdminAsync{T}"/> does, and then — only when the write succeeded —
/// one <see cref="LiveMatchNotifier"/> notification naming the game that changed.
/// <para>
/// The notification belongs to the shape rather than to a line at the end of every method, in the
/// way the admin check already does. Three services now write to a match being played, and a
/// spectator's screen silently going stale is not a bug any of them should be able to reintroduce
/// by forgetting a call.
/// </para>
/// </summary>
internal static class LiveMatchOperation
{
    /// <param name="gameId">The game the caller named, and the one every viewer is told about.</param>
    internal static Task<Result<T>> RunAdminAsync<T>(
        LiveMatchNotifier notifier, int gameId, ICurrentUser currentUser, ILogger logger, string action,
        CancellationToken cancellationToken, Func<Task<Result<T>>> operation) =>
        // Inside the wrapper, not after it: a subscriber that throws is a failed operation, the
        // same as it was when each method ended with its own Notify call.
        ServiceOperation.RunAdminAsync(currentUser, logger, action, cancellationToken, async () =>
        {
            var result = await operation();
            if (result.IsSuccess) notifier.Notify(gameId);
            return result;
        });

    /// <summary>
    /// The same, for a write whose caller has no value to take away. The operation answers with the
    /// id of the game it changed — which it may only learn by doing the work, as undoing a
    /// substitution identified by its own id does — and the caller gets a plain <see cref="Result"/>.
    /// </summary>
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
