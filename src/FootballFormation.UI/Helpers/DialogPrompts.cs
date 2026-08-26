using FootballFormation.UI.Components;

namespace FootballFormation.UI.Helpers;

/// Shorthand for the standard prompts, so pages state intent instead of wiring parameters.
public static class DialogPrompts
{
    /// For a destructive action. False when cancelled.
    public static Task<bool> ConfirmDeleteAsync(
        this IDialogService dialogService,
        string title,
        string message) =>
        dialogService.ConfirmAsync(title, message, "Delete", Color.Error);

    /// For an action that is significant but not destructive, so the button says what will happen rather than "Delete".
    /// <paramref name="buttonText"/> is a resource key, like every string ConfirmDialog is given.
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

    /// Null when the user cancelled. Dialogs here never persist anything themselves — they hand a value back and the page saves it — so
    /// unwrapping the result is the same six lines everywhere.
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
