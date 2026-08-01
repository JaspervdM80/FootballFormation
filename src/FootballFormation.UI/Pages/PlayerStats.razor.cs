using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class PlayerStats : IDisposable
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [Parameter] public int PlayerId { get; set; }

    private Helpers.PlayerStats? _stats;
    private bool _loaded;

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
        _loaded = false;

        var playerResult = await PlayerService.GetByIdAsync(PlayerId);
        if (!Snackbar.ReportFailure(playerResult))
        {
            Navigation.NavigateTo("/players");
            return;
        }

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonState.SelectedSeasonId);
        var games = Snackbar.ReportFailure(gamesResult) ? gamesResult.Value! : [];

        _stats = PlayerStatsReport.Build(playerResult.Value!, games);
        _loaded = true;
    }

    private void NavigateBack() => Navigation.NavigateTo("/players");
}
