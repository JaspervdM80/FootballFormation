using Microsoft.JSInterop;

namespace FootballFormation.UI.Pages;

/// The live banner is the only moving part: whenever a match is on, this is the shortest route to it for anyone sent the site rather
/// than a link to the game.
public partial class Home
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private State.TeamState Team { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// What push.js last answered. Null until the first render has asked, which keeps the row out of the markup rather than flashing a
    /// wrong label — the browser is the only thing that knows, and the server prerenders this page before it can be asked.
    private string? NotificationState { get; set; }

    private bool NotificationsOn => NotificationState == "on";

    private Game? TodaysGame { get; set; }

    private bool IsLive => TodaysGame?.MatchState == MatchState.InProgress;

    /// Only a match actually being played gets the loud treatment.
    private string BannerCssClass => TodaysGame?.MatchState switch
    {
        MatchState.InProgress => "",
        MatchState.Finished => "home-banner-done",
        _ => "home-banner-upcoming"
    };

    private string BannerLabel => TodaysGame?.MatchState switch
    {
        MatchState.InProgress => L["Live now"],
        MatchState.Finished => L["Full time"],
        _ => L["Today"]
    };

    /// The score in venue order — ours first at home, the opponent's first away.
    private string LiveScore => TodaysGame?.ScoreboardOrder().ToString() ?? "";

    protected override async Task OnInitializedAsync()
    {
        await Team.EnsureLoadedAsync();
        await LoadTodaysGameAsync();

        // Any live-match change, not just this game's: the banner has no game of its own until it loads one, and a match starting is
        // exactly the event it must not miss.
        Notifier.Changed += OnLiveChanged;
    }

    private async Task LoadTodaysGameAsync()
    {
        var result = await Live.GetTodaysMatchAsync(Cancellation);
        TodaysGame = result.IsSuccess ? result.Value : null;
    }

    private DotNetObjectReference<Home>? _self;

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
            NotificationState = await JS.InvokeAsync<string>("matchNotifications.bind", Cancellation, _self);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            NotificationState = null;
        }

        StateHasChanged();
    }

    /// Called by push.js once it has turned notifications on or off, because the tap has to reach the permission prompt as a user
    /// gesture — which a click routed through the circuit is not.
    [JSInvokable]
    public Task NotificationStateChanged(string state)
    {
        NotificationState = state;
        return InvokeAsync(StateHasChanged);
    }

    private void OnLiveChanged(int gameId, LiveMatchEvent change) => _ = InvokeAsync(async () =>
    {
        await LoadTodaysGameAsync();
        StateHasChanged();
    });

    /// The live screen while there is still a match to follow — before kick-off too, and for spectators as much as the coach — then the result.
    private string TodaysMatchUrl => TodaysGame is null
        ? AppRoutes.Home
        : TodaysGame.MatchState == MatchState.Finished
            ? AppRoutes.Result(TodaysGame.Id)
            : AppRoutes.Live(TodaysGame.Id);

    public override void Dispose()
    {
        Notifier.Changed -= OnLiveChanged;
        _self?.Dispose();
        base.Dispose();
    }
}
