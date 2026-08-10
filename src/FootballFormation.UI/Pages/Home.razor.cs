using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FootballFormation.UI.Pages;

/// <summary>
/// The landing page. Its only moving part is the live banner: whenever a match is being played,
/// the home page is the shortest route to it for anyone who was sent the site rather than a link
/// to the game.
/// </summary>
public partial class Home
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private Game? TodaysGame { get; set; }

    private bool IsLive => TodaysGame?.MatchState == MatchState.InProgress;

    /// <summary>Only a match actually being played gets the loud treatment.</summary>
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

    /// <summary>The score in venue order — ours first at home, the opponent's first away.</summary>
    private string LiveScore
    {
        get
        {
            if (TodaysGame is null) return "";
            var ours = TodaysGame.ScoreHome ?? 0;
            var theirs = TodaysGame.ScoreAway ?? 0;
            return TodaysGame.IsHomeGame ? $"{ours} – {theirs}" : $"{theirs} – {ours}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadTodaysGameAsync();

        // Any live-match change at all, not just this game's: the banner has no game of its own
        // until it loads one, and a match starting is exactly the event it must not miss.
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

    /// <summary>
    /// Where the banner goes: the live screen while there is still a match to follow — before
    /// kick-off too, and for spectators as much as the coach — and the result once it is over.
    /// </summary>
    private void OpenTodaysMatch()
    {
        if (TodaysGame is null) return;

        Navigation.NavigateTo(TodaysGame.MatchState == MatchState.Finished
            ? AppRoutes.Result(TodaysGame.Id)
            : AppRoutes.Live(TodaysGame.Id));
    }

    /// <summary>The banner is a div, so it needs the keyboard activation a button would give it.</summary>
    private void OpenTodaysMatchOnKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ") OpenTodaysMatch();
    }

    private void Open(string url) => Navigation.NavigateTo(url);

    /// <summary>Same reason as the banner: the tiles are divs standing in for links.</summary>
    private void OpenOnKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e, string url)
    {
        if (e.Key is "Enter" or " ") Open(url);
    }

    public override void Dispose()
    {
        Notifier.Changed -= OnLiveChanged;
        base.Dispose();
    }
}
