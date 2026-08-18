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
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private Core.Reporting.PositionDevelopment? _report;
    private bool _loaded;

    protected override async Task LoadAsync()
    {
        _loaded = false;

        // Same source as /stats — the squad is the authoritative roster, not everyone on file.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonId, Cancellation);
        var squads = _notice.ReportFailure(L, squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        var games = _notice.ReportFailure(L, gamesResult) ? gamesResult.Value! : [];

        // Guests are left out, same as the playing-time fairness table on /stats: this grid is
        // about squad rotation, and a guest was never in the rotation to begin with.
        var regulars = squads.AllPlayers.Where(p => squads.IsFullMemberAnywhere(p.Id)).ToList();
        var stats = SeasonStatsReport.Build(regulars, games, squads);

        _report = PositionDevelopmentReport.Build(stats.Players);
        _loaded = true;
    }
}
