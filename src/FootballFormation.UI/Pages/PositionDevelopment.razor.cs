using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

public partial class PositionDevelopment
{
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private Core.Reporting.PositionDevelopment? _report;
    private bool _loaded;

    protected override async Task LoadAsync()
    {
        _loaded = false;

        // Filtered, not rebuilt, so this shares /stats' cache entry — each player's figures are
        // built independently, so dropping the guests afterwards is the same answer.
        var result = await StatsService.GetSeasonAsync(SeasonId, Cancellation);
        var view = _notice.ReportFailure(L, result)
            ? result.Value!
            : new SeasonStatsView(Core.Reporting.SeasonStats.Empty, SeasonSquads.Empty);

        // Guests are left out, same as the playing-time fairness table on /stats: this grid is
        // about squad rotation, and a guest was never in the rotation to begin with.
        var regulars = view.Stats.Players
            .Where(p => view.Squads.IsFullMemberAnywhere(p.Player.Id))
            .ToList();

        _report = PositionDevelopmentReport.Build(regulars);
        _loaded = true;
    }
}
