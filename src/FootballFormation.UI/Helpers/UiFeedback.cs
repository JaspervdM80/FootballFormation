using FootballFormation.Core;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Bridges the service-layer <see cref="Result"/> pattern and MudBlazor's snackbar so
/// pages don't repeat the same success/error branch after every service call.
/// A cancelled result gets no snackbar — it belongs to the circuit, not to the page the visitor left — and still answers false.
/// </summary>
public static class UiFeedback
{
    /// <summary>Dialogs must not close on backdrop click — see docs/ui_components/dialogs-and-pickers.md.</summary>
    public static readonly DialogOptions LockedDialog = new()
    {
        BackdropClick = false,
        MaxWidth = MaxWidth.Small,
        FullWidth = true
    };

    /// <summary>
    /// Shows <paramref name="successMessage"/> when the call succeeded, the translated service
    /// error otherwise. Returns whether it succeeded, so callers can guard follow-up work.
    /// </summary>
    public static bool Report(
        this ISnackbar snackbar,
        IStringLocalizer<Strings> localizer,
        Result result,
        string successMessage,
        Severity successSeverity = Severity.Success)
    {
        if (result.IsSuccess)
        {
            snackbar.Add(successMessage, successSeverity);
            return true;
        }

        if (result.IsCancelled) return false;

        snackbar.Add(Translate(localizer, result), Severity.Error);
        return false;
    }

    /// <summary>Shows the service error only on failure — for loads that need no success noise.</summary>
    public static bool ReportFailure(
        this ISnackbar snackbar,
        IStringLocalizer<Strings> localizer,
        Result result)
    {
        if (result.IsSuccess) return true;

        if (result.IsCancelled) return false;

        snackbar.Add(Translate(localizer, result), Severity.Error);
        return false;
    }

    /// <summary>
    /// Turns a failure into the reader's language. Core states its errors in English, which is also
    /// the resource key (see docs/ui_components/shared-components.md), so the untranslated template goes straight to
    /// the localizer — and an entry that hasn't been translated yet falls back to that English text
    /// rather than showing a key.
    /// </summary>
    public static string Translate(IStringLocalizer<Strings> localizer, Result result)
    {
        if (result.ErrorKey is null) return string.Empty;

        // Only these two keys take a translatable argument; every other ErrorArg is data, so a player named "Start" must not come out translated.
        if (result.ErrorKey is ServiceOperation.UnexpectedFailureKey or ServiceOperation.NotAllowedKey
            && result.ErrorArgs is [string action])
            return localizer[result.ErrorKey, localizer[action].Value];

        return localizer[result.ErrorKey, [.. result.ErrorArgs]];
    }
}
