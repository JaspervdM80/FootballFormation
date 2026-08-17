using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class PlayerStats
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

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

        if (!Snackbar.ReportFailure(L, playerResult))
        {
            Trail.Redirect(AppRoutes.Players);
            return;
        }

        // Squads carry per-season guest status, which decides whether a game counts towards this
        // player's available minutes. GetByIdAsync stays: the page is reachable for anyone on file,
        // including someone who is in no current squad.
        var squadsResult = await SquadService.GetSquadsAsync(SeasonId, Cancellation);
        var squads = Snackbar.ReportFailure(L, squadsResult) ? squadsResult.Value! : SeasonSquads.Empty;

        var gamesResult = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        var games = Snackbar.ReportFailure(L, gamesResult) ? gamesResult.Value! : [];

        _stats = PlayerStatsReport.Build(playerResult.Value!, games, squads);
        _loaded = true;
    }
}
