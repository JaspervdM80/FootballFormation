using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Games
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private List<Game>? _games;

    protected override async Task OnInitializedAsync() => await LoadGames();

    private async Task LoadGames()
    {
        var result = await GameService.GetAllAsync();
        _games = Snackbar.ReportFailure(result) ? result.Value : [];
    }

    private async Task OpenAddDialog()
    {
        var game = await ShowGameDialogAsync("New Game");
        if (game is null) return;

        var result = await GameService.CreateAsync(game);
        Snackbar.Report(result, $"Game vs {game.Opponent} created");
        await LoadGames();
    }

    private async Task OpenEditDialog(Game game)
    {
        var updated = await ShowGameDialogAsync("Edit Game", game);
        if (updated is null) return;

        var result = await GameService.UpdateAsync(updated);
        Snackbar.Report(result, $"Game vs {updated.Opponent} updated");
        await LoadGames();
    }

    private async Task DeleteGame(Game game)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            "Delete Game",
            $"Are you sure you want to delete the game vs {game.Opponent}?");
        if (!confirmed) return;

        var result = await GameService.DeleteAsync(game.Id);
        Snackbar.Report(result, $"Game vs {game.Opponent} deleted", Severity.Warning);
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
