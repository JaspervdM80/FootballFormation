using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class PositionDevelopment
{
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private Core.Reporting.PositionDevelopment? _report;
    private bool _loaded;

    protected override async Task LoadAsync()
    {
        _loaded = false;

        // Same source as /stats: the squad is the authoritative roster and per-player figures
        // reuse the same season-scoped game fetch and report builder.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonId, Cancellation);
        var squads = Snackbar.ReportFailure(L, squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        var games = Snackbar.ReportFailure(L, gamesResult) ? gamesResult.Value! : [];

        // Guests are left out, same as the playing-time fairness table on /stats: this grid is
        // about squad rotation, and a guest was never in the rotation to begin with.
        var regulars = squads.AllPlayers.Where(p => squads.IsFullMemberAnywhere(p.Id)).ToList();
        var stats = SeasonStatsReport.Build(regulars, games, squads);

        _report = PositionDevelopmentReport.Build(stats.Players);
        _loaded = true;
    }

    private void OnRowClicked(TableRowClickEventArgs<PositionDevelopmentRow> args) => OpenPlayer(args.Item!.Player.Id);

    private void OpenPlayer(int playerId) => Navigation.NavigateTo(AppRoutes.PlayerStats(playerId));
}
