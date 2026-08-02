using System.Globalization;

namespace FootballFormation.Core;

public class Result
{
    private readonly object[] _errorArgs;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The failure in English, ready to display as-is. Null on success.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// The untranslated message template, e.g. <c>"Game with ID {0} not found"</c>. This is the
    /// resource key — the app's convention is that English text is the key (see
    /// docs/ui_components.md) — so the UI can look it up and fall back to <see cref="Error"/> when
    /// no translation exists. For a message with no placeholders it equals <see cref="Error"/>.
    /// </summary>
    public string? ErrorKey { get; }

    /// <summary>The values for <see cref="ErrorKey"/>'s placeholders, in order.</summary>
    public IReadOnlyList<object> ErrorArgs => _errorArgs;

    protected Result(bool isSuccess, string? errorKey, object[] errorArgs)
    {
        IsSuccess = isSuccess;
        ErrorKey = errorKey;
        _errorArgs = errorArgs;

        Error = errorKey is null
            ? null
            : errorArgs.Length == 0
                ? errorKey
                : string.Format(CultureInfo.InvariantCulture, errorKey, errorArgs);
    }

    public static Result Success() => new(true, null, []);

    /// <param name="errorKey">English message, and the resource key. Use <c>{0}</c> placeholders
    /// for anything variable rather than interpolating, or the message cannot be translated.</param>
    public static Result Failure(string errorKey, params object[] args) => new(false, errorKey, args);

    public static Result<T> Success<T>(T value) => new(value, true, null, []);

    public static Result<T> Failure<T>(string errorKey, params object[] args) =>
        new(default, false, errorKey, args);

    /// <summary>Carries a failure over to a different value type, keeping key and arguments intact.</summary>
    public Result<T> To<T>() => new(default, IsSuccess, ErrorKey, _errorArgs);
}

public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, string? errorKey, object[] errorArgs)
        : base(isSuccess, errorKey, errorArgs) =>
        _value = value;

    /// <summary>
    /// The result of the operation. Reading it on a failed result throws rather than handing back
    /// a silent default — a caller that skipped the success check has a bug, and it should surface
    /// here rather than as a null three frames away.
    /// </summary>
    public T? Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result: {Error}");
}
