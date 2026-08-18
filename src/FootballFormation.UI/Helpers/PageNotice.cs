using FootballFormation.Core;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// A page's own error line, for pages that render without a circuit.
/// <para>
/// <see cref="ISnackbar"/> needs <c>MudSnackbarProvider</c>, and that provider only works inside an
/// interactive render mode — so on a statically rendered page a reported failure would go nowhere
/// at all. This holds the message instead and <c>InlineNotice</c> puts it on the page.
/// </para>
/// <para>
/// Deliberately the same shape as <see cref="UiFeedback.ReportFailure"/>, so a load path reads the
/// same either way and the only difference at the call site is which of the two it asks.
/// </para>
/// </summary>
public sealed class PageNotice
{
    public string? Message { get; private set; }

    public Severity Severity { get; private set; } = Severity.Error;

    /// <summary>
    /// Records the service error on failure and answers whether the call succeeded, so callers can
    /// guard their follow-up work.
    /// <para>
    /// A cancelled result records nothing and still answers false: the visitor has left, and there
    /// is no page left to say anything on.
    /// </para>
    /// </summary>
    public bool ReportFailure(IStringLocalizer<Strings> localizer, Result result)
    {
        if (result.IsSuccess) return true;

        if (result.IsCancelled) return false;

        Message = UiFeedback.Translate(localizer, result);
        Severity = Severity.Error;
        return false;
    }
}
