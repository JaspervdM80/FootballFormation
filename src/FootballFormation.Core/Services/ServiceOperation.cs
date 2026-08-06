using FootballFormation.Core.Security;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Wraps a database operation so every service fails the same way: the exception is
/// logged with its stack trace and the caller gets a "Failed to {action}" message
/// instead of a raw exception. Expected failures (not found, validation) are returned
/// by the operation itself as a <see cref="Result"/> and pass through untouched.
/// </summary>
public static class ServiceOperation
{
    /// <summary>
    /// The message every unexpected failure carries. Public because the UI has to recognise it:
    /// its one argument is an English action phrase that needs translating too, unlike the data
    /// every other message interpolates (see <c>UiFeedback.Translate</c>).
    /// </summary>
    public const string UnexpectedFailureKey = "Failed to {0}";

    internal static async Task<Result> RunAsync(ILogger logger, string action, Func<Task<Result>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}", action);
            return Result.Failure(UnexpectedFailureKey, action);
        }
    }

    internal static async Task<Result<T>> RunAsync<T>(ILogger logger, string action, Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation();
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
    /// <see cref="RunAsync(ILogger, string, Func{Task{Result}})"/>, so the check is a property of
    /// the shape instead of something each method has to remember.
    /// </summary>
    internal static async Task<Result> RunAdminAsync(
        ICurrentUser currentUser, ILogger logger, string action, Func<Task<Result>> operation)
    {
        if (!await IsAllowedAsync(currentUser, logger, action))
            return Result.Failure(NotAllowedKey, action);

        return await RunAsync(logger, action, operation);
    }

    /// <inheritdoc cref="RunAdminAsync(ICurrentUser, ILogger, string, Func{Task{Result}})"/>
    internal static async Task<Result<T>> RunAdminAsync<T>(
        ICurrentUser currentUser, ILogger logger, string action, Func<Task<Result<T>>> operation)
    {
        if (!await IsAllowedAsync(currentUser, logger, action))
            return Result.Failure<T>(NotAllowedKey, action);

        return await RunAsync(logger, action, operation);
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
