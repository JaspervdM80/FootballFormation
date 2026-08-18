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

public partial class PlayerStats
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private ILogger<PlayerStats> Logger { get; set; } = null!;
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

        if (playerResult.IsFailure)
        {
            // A notice here would render on a page that is about to redirect away, so the log is
            // the only place this can be said — same as the match report's missing game.
            Logger.LogWarning("Player {PlayerId} not found for statistics", PlayerId);
            Trail.Redirect(AppRoutes.Players);
            return;
        }

        // Squads carry per-season guest status, which decides whether a game counts towards this
        // player's available minutes. GetByIdAsync stays: the page is reachable for anyone on file,
        // including someone who is in no current squad.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonId, Cancellation);
        var squads = _notice.ReportFailure(L, squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        var games = _notice.ReportFailure(L, gamesResult) ? gamesResult.Value! : [];

        _stats = PlayerStatsReport.Build(playerResult.Value!, games, squads);
        _loaded = true;
    }
}
