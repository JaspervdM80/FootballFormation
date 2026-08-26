using FootballFormation.Core;

namespace FootballFormation.UI.Helpers;

/// A cancelled result gets no snackbar — it belongs to the circuit, not to the page the visitor left — and still answers false.
public static class UiFeedback
{
    /// Dialogs must not close on backdrop click — see docs/ui_components/dialogs-and-pickers.md.
    public static readonly DialogOptions LockedDialog = new()
    {
        BackdropClick = false,
        MaxWidth = MaxWidth.Small,
        FullWidth = true
    };

    /// Answers whether the call succeeded, so callers can guard follow-up work.
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

    /// Shows the service error only on failure — for loads that need no success noise.
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

    /// Core states its errors in English, which is also the resource key, so an untranslated entry falls back to that English text
    /// rather than showing a key. See docs/ui_components/shared-components.md.
    public static string Translate(IStringLocalizer<Strings> localizer, Result result)
    {
        if (result.ErrorKey is null) return string.Empty;

        // Only these two keys take a translatable argument; every other ErrorArg is data, so a player named "Start" stays "Start".
        if (result.ErrorKey is ServiceOperation.UnexpectedFailureKey or ServiceOperation.NotAllowedKey
            && result.ErrorArgs is [string action])
            return localizer[result.ErrorKey, localizer[action].Value];

        return localizer[result.ErrorKey, [.. result.ErrorArgs]];
    }
}
