using FootballFormation.Core;
using MudBlazor;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Bridges the service-layer <see cref="Result"/> pattern and MudBlazor's snackbar so
/// pages don't repeat the same success/error branch after every service call.
/// </summary>
public static class UiFeedback
{
    /// <summary>Dialogs must not close on backdrop click — see docs/ui_components.md.</summary>
    public static readonly DialogOptions LockedDialog = new()
    {
        BackdropClick = false,
        MaxWidth = MaxWidth.Small,
        FullWidth = true
    };

    /// <summary>
    /// Shows <paramref name="successMessage"/> when the call succeeded, the service error
    /// otherwise. Returns whether it succeeded, so callers can guard follow-up work.
    /// </summary>
    public static bool Report(
        this ISnackbar snackbar,
        Result result,
        string successMessage,
        Severity successSeverity = Severity.Success)
    {
        if (result.IsSuccess)
        {
            snackbar.Add(successMessage, successSeverity);
            return true;
        }

        snackbar.Add(result.Error!, Severity.Error);
        return false;
    }

    /// <summary>Shows the service error only on failure — for loads that need no success noise.</summary>
    public static bool ReportFailure(this ISnackbar snackbar, Result result)
    {
        if (result.IsSuccess) return true;

        snackbar.Add(result.Error!, Severity.Error);
        return false;
    }
}
