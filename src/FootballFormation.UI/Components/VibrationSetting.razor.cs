using Microsoft.JSInterop;

namespace FootballFormation.UI.Components;

/// Per browser, like the notifications themselves: nobody signs in to follow a match, so there is no account to keep it on.
public partial class VibrationSetting
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// Unsupported until the browser has answered — the prerender cannot ask.
    private VibrationState _state;

    private bool On => _state is VibrationState.On;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            _state = await JS.InvokeAsync<string>("vibration.state", Cancellation) switch
            {
                "on" => VibrationState.On,
                "off" => VibrationState.Off,
                "ios" => VibrationState.Ios,
                _ => VibrationState.Unsupported,
            };
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _state = VibrationState.Unsupported;
        }

        StateHasChanged();
    }

    private async Task Toggle()
    {
        if (_state is not (VibrationState.On or VibrationState.Off)) return;

        try
        {
            await JS.InvokeVoidAsync("vibration.set", Cancellation, !On);
            _state = On ? VibrationState.Off : VibrationState.On;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            // Left showing what is actually stored.
        }
    }

    private enum VibrationState
    {
        Unsupported,
        Ios,
        Off,
        On,
    }
}
