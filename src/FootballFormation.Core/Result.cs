using System.Globalization;

namespace FootballFormation.Core;

public class Result
{
    private readonly object[] _errorArgs;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// An <see cref="IsFailure"/> too, so every "did that work?" check reads it as no — but carrying no message, so the UI shows nothing.
    /// See docs/patterns/result-and-cancellation.md.
    public bool IsCancelled { get; }

    /// Null on success and on a cancellation.
    public string? Error { get; }

    /// The English text is itself the resource key, which is what lets the UI look it up and fall back to <see cref="Error"/> when no
    /// translation exists. See docs/ui_components/shared-components.md.
    public string? ErrorKey { get; }

    /// The values for <see cref="ErrorKey"/>'s placeholders, in order.
    public IReadOnlyList<object> ErrorArgs => _errorArgs;

    protected Result(bool isSuccess, string? errorKey, object[] errorArgs, bool isCancelled = false)
    {
        IsSuccess = isSuccess;
        ErrorKey = errorKey;
        IsCancelled = isCancelled;
        _errorArgs = errorArgs;

        Error = errorKey is null
            ? null
            : errorArgs.Length == 0
                ? errorKey
                : string.Format(CultureInfo.InvariantCulture, errorKey, errorArgs);
    }

    public static Result Success() => new(true, null, []);

    /// Use <c>{0}</c> placeholders for anything variable rather than interpolating, or the message cannot be translated.
    public static Result Failure(string errorKey, params object[] args) => new(false, errorKey, args);

    public static Result Cancelled() => new(false, null, [], isCancelled: true);

    public static Result<T> Success<T>(T value) => new(value, true, null, []);

    public static Result<T> Failure<T>(string errorKey, params object[] args) =>
        new(default, false, errorKey, args);

    public static Result<T> Cancelled<T>() => new(default, false, null, [], isCancelled: true);

    /// Carries a failure — or a cancellation — to a different value type, intact.
    public Result<T> To<T>() => new(default, IsSuccess, ErrorKey, _errorArgs, IsCancelled);
}

public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, string? errorKey, object[] errorArgs, bool isCancelled = false)
        : base(isSuccess, errorKey, errorArgs, isCancelled) =>
        _value = value;

    /// Throws on a failed result rather than handing back a silent default: a caller that skipped the success check has a bug, and it
    /// should surface here rather than as a null three frames away.
    public T? Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(IsCancelled
            ? "Cannot read the value of a cancelled result: the caller went away before it had one"
            : $"Cannot read the value of a failed result: {Error}");
}
