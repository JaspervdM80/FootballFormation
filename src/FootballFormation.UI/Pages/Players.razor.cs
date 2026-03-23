using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Players
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ILogger<Players> Logger { get; set; } = null!;

    private static readonly DialogOptions NoBackdropClose = new() { BackdropClick = false };

    private List<Player>? _players;

    protected override async Task OnInitializedAsync()
    {
        await LoadPlayers();
    }

    private async Task LoadPlayers()
    {
        var result = await PlayerService.GetAllAsync();
        if (result.IsSuccess)
        {
            _players = result.Value;
        }
        else
        {
            Snackbar.Add(result.Error!, Severity.Error);
            _players = [];
        }
    }

    private async Task OpenAddDialog()
    {
        var parameters = new DialogParameters<PlayerDialog>();
        var dialog = await DialogService.ShowAsync<PlayerDialog>("Add Player", parameters, NoBackdropClose);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Player player })
        {
            var createResult = await PlayerService.CreateAsync(player);
            if (createResult.IsSuccess)
            {
                Snackbar.Add($"Player {player.DisplayName} added", Severity.Success);
            }
            else
            {
                Snackbar.Add(createResult.Error!, Severity.Error);
            }

            await LoadPlayers();
        }
    }

    private async Task OpenEditDialog(Player player)
    {
        var parameters = new DialogParameters<PlayerDialog>
        {
            { x => x.Player, player }
        };
        var dialog = await DialogService.ShowAsync<PlayerDialog>("Edit Player", parameters, NoBackdropClose);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Player updated })
        {
            var updateResult = await PlayerService.UpdateAsync(updated);
            if (updateResult.IsSuccess)
            {
                Snackbar.Add($"Player {updated.DisplayName} updated", Severity.Success);
            }
            else
            {
                Snackbar.Add(updateResult.Error!, Severity.Error);
            }

            await LoadPlayers();
        }
    }

    private async Task DeletePlayer(Player player)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, $"Are you sure you want to delete {player.DisplayName}?" },
            { x => x.ButtonText, "Delete" },
            { x => x.Color, Color.Error }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Delete Player", parameters);
        var result = await dialog.Result;

        if (result is { Canceled: false })
        {
            var deleteResult = await PlayerService.DeleteAsync(player.Id);
            if (deleteResult.IsSuccess)
            {
                Snackbar.Add($"Player {player.DisplayName} deleted", Severity.Warning);
            }
            else
            {
                Snackbar.Add(deleteResult.Error!, Severity.Error);
            }

            await LoadPlayers();
        }
    }
}
