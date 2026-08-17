using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class SeasonStats
{
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private Core.Reporting.SeasonStats? _stats;
    private bool _loaded;

    // Derived views over _stats, computed once on load for the razor.
    private List<Core.Reporting.PlayerStats> _scorers = [];
    private List<Core.Reporting.PlayerStats> _keepers = [];
    private List<Core.Reporting.PlayerStats> _playingTime = [];

    protected override async Task LoadAsync()
    {
        // Back to the spinner while the newly selected season loads.
        _loaded = false;

        // The squad is the authoritative roster, so the player list comes from it rather than from
        // every person on file. That is what stops a past season showing today's squad.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonId, Cancellation);
        var squads = Snackbar.ReportFailure(L, squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;
        var players = squads.AllPlayers;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        var games = Snackbar.ReportFailure(L, gamesResult) ? gamesResult.Value! : [];

        // Build takes the games and squads as parameters, so filtering at the call site is all a
        // season-scoped report needs — the report builders stay pure.
        _stats = SeasonStatsReport.Build(players, games, squads);

        _scorers = _stats.Players
            .Where(p => p.Goals > 0 || p.Assists > 0)
            .OrderByDescending(p => p.Goals)
            .ThenByDescending(p => p.Assists)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _keepers = _stats.Players
            .Where(p => p.GoalkeeperMinutes > 0)
            .OrderByDescending(p => p.GoalkeeperMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        // Fairness table is about squad rotation, so guests are left out — per season. On "All
        // seasons" a player counts if they were a regular in at least one of the seasons shown.
        // Ordered by share rather than by volume: someone who missed half the season and played
        // every minute of the rest was not rotated out, and sorting on the total says they were.
        // Utilization is whole percent, so ties are common and minutes break them.
        _playingTime = _stats.Players
            .Where(p => squads.IsFullMemberAnywhere(p.Player.Id))
            .OrderByDescending(p => p.Utilization)
            .ThenByDescending(p => p.TotalMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _loaded = true;
    }

    private void OpenPlayer(int playerId) => Navigation.NavigateTo(AppRoutes.PlayerStats(playerId));

    /// <summary>Single-letter form pill, localized (W/D/L in English, W/G/V in Dutch).</summary>
    private string ResultLetter(GameResult r) => L[r.ToString()].ToString()[..1];
}
