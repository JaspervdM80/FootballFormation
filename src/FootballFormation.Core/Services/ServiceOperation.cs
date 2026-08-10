using FootballFormation.Core.Security;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Wraps a database operation so every service fails the same way: the exception is
/// logged with its stack trace and the caller gets a "Failed to {action}" message
/// instead of a raw exception. Expected failures (not found, validation) are returned
/// by the operation itself as a <see cref="Result"/> and pass through untouched.
/// <para>
/// It is also where a cancelled call stops being an exception. Every service method takes a
/// <see cref="CancellationToken"/> and hands it to EF, so abandoning a query throws
/// <see cref="OperationCanceledException"/> from somewhere inside the lambda. That is not a
/// failure — it is the caller having left — so it is caught ahead of the general handler and
/// answered with <see cref="Result.Cancelled()"/>. Without that, every navigation-away would log
/// an error and raise a "Failed to load games" snackbar on the page the visitor moved to.
/// </para>
/// </summary>
public static class ServiceOperation
{
    /// <summary>
    /// The message every unexpected failure carries. Public because the UI has to recognise it:
    /// its one argument is an English action phrase that needs translating too, unlike the data
    /// every other message interpolates (see <c>UiFeedback.Translate</c>).
    /// </summary>
    public const string UnexpectedFailureKey = "Failed to {0}";

    internal static async Task<Result> RunAsync(
        ILogger logger, string action, CancellationToken cancellationToken, Func<Task<Result>> operation)
    {
        // Answered before the lambda runs, so the contract holds even for an operation that never
        // gets as far as an EF call that would observe the token.
        if (cancellationToken.IsCancellationRequested) return Abandoned(logger, action);

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Abandoned(logger, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}", action);
            return Result.Failure(UnexpectedFailureKey, action);
        }
    }

    /// <inheritdoc cref="RunAsync(ILogger, string, CancellationToken, Func{Task{Result}})"/>
    internal static async Task<Result<T>> RunAsync<T>(
        ILogger logger, string action, CancellationToken cancellationToken, Func<Task<Result<T>>> operation)
    {
        if (cancellationToken.IsCancellationRequested) return Abandoned<T>(logger, action);

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Abandoned<T>(logger, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}", action);
            return Result.Failure<T>(UnexpectedFailureKey, action);
        }
    }

    /// <summary>
    /// The same wrapper for an operation that writes: refuses before running when the caller is
    /// not an admin. Every mutating service method goes through this rather than
    /// <see cref="RunAsync(ILogger, string, CancellationToken, Func{Task{Result}})"/>, so the check
    /// is a property of the shape instead of something each method has to remember.
    /// </summary>
    internal static async Task<Result> RunAdminAsync(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result>> operation)
    {
        // Ahead of the authorization check, so an abandoned call is never logged as a refusal.
        if (cancellationToken.IsCancellationRequested) return Abandoned(logger, action);

        if (!await IsAllowedAsync(currentUser, logger, action))
            return Result.Failure(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// <inheritdoc cref="RunAdminAsync(ICurrentUser, ILogger, string, CancellationToken, Func{Task{Result}})"/>
    internal static async Task<Result<T>> RunAdminAsync<T>(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result<T>>> operation)
    {
        if (cancellationToken.IsCancellationRequested) return Abandoned<T>(logger, action);

        if (!await IsAllowedAsync(currentUser, logger, action))
            return Result.Failure<T>(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// <summary>
    /// Debug, not Warning: a visitor leaving a page is the most ordinary thing the app does, and
    /// on a phone on a bad connection it happens constantly. It is logged at all only because
    /// "the query stopped and nothing was shown" is otherwise invisible when tracing one request.
    /// </summary>
    private static Result Abandoned(ILogger logger, string action)
    {
        logger.LogDebug("Gave up trying to {Action}: the caller went away", action);
        return Result.Cancelled();
    }

    /// <inheritdoc cref="Abandoned(ILogger, string)"/>
    private static Result<T> Abandoned<T>(ILogger logger, string action)
    {
        logger.LogDebug("Gave up trying to {Action}: the caller went away", action);
        return Result.Cancelled<T>();
    }

    /// <summary>
    /// A failed authorization check counts as a refusal, not an error: if the principal cannot be
    /// resolved at all, the answer is still "no".
    /// </summary>
    private static async Task<bool> IsAllowedAsync(ICurrentUser currentUser, ILogger logger, string action)
    {
        bool allowed;
        try
        {
            allowed = await currentUser.IsAdminAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not determine whether the caller may {Action}", action);
            return false;
        }

        if (!allowed) logger.LogWarning("Refused an unauthorized attempt to {Action}", action);
        return allowed;
    }

    /// <summary>
    /// Its argument is an English action phrase, so like <see cref="UnexpectedFailureKey"/> it
    /// needs translating rather than passing through as data — see <c>UiFeedback.Translate</c>.
    /// </summary>
    public const string NotAllowedKey = "You are not signed in to {0}";
}
