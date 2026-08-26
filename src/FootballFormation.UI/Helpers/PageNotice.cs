using FootballFormation.Core;

namespace FootballFormation.UI.Helpers;

/// A page's own error line, for pages that render without a circuit.
public sealed class PageNotice
{
    public string? Message { get; private set; }

    public Severity Severity { get; private set; } = Severity.Error;

    /// Answers whether the call succeeded, so callers can guard follow-up work. A cancelled result records nothing and still answers false.
    public bool ReportFailure(IStringLocalizer<Strings> localizer, Result result)
    {
        if (result.IsSuccess) return true;

        if (result.IsCancelled) return false;

        Message = UiFeedback.Translate(localizer, result);
        Severity = Severity.Error;
        return false;
    }
}
