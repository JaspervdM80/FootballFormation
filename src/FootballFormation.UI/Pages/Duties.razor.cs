using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

public partial class Duties
{
    [Inject] private StatsService Stats { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private DutyRoster? _roster;

    protected override async Task LoadAsync()
    {
        var result = await Stats.GetHomeAsync(SeasonId, Cancellation);
        _roster = _notice.ReportFailure(L, result) ? result.Value!.Duties : DutyRoster.Empty;
    }

    private static string Duty(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : name;
}
