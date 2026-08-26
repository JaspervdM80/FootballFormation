using FootballFormation.Core;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// A page's own error line, for pages that render without a circuit.
/// </summary>
public sealed class PageNotice
{
    public string? Message { get; private set; }

    public Severity Severity { get; private set; } = Severity.Error;

    /// <summary>
    /// Records the service error on failure and answers whether the call succeeded, so callers can
    /// guard their follow-up work. A cancelled result records nothing and still answers false.
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
