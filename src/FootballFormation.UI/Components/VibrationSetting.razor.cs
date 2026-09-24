using Microsoft.JSInterop;

namespace FootballFormation.UI.Components;

/// Per browser, like the notifications themselves: nobody signs in to follow a match, so there is no account to keep it on.
public partial class VibrationSetting
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// Null until the browser has answered, and for good where it has nothing to vibrate — the prerender cannot ask.
    private bool? _on;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            _on = await JS.InvokeAsync<string>("vibration.state", Cancellation) switch
            {
                "on" => true,
                "off" => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _on = null;
        }

        StateHasChanged();
    }

    private async Task Toggle()
    {
        if (_on is not { } on) return;

        try
        {
            await JS.InvokeVoidAsync("vibration.set", Cancellation, !on);
            _on = !on;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            // Left showing what is actually stored.
        }
    }
}
