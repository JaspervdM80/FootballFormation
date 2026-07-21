using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Players
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private List<Player>? _players;

    protected override async Task OnInitializedAsync() => await LoadPlayers();

    private void OnRowClicked(TableRowClickEventArgs<Player> args)
    {
        if (args.Item is not null)
            Navigation.NavigateTo($"/players/{args.Item.Id}/stats");
    }

    private async Task LoadPlayers()
    {
        var result = await PlayerService.GetAllAsync();
        _players = Snackbar.ReportFailure(result) ? result.Value : [];
    }

    private async Task OpenAddDialog()
    {
        var player = await ShowPlayerDialogAsync(L["Add Player"]);
        if (player is null) return;

        var result = await PlayerService.CreateAsync(player);
        Snackbar.Report(result, L["Player {0} added", player.DisplayName]);
        await LoadPlayers();
    }

    private async Task OpenEditDialog(Player player)
    {
        var updated = await ShowPlayerDialogAsync(L["Edit Player"], player);
        if (updated is null) return;

        var result = await PlayerService.UpdateAsync(updated);
        Snackbar.Report(result, L["Player {0} updated", updated.DisplayName]);
        await LoadPlayers();
    }

    private async Task DeletePlayer(Player player)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Player"],
            L["Are you sure you want to delete {0}?", player.DisplayName]);
        if (!confirmed) return;

        var result = await PlayerService.DeleteAsync(player.Id);
        Snackbar.Report(result, L["Player {0} deleted", player.DisplayName], Severity.Warning);
        await LoadPlayers();
    }

    /// <summary>Returns the edited player, or null when the dialog was cancelled.</summary>
    private async Task<Player?> ShowPlayerDialogAsync(string title, Player? player = null)
    {
        var parameters = new DialogParameters<PlayerDialog>();
        if (player is not null) parameters.Add(x => x.Player, player);

        var dialog = await DialogService.ShowAsync<PlayerDialog>(title, parameters, UiFeedback.LockedDialog);
        var result = await dialog.Result;

        return result is { Canceled: false, Data: Player edited } ? edited : null;
    }
}
