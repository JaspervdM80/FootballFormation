using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class SeasonStats : IDisposable
{
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;
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
        // Must come before any other service call — see SeasonState.EnsureLoadedAsync.
        await SeasonState.EnsureLoadedAsync();
        SeasonState.OnChanged += OnSeasonChanged;

        await Load();
    }

    private void OnSeasonChanged() => _ = InvokeAsync(async () =>
    {
        await Load();
        StateHasChanged();
    });

    public void Dispose() => SeasonState.OnChanged -= OnSeasonChanged;

    private async Task Load()
    {
        // Back to the spinner while the newly selected season loads.
        _loaded = false;

        // The squad is the authoritative roster, so the player list comes from it rather than from
        // every person on file. That is what stops a past season showing today's squad.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonState.SelectedSeasonId);
        var squads = Snackbar.ReportFailure(squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;
        var players = squads.AllPlayers;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonState.SelectedSeasonId);
        var games = Snackbar.ReportFailure(gamesResult) ? gamesResult.Value! : [];

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
        _playingTime = _stats.Players
            .Where(p => squads.IsFullMemberAnywhere(p.Player.Id))
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
