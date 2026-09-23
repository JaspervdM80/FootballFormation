using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

/// On match day the banner is the shortest route to the game for anyone sent the site rather than a link to it.
public partial class Home
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private State.TeamState Team { get; set; } = null!;

    private Game? TodaysGame { get; set; }

    private HomeDashboard _dashboard = HomeDashboard.Empty;

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

    protected override async Task OnInitializedCoreAsync()
    {
        await Team.EnsureLoadedAsync();
        await LoadTodaysGameAsync();

        // Any live-match change, not just this game's: the banner has no game of its own until it loads one, and a match starting is
        // exactly the event it must not miss.
        Notifier.Changed += OnLiveChanged;
    }

    protected override async Task LoadAsync()
    {
        var result = await GameService.GetAllAsync(SeasonId, Cancellation);
        _dashboard = Snackbar.ReportFailure(L, result)
            ? HomeDashboardReport.Build(result.Value!, Now)
            : HomeDashboard.Empty;
    }

    private async Task LoadTodaysGameAsync()
    {
        var result = await Live.GetTodaysMatchAsync(Cancellation);
        TodaysGame = result.IsSuccess ? result.Value : null;
    }

    private void OnLiveChanged(int gameId, LiveMatchEvent change) => _ = InvokeAsync(async () =>
    {
        await LoadTodaysGameAsync();

        // Every change, not just full time: a goal corrected after the whistle changes the last result too.
        await LoadAsync();
        StateHasChanged();
    });

    /// The live screen while there is still a match to follow — before kick-off too, and for spectators as much as the coach — then the result.
    private string TodaysMatchUrl => TodaysGame is null
        ? AppRoutes.Home
        : TodaysGame.MatchState == MatchState.Finished
            ? AppRoutes.Result(TodaysGame.Id)
            : AppRoutes.Live(TodaysGame.Id);

    private static string CardDate(Game game) => game.DateLine("dddd d MMMM");

    private string? MeetLine(Game game) => game.MeetTime is null
        ? null
        : $"{ClockText.Of(game.MeetTime)} {(game.IsHomeGame ? L["assemble"] : L["depart"])}";

    private string RecordLabel => SeasonState.SelectedSeason?.Name ?? L["All seasons"];

    private static string ResultClass(Game game) =>
        game.ScoreHome > game.ScoreAway ? "win" : game.ScoreHome < game.ScoreAway ? "loss" : "draw";

    private string Scorers => string.Join(", ", _dashboard.LastScorers.Select(s =>
        s.Goals > 1 ? $"{s.Scorer.DisplayName} ({s.Goals})" : s.Scorer.DisplayName));

    private string GoalDifference => _dashboard.Record.GoalDifference.ToString("+0;-0;+0");

    /// By the calendar rather than IsCurrent, which a season not yet started shares with one already over.
    private string NoNextGameText => SeasonState.SelectedSeason is { } season && season.EndDate.Date < Now.Date
        ? L["No matches left in this season."]
        : L["No match planned yet."];

    private DateTime Now => Time.GetLocalNow().DateTime;

    public override void Dispose()
    {
        Notifier.Changed -= OnLiveChanged;
        base.Dispose();
    }
}
