using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class FormationOverview
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILogger<FormationOverview> Logger { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; set; } = new();
    private bool IsAnonymous { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        IsAnonymous = authState.User.Identity?.IsAuthenticated != true;

        var result = await GameService.GetByIdAsync(GameId);
        if (result.IsFailure || result.Value is null)
        {
            Logger.LogWarning("Game {GameId} not found for overview", GameId);
            Snackbar.Add("Game not found", Severity.Error);
            Navigation.NavigateTo("/games");
            return;
        }

        GameData = result.Value;

        foreach (var period in GameData.Periods)
        {
            PeriodLineups[period.Id] = period.PlayerPositions.ToList();
        }
    }

    private void NavigateToEditor() => Navigation.NavigateTo($"/games/{GameId}/formation");
    private void NavigateToGames() => Navigation.NavigateTo("/games");

    private async Task CaptureScreenshot()
    {
        try
        {
            await JS.InvokeVoidAsync("captureFormationOverview", "formation-overview");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to capture screenshot");
            Snackbar.Add("Screenshot failed — try using your device's screenshot instead", Severity.Warning);
        }
    }
}
