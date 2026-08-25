using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

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

        // Filtered rather than rebuilt, so this shares the cache entry /stats reads: each player's
        // figures are built independently, so dropping the guests afterwards is the same answer.
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
