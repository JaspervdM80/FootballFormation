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

    /// Further rows for that section, drawn under this one. Only used alongside <see cref="Heading"/>.
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// What push.js can answer. Starts <see cref="NotificationState.Unsupported"/> and stays there until the first interactive render
    /// has asked, which keeps the row out of the markup rather than flashing a wrong label: the browser is the only thing that knows,
    /// and the server prerenders before it can be asked.
    private NotificationState _state;

    private bool On => _state is NotificationState.On;

    /// Anything but "this browser cannot do push at all", which is drawn as nothing rather than as a switch that would not move.
    private bool Renderable => _state is not NotificationState.Unsupported;

    /// The start page invites only where no choice has been made yet — once there is one, /settings is the only place it lives. Read
    /// from the first answer and then left alone, so answering here does not take the row out from under the thumb that just tapped it.
    private bool _invited;

    /// push.js's word for the state the button would move away from, which is what its delegated click handler reads. The other
    /// direction of the same contract is <see cref="Parse"/>.
    private string Toggle => On ? "on" : "off";

    private string Text => _state switch
    {
        NotificationState.InstallFirst => L["Add the app to your home screen first, then notifications can be turned on."],
        NotificationState.Blocked => L["Notifications are blocked in your browser settings."],
        NotificationState.On when Manage => L["On in this browser. A message at kick-off, at every goal, and at full time."],
        NotificationState.On => L["On in this browser. Turn them off under Settings."],
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
            _state = Parse(await JS.InvokeAsync<string>("matchNotifications.bind", Cancellation, _self));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _state = NotificationState.Unsupported;
        }

        _invited = _state is NotificationState.Unset or NotificationState.InstallFirst;
        StateHasChanged();
    }

    /// Called by push.js once it has turned notifications on or off, because the tap has to reach the permission prompt as a user
    /// gesture — which a click routed through the circuit is not.
    [JSInvokable]
    public Task NotificationStateChanged(string state)
    {
        _state = Parse(state);
        return InvokeAsync(StateHasChanged);
    }

    /// The one place push.js's vocabulary is written down on this side. Anything else is silence rather than a switch that would not
    /// move, which is also what a browser with no Push API at all answers.
    private static NotificationState Parse(string? answer) => answer switch
    {
        "unset" => NotificationState.Unset,
        "install-first" => NotificationState.InstallFirst,
        "blocked" => NotificationState.Blocked,
        "off" => NotificationState.Off,
        "on" => NotificationState.On,
        _ => NotificationState.Unsupported,
    };

    public override void Dispose()
    {
        _self?.Dispose();
        base.Dispose();
    }

    /// <see cref="Unset"/> is the permission prompt never having been answered — turning them off again after a yes leaves the
    /// permission granted, which is a choice, and reads as <see cref="Off"/>.
    private enum NotificationState
    {
        Unsupported,
        Unset,
        InstallFirst,
        Blocked,
        Off,
        On,
    }
}
