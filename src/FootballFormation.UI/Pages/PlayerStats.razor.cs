using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

public partial class PlayerStats
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter] public int PlayerId { get; set; }

    private Core.Reporting.PlayerStats? _stats;
    private bool _loaded;

    // A flag rather than an AuthorizeView per gated spot: two of them set a class on a container so the grid loses a track along with
    // its cell, and nothing inside the rows can reach that far up.
    private bool _isAdmin;

    protected override async Task OnInitializedCoreAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.IsAdmin();
    }

    protected override async Task LoadAsync()
    {
        _loaded = false;

        var playerResult = await PlayerService.GetByIdAsync(PlayerId, Cancellation);

        // Not a missing player — the visitor left. Redirecting would move them again.
        if (playerResult.IsCancelled) return;

        if (!_notice.ReportFailure(L, playerResult))
        {
            Trail.Redirect(AppRoutes.Players);
            return;
        }

        var statsResult = await StatsService.GetPlayerAsync(playerResult.Value!, SeasonId, Cancellation);
        if (statsResult.IsCancelled) return;

        // Empty, not null: the markup returns early on a null _stats, before <InlineNotice>.
        _stats = _notice.ReportFailure(L, statsResult)
            ? statsResult.Value!
            : PlayerStatsReport.Build(playerResult.Value!, [], SeasonSquads.Empty);

        _loaded = true;
    }
}
