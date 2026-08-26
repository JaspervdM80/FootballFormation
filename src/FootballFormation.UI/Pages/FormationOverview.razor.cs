using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace FootballFormation.UI.Pages;

public partial class FormationOverview
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private ILogger<FormationOverview> Logger { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; set; } = new();
    private bool IsAnonymous { get; set; }

    /// This page has no circuit to hand a string to a script through, so the composed text goes into a hidden element for clipboard.js.
    /// Null before the game loads, or for a fixture with no score to report.
    private string? SummaryText { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        IsAnonymous = !authState.User.IsAdmin();

        var result = await GameService.GetByIdAsync(GameId, Cancellation);

        // Not a missing game — the visitor left. Redirecting would move them again.
        if (result.IsCancelled) return;

        if (result.IsFailure || result.Value is null)
        {
            // No message travels with the redirect: without a circuit there is no snackbar that outlives the navigation.
            Logger.LogWarning("Game {GameId} not found for overview", GameId);
            Trail.Redirect(AppRoutes.Games);
            return;
        }

        GameData = result.Value;

        foreach (var period in GameData.Periods)
        {
            PeriodLineups[period.Id] = period.PlayerPositions.ToList();
        }

        if (GameData.HasFinalScore)
        {
            // Always false: the summary is for sharing, so it is never built from private notes, whoever is looking at this page.
            var commentsResult = await GameService.GetCommentsAsync(GameId, includePrivate: false, Cancellation);
            if (commentsResult.IsCancelled) return;

            var comments = commentsResult.IsSuccess ? commentsResult.Value! : [];
            var summary = MatchSummaryReport.Build(GameData, comments);
            SummaryText = MatchSummaryTextBuilder.Build(GameData, summary, L);
        }
    }

    /// Only reached on a deep link, which a shared overview usually is: an admin landing here cold is most likely on their way to edit.
    private string BackFallback => IsAnonymous ? AppRoutes.Games : AppRoutes.Formation(GameId);
}
