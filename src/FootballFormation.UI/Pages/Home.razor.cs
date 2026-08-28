namespace FootballFormation.UI.Pages;

/// The live banner is the only moving part: whenever a match is on, this is the shortest route to it for anyone sent the site rather
/// than a link to the game.
public partial class Home
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private State.TeamState Team { get; set; } = null!;

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

    private void OnLiveChanged(int gameId) => _ = InvokeAsync(async () =>
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
        base.Dispose();
    }
}
