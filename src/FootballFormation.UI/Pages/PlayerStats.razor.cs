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
    private PlayerTrainingAttendance? _attendance;
    private bool _loaded;

    // A flag rather than an AuthorizeView per gated spot: two of them set a class on a container so the grid loses a track along with
    // its cell, and nothing inside the rows can reach that far up.
    private bool _isAdmin;

    private string TileColumns => !_isAdmin ? "stat-tiles-3" : _attendance is { Held: > 0 } ? "stat-tiles-5" : "";

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

        // Only asked for as an admin: the register behind it is the one read that is not public, so a visitor would earn a refusal
        // notice for a figure the page was never going to show them.
        if (_isAdmin)
        {
            var attendanceResult = await StatsService.GetPlayerTrainingAttendanceAsync(
                playerResult.Value!, SeasonId, Cancellation);
            if (attendanceResult.IsCancelled) return;

            if (_notice.ReportFailure(L, attendanceResult)) _attendance = attendanceResult.Value;
        }

        _loaded = true;
    }
}
