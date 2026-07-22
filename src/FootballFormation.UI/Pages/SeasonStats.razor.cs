using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class SeasonStats
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private Helpers.SeasonStats? _stats;
    private bool _loaded;

    // Derived views over _stats, computed once on load for the razor.
    private List<Helpers.PlayerStats> _scorers = [];
    private List<Helpers.PlayerStats> _keepers = [];
    private List<Helpers.PlayerStats> _playingTime = [];
    private int _maxMinutes;

    protected override async Task OnInitializedAsync()
    {
        var playersResult = await PlayerService.GetAllAsync();
        var players = Snackbar.ReportFailure(playersResult) ? playersResult.Value! : [];

        var gamesResult = await GameService.GetAllWithDetailsAsync();
        var games = Snackbar.ReportFailure(gamesResult) ? gamesResult.Value! : [];

        _stats = SeasonStatsReport.Build(players, games);

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

        // Fairness table is about squad rotation, so guests are left out.
        _playingTime = _stats.Players
            .Where(p => !p.Player.IsGuest)
            .OrderByDescending(p => p.TotalMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _maxMinutes = _playingTime.Count > 0 ? _playingTime.Max(p => p.TotalMinutes) : 0;

        _loaded = true;
    }

    private double BarWidth(int minutes) => _maxMinutes > 0 ? (double)minutes / _maxMinutes * 100 : 0;

    private void OpenPlayer(int playerId) => Navigation.NavigateTo($"/players/{playerId}/stats");

    /// <summary>Single-letter form pill, localized (W/D/L in English, W/G/V in Dutch).</summary>
    private string ResultLetter(GameResult r) => L[r.ToString()].ToString()[..1];
}
