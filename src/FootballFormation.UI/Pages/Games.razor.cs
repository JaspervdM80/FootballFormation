using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Games
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private bool _isAdmin;

    private List<Game>? _games;

    protected override async Task OnInitializedCoreAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.IsAdmin();
    }

    protected override async Task LoadAsync()
    {
        // Details variant loads the period lineups so we can flag games missing one.
        var result = await GameService.GetAllWithDetailsAsync(SeasonId);
        _games = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

    /// <summary>A game that has already been played but has no lineup entered — its playing
    /// time can't be computed, so the data is incomplete. Future games are legitimately empty.</summary>
    private static bool IsIncomplete(Game game) =>
        game.Date.Date < DateTime.Today && !game.HasLineup;

    /// <summary>
    /// Whether the scoreline is settled. A live match writes `ScoreHome`/`ScoreAway` as the goals
    /// go in, so a score on its own only means the game has *started* — the state has to be
    /// checked too. Once it is settled there is nothing left to run live, and the result page is
    /// where the game's information lives.
    /// </summary>
    private static bool HasFinalScore(Game game) =>
        game.MatchState != MatchState.InProgress
        && game.ScoreHome.HasValue
        && game.ScoreAway.HasValue;

    private async Task OpenAddDialog()
    {
        var game = await ShowGameDialogAsync(L["New Game"]);
        if (game is null) return;

        var result = await GameService.CreateAsync(game);
        Snackbar.Report(L, result, L["Game vs {0} created", game.Opponent]);
        await LoadAsync();
    }

    private async Task OpenEditDialog(Game game)
    {
        var updated = await ShowGameDialogAsync(L["Edit Game"], game);
        if (updated is null) return;

        var result = await GameService.UpdateAsync(updated);
        Snackbar.Report(L, result, L["Game vs {0} updated", updated.Opponent]);
        await LoadAsync();
    }

    /// <summary>Row click: a match under way beats everything; then finished games open the
    /// result; admins build formations; visitors get the overview.</summary>
    private void OpenGame(Game game)
    {
        if (game.MatchState == MatchState.InProgress)
            OpenLive(game.Id);
        else if (HasFinalScore(game))
            OpenResult(game.Id);
        else if (_isAdmin)
            OpenFormation(game.Id);
        else
            OpenOverview(game.Id);
    }

    private async Task DeleteGame(Game game)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Game"],
            L["Are you sure you want to delete the game vs {0}?", game.Opponent]);
        if (!confirmed) return;

        var result = await GameService.DeleteAsync(game.Id);
        Snackbar.Report(L, result, L["Game vs {0} deleted", game.Opponent], Severity.Warning);
        await LoadAsync();
    }

    private void OpenFormation(int gameId) => Navigation.NavigateTo(AppRoutes.Formation(gameId));

    private void OpenOverview(int gameId) => Navigation.NavigateTo(AppRoutes.Overview(gameId));

    private void OpenResult(int gameId) => Navigation.NavigateTo(AppRoutes.Result(gameId));

    private void OpenLive(int gameId) => Navigation.NavigateTo(AppRoutes.Live(gameId));

    /// <summary>Returns the edited game, or null when the dialog was cancelled.</summary>
    private async Task<Game?> ShowGameDialogAsync(string title, Game? game = null)
    {
        return await DialogService.PromptAsync<GameDialog, Game>(title, p =>
        {
            if (game is not null) p.Add(x => x.Game, game);
        });
    }
}
