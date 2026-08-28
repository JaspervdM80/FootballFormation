namespace FootballFormation.Core.Services;

/// Only unexpected exceptions are caught; a Result the operation returns itself passes through untouched. A cancelled call answers
/// <see cref="Result.Cancelled()"/>, or every navigation-away would raise a snackbar on the page the visitor moved to.
public static class ServiceOperation
{
    /// Public because the UI has to recognise it: its argument is an English action phrase needing translation, unlike the data every
    /// other message interpolates. See UiFeedback.Translate.
    public const string UnexpectedFailureKey = "Failed to {0}";

    internal static async Task<Result> RunAsync(
        ILogger logger, string action, CancellationToken cancellationToken, Func<Task<Result>> operation)
    {
        // Before the lambda, so the contract holds even if nothing inside observes the token.
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

    /// <summary>Every mutating service method goes through this rather than the plain overload, so the admin check is a property of the
    /// shape instead of something each method has to remember.</summary>
    internal static async Task<Result> RunAdminAsync(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result>> operation)
    {
        // Ahead of the authorization check, so an abandoned call is never logged as a refusal.
        if (cancellationToken.IsCancellationRequested) return Abandoned(logger, action);

        if (!await IsAllowedAsync(currentUser.IsAdminAsync, logger, action))
            return Result.Failure(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// <inheritdoc cref="RunAdminAsync(ICurrentUser, ILogger, string, CancellationToken, Func{Task{Result}})"/>
    internal static async Task<Result<T>> RunAdminAsync<T>(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result<T>>> operation)
    {
        if (cancellationToken.IsCancellationRequested) return Abandoned<T>(logger, action);

        if (!await IsAllowedAsync(currentUser.IsAdminAsync, logger, action))
            return Result.Failure<T>(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// <summary>The same shape one rung up, for what only an application admin may change: the clubs and teams the app serves, and who
    /// else may manage them.</summary>
    internal static async Task<Result> RunApplicationAdminAsync(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result>> operation)
    {
        if (cancellationToken.IsCancellationRequested) return Abandoned(logger, action);

        if (!await IsAllowedAsync(currentUser.IsApplicationAdminAsync, logger, action))
            return Result.Failure(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// <inheritdoc cref="RunApplicationAdminAsync(ICurrentUser, ILogger, string, CancellationToken, Func{Task{Result}})"/>
    internal static async Task<Result<T>> RunApplicationAdminAsync<T>(
        ICurrentUser currentUser, ILogger logger, string action, CancellationToken cancellationToken,
        Func<Task<Result<T>>> operation)
    {
        if (cancellationToken.IsCancellationRequested) return Abandoned<T>(logger, action);

        if (!await IsAllowedAsync(currentUser.IsApplicationAdminAsync, logger, action))
            return Result.Failure<T>(NotAllowedKey, action);

        return await RunAsync(logger, action, cancellationToken, operation);
    }

    /// Debug, not Warning: a visitor leaving a page is the most ordinary thing there is.
    private static Result Abandoned(ILogger logger, string action)
    {
        logger.LogDebug("Gave up trying to {Action}: the caller went away", action);
        return Result.Cancelled();
    }

    private static Result<T> Abandoned<T>(ILogger logger, string action)
    {
        logger.LogDebug("Gave up trying to {Action}: the caller went away", action);
        return Result.Cancelled<T>();
    }

    /// A principal that cannot be resolved at all is a refusal, not an error — the answer is still "no".
    private static async Task<bool> IsAllowedAsync(Func<Task<bool>> authorize, ILogger logger, string action)
    {
        bool allowed;
        try
        {
            allowed = await authorize();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not determine whether the caller may {Action}", action);
            return false;
        }

        if (!allowed) logger.LogWarning("Refused an unauthorized attempt to {Action}", action);
        return allowed;
    }

    /// Its argument is an English action phrase, so like <see cref="UnexpectedFailureKey"/> it needs translating rather than passing
    /// through as data. See UiFeedback.Translate.
    public const string NotAllowedKey = "You are not signed in to {0}";
}
