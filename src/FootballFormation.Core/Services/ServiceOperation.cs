using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Wraps a database operation so every service fails the same way: the exception is
/// logged with its stack trace and the caller gets a "Failed to {action}" message
/// instead of a raw exception. Expected failures (not found, validation) are returned
/// by the operation itself as a <see cref="Result"/> and pass through untouched.
/// </summary>
internal static class ServiceOperation
{
    public static async Task<Result> RunAsync(ILogger logger, string action, Func<Task<Result>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}", action);
            return Result.Failure($"Failed to {action}");
        }
    }

    public static async Task<Result<T>> RunAsync<T>(ILogger logger, string action, Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}", action);
            return Result.Failure<T>($"Failed to {action}");
        }
    }
}
