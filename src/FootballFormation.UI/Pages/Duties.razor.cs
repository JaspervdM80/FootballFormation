using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

public partial class Duties
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private DutyRoster? _roster;

    protected override async Task LoadAsync()
    {
        var result = await GameService.GetAllAsync(SeasonId, Cancellation);
        var games = _notice.ReportFailure(L, result) ? result.Value! : [];

        _roster = DutyRosterReport.Build(games, Time.GetLocalNow().Date);
    }

    private static string Duty(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : name;
}
