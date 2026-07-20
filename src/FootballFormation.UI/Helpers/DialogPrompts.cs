using FootballFormation.UI.Components;
using MudBlazor;

namespace FootballFormation.UI.Helpers;

/// <summary>Shorthand for the standard prompts, so pages state intent instead of wiring parameters.</summary>
public static class DialogPrompts
{
    /// <summary>Asks the user to confirm a destructive action. Returns false when cancelled.</summary>
    public static async Task<bool> ConfirmDeleteAsync(
        this IDialogService dialogService,
        string title,
        string message)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, message },
            { x => x.ButtonText, "Delete" },
            { x => x.Color, Color.Error }
        };

        var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters);
        var result = await dialog.Result;

        return result is { Canceled: false };
    }
}
