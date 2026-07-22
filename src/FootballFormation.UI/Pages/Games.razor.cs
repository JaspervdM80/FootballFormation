using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
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

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.Identity?.IsAuthenticated == true;
        await LoadGames();
    }

    private async Task LoadGames()
    {
        // Details variant loads the period lineups so we can flag games missing one.
        var result = await GameService.GetAllWithDetailsAsync();
        _games = Snackbar.ReportFailure(result) ? result.Value : [];
    }

    /// <summary>A game that has already been played but has no lineup entered — its playing
    /// time can't be computed, so the data is incomplete. Future games are legitimately empty.</summary>
    private static bool IsIncomplete(Game game) =>
        game.Date.Date < DateTime.Today && !game.HasLineup;

    private async Task OpenAddDialog()
    {
        var game = await ShowGameDialogAsync(L["New Game"]);
        if (game is null) return;

        var result = await GameService.CreateAsync(game);
        Snackbar.Report(result, L["Game vs {0} created", game.Opponent]);
        await LoadGames();
    }

    private async Task OpenEditDialog(Game game)
    {
        var updated = await ShowGameDialogAsync(L["Edit Game"], game);
        if (updated is null) return;

        var result = await GameService.UpdateAsync(updated);
        Snackbar.Report(result, L["Game vs {0} updated", updated.Opponent]);
        await LoadGames();
    }

    /// <summary>Row click: finished games open the result; admins build formations; visitors get the overview.</summary>
    private void OpenGame(Game game)
    {
        if (game.ScoreHome.HasValue && game.ScoreAway.HasValue)
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
        Snackbar.Report(result, L["Game vs {0} deleted", game.Opponent], Severity.Warning);
        await LoadGames();
    }

    private void OpenFormation(int gameId) => Navigation.NavigateTo($"/games/{gameId}/formation");

    private void OpenOverview(int gameId) => Navigation.NavigateTo($"/games/{gameId}/overview");

    private void OpenResult(int gameId) => Navigation.NavigateTo($"/games/{gameId}/result");

    /// <summary>Returns the edited game, or null when the dialog was cancelled.</summary>
    private async Task<Game?> ShowGameDialogAsync(string title, Game? game = null)
    {
        var parameters = new DialogParameters<GameDialog>();
        if (game is not null) parameters.Add(x => x.Game, game);

        var dialog = await DialogService.ShowAsync<GameDialog>(title, parameters, UiFeedback.LockedDialog);
        var result = await dialog.Result;

        return result is { Canceled: false, Data: Game edited } ? edited : null;
    }
}
