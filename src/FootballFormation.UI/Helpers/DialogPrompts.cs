using FootballFormation.UI.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Helpers;

/// <summary>Shorthand for the standard prompts, so pages state intent instead of wiring parameters.</summary>
public static class DialogPrompts
{
    /// <summary>Asks the user to confirm a destructive action. Returns false when cancelled.</summary>
    public static Task<bool> ConfirmDeleteAsync(
        this IDialogService dialogService,
        string title,
        string message) =>
        dialogService.ConfirmAsync(title, message, "Delete", Color.Error);

    /// <summary>
    /// Asks the user to confirm an action that is significant but not destructive — the confirm
    /// button says what will happen rather than "Delete".
    /// </summary>
    /// <param name="buttonText">A resource key, like every string ConfirmDialog is given.</param>
    public static async Task<bool> ConfirmAsync(
        this IDialogService dialogService,
        string title,
        string message,
        string buttonText,
        Color color = Color.Primary)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, message },
            { x => x.ButtonText, buttonText },
            { x => x.Color, color }
        };

        var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters);
        var result = await dialog.Result;

        return result is { Canceled: false };
    }

    /// <summary>
    /// Opens an editing dialog and returns what it produced, or <c>null</c> when the user
    /// cancelled. Dialogs in this app never persist anything themselves — they hand a value back
    /// and the page saves it (see SquadMemberDialog) — so unwrapping the result is the same six
    /// lines everywhere, and this is them.
    /// </summary>
    public static async Task<TResult?> PromptAsync<TDialog, TResult>(
        this IDialogService dialogService,
        string title,
        Action<DialogParameters<TDialog>>? configure = null)
        where TDialog : ComponentBase
        where TResult : class
    {
        var result = await ShowAsync(dialogService, title, configure);

        return result is { Canceled: false, Data: TResult value } ? value : null;
    }

    private static async Task<DialogResult?> ShowAsync<TDialog>(
        IDialogService dialogService,
        string title,
        Action<DialogParameters<TDialog>>? configure)
        where TDialog : ComponentBase
    {
        var parameters = new DialogParameters<TDialog>();
        configure?.Invoke(parameters);

        var dialog = await dialogService.ShowAsync<TDialog>(title, parameters, UiFeedback.LockedDialog);
        return await dialog.Result;
    }
}
