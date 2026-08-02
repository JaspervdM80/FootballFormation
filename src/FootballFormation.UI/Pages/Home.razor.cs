using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FootballFormation.UI.Pages;

/// <summary>
/// The landing page. Its only moving part is the live banner: whenever a match is being played,
/// the home page is the shortest route to it for anyone who was sent the site rather than a link
/// to the game.
/// </summary>
public partial class Home : IDisposable
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private Game? LiveGame { get; set; }

    /// <summary>The score in venue order — ours first at home, the opponent's first away.</summary>
    private string LiveScore
    {
        get
        {
            if (LiveGame is null) return "";
            var ours = LiveGame.ScoreHome ?? 0;
            var theirs = LiveGame.ScoreAway ?? 0;
            return LiveGame.IsHomeGame ? $"{ours} – {theirs}" : $"{theirs} – {ours}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadLiveGameAsync();

        // Any live-match change at all, not just this game's: the banner has no game of its own
        // until it loads one, and a match starting is exactly the event it must not miss.
        Notifier.Changed += OnLiveChanged;
    }

    private async Task LoadLiveGameAsync()
    {
        var result = await Live.GetInProgressAsync();
        LiveGame = result.IsSuccess ? result.Value : null;
    }

    private void OnLiveChanged(int gameId) => _ = InvokeAsync(async () =>
    {
        await LoadLiveGameAsync();
        StateHasChanged();
    });

    private void OpenLiveMatch()
    {
        if (LiveGame is not null) Navigation.NavigateTo($"/games/{LiveGame.Id}/live");
    }

    /// <summary>The banner is a div, so it needs the keyboard activation a button would give it.</summary>
    private void OpenLiveMatchOnKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ") OpenLiveMatch();
    }

    public void Dispose() => Notifier.Changed -= OnLiveChanged;
}
