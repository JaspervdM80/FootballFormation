using FootballFormation.UI.State;
using Microsoft.JSInterop;

namespace FootballFormation.UI.Components;

/// The browser half of match notifications, rendered from the start page as the invitation and from /settings as the switch. One
/// component because push.js holds a single page reference, and two copies of that handshake would fight over it.
public partial class MatchNotifications
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private TeamState Team { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// Whether this placement owns turning them off again. The start page only invites; /settings is where a follower comes back to.
    [Parameter] public bool Manage { get; set; }

    /// Set to wrap the row in a settings section of its own, which is how /settings places it.
    [Parameter] public string? Heading { get; set; }

    /// What push.js last answered. Null until the first render has asked, which keeps the row out of the markup rather than flashing a
    /// wrong label — the browser is the only thing that knows, and the server prerenders before it can be asked.
    private string? _state;

    private bool On => _state == "on";

    /// Everything but "this browser cannot do push at all", which is drawn as nothing rather than as a switch that would not move.
    private bool Renderable => _state is "unset" or "on" or "off" or "install-first" or "blocked";

    /// The start page invites only where no choice has been made yet — once there is one, /settings is the only place it lives. Read
    /// from the first answer and then left alone, so answering here does not take the row out from under the thumb that just tapped it.
    private bool _invited;

    private string Text => _state switch
    {
        "install-first" => L["Add the app to your home screen first, then notifications can be turned on."],
        "blocked" => L["Notifications are blocked in your browser settings."],
        "on" when Manage => L["On in this browser. A message at kick-off, at every goal, and at full time."],
        "on" => L["On in this browser. Turn them off under Settings."],
        _ => L["A message at kick-off, at every goal, and at full time."],
    };

    private DotNetObjectReference<MatchNotifications>? _self;

    protected override async Task OnInitializedAsync() => await Team.EnsureLoadedAsync();

    /// Only after the first render: the prerender has no browser to ask, and calling JS before then throws. The button's own click is
    /// handled in push.js rather than here — see NotificationStateChanged.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _self = DotNetObjectReference.Create(this);

        // A browser with the script blocked, or an old one without the Push API, leaves the row hidden rather than showing a button that
        // cannot work.
        try
        {
            _state = await JS.InvokeAsync<string>("matchNotifications.bind", Cancellation, _self);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _state = null;
        }

        _invited = _state is "unset" or "install-first";
        StateHasChanged();
    }

    /// Called by push.js once it has turned notifications on or off, because the tap has to reach the permission prompt as a user
    /// gesture — which a click routed through the circuit is not.
    [JSInvokable]
    public Task NotificationStateChanged(string state)
    {
        _state = state;
        return InvokeAsync(StateHasChanged);
    }

    public override void Dispose()
    {
        _self?.Dispose();
        base.Dispose();
    }
}
