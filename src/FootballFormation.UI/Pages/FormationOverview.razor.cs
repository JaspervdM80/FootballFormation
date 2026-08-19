using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
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

    /// <summary>
    /// The copyable match summary, rendered into a hidden element for <c>clipboard.js</c> to read —
    /// this page has no circuit to hand a string to a script through, so the composed text (already
    /// through <see cref="L"/>) goes on the page instead. Null before the game has loaded or for a
    /// fixture with no score to report yet.
    /// </summary>
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

        if (GameData.ScoreHome is not null && GameData.ScoreAway is not null)
        {
            // No visitor here can see a private comment either way, so includePrivate is always
            // false — this page has no admin-ness of its own to gate it on.
            var commentsResult = await GameService.GetCommentsAsync(GameId, includePrivate: false, Cancellation);
            if (commentsResult.IsCancelled) return;

            var comments = commentsResult.IsSuccess ? commentsResult.Value! : [];
            var summary = MatchSummaryReport.Build(GameData, comments);
            SummaryText = MatchSummaryTextBuilder.Build(GameData, summary, L);
        }
    }

    /// <summary>Only reached on a deep link — a shared overview usually is one. An admin who lands
    /// here cold is most likely on their way to edit it; a visitor has no editor to go to.</summary>
    private string BackFallback => IsAnonymous ? AppRoutes.Games : AppRoutes.Formation(GameId);
}
