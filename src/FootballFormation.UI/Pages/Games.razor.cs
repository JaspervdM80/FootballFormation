using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Games
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ILogger<Games> Logger { get; set; } = null!;

    private static readonly DialogOptions NoBackdropClose = new() { BackdropClick = false };

    private List<Game>? _games;

    protected override async Task OnInitializedAsync()
    {
        await LoadGames();
    }

    private async Task LoadGames()
    {
        var result = await GameService.GetAllAsync();
        if (result.IsSuccess)
        {
            _games = result.Value;
        }
        else
        {
            Snackbar.Add(result.Error!, Severity.Error);
            _games = [];
        }
    }

    private async Task OpenAddDialog()
    {
        var parameters = new DialogParameters<GameDialog>();
        var dialog = await DialogService.ShowAsync<GameDialog>("New Game", parameters, NoBackdropClose);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Game game })
        {
            var createResult = await GameService.CreateAsync(game);
            if (createResult.IsSuccess)
            {
                Snackbar.Add($"Game vs {game.Opponent} created", Severity.Success);
            }
            else
            {
                Snackbar.Add(createResult.Error!, Severity.Error);
            }

            await LoadGames();
        }
    }

    private async Task OpenEditDialog(Game game)
    {
        var parameters = new DialogParameters<GameDialog>
        {
            { x => x.Game, game }
        };
        var dialog = await DialogService.ShowAsync<GameDialog>("Edit Game", parameters, NoBackdropClose);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Game updated })
        {
            var updateResult = await GameService.UpdateAsync(updated);
            if (updateResult.IsSuccess)
            {
                Snackbar.Add($"Game vs {updated.Opponent} updated", Severity.Success);
            }
            else
            {
                Snackbar.Add(updateResult.Error!, Severity.Error);
            }

            await LoadGames();
        }
    }

    private void OpenFormation(int gameId)
    {
        Navigation.NavigateTo($"/games/{gameId}/formation");
    }

    private void OpenOverview(int gameId)
    {
        Navigation.NavigateTo($"/games/{gameId}/overview");
    }

    private void OpenResult(int gameId)
    {
        Navigation.NavigateTo($"/games/{gameId}/result");
    }

    private async Task DeleteGame(Game game)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, $"Are you sure you want to delete the game vs {game.Opponent}?" },
            { x => x.ButtonText, "Delete" },
            { x => x.Color, Color.Error }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Delete Game", parameters);
        var result = await dialog.Result;

        if (result is { Canceled: false })
        {
            var deleteResult = await GameService.DeleteAsync(game.Id);
            if (deleteResult.IsSuccess)
            {
                Snackbar.Add($"Game vs {game.Opponent} deleted", Severity.Warning);
            }
            else
            {
                Snackbar.Add(deleteResult.Error!, Severity.Error);
            }

            await LoadGames();
        }
    }
}
