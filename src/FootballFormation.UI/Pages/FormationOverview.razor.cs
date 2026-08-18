using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace FootballFormation.UI.Pages;

public partial class FormationOverview
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
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
        IsAnonymous = !authState.User.IsAdmin();

        var result = await GameService.GetByIdAsync(GameId, Cancellation);

        // Not a missing game — the visitor left. Redirecting would move them again.
        if (result.IsCancelled) return;

        if (result.IsFailure || result.Value is null)
        {
            // No message travels with the redirect: this page renders without a circuit, so there
            // is no snackbar that outlives the navigation to raise one on. The games list is where
            // a broken link should land anyway, and the log has the id.
            Logger.LogWarning("Game {GameId} not found for overview", GameId);
            Trail.Redirect(AppRoutes.Games);
            return;
        }

        GameData = result.Value;

        foreach (var period in GameData.Periods)
        {
            PeriodLineups[period.Id] = period.PlayerPositions.ToList();
        }
    }

    /// <summary>Only reached on a deep link — a shared overview usually is one. An admin who lands
    /// here cold is most likely on their way to edit it; a visitor has no editor to go to.</summary>
    private string BackFallback => IsAnonymous ? AppRoutes.Games : AppRoutes.Formation(GameId);
}
