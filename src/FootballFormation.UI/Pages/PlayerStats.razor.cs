using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;

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

    // A flag rather than an AuthorizeView per gated spot: two of them set a class on a container
    // (.stat-tiles-3, .game-list-no-minutes) so the grid loses a track along with its cell, and
    // nothing inside the rows can reach that far up.
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

        // GetByIdAsync stays: the page is reachable for anyone on file, including someone in no
        // current squad, whom StatsService reports on separately.
        var statsResult = await StatsService.GetPlayerAsync(playerResult.Value!, SeasonId, Cancellation);
        if (statsResult.IsCancelled) return;

        // Empty rather than null on failure — the markup returns early on a null _stats, before it
        // reaches <InlineNotice>, leaving a blank page instead of the reason for it.
        _stats = _notice.ReportFailure(L, statsResult)
            ? statsResult.Value!
            : PlayerStatsReport.Build(playerResult.Value!, [], SeasonSquads.Empty);

        _loaded = true;
    }
}
