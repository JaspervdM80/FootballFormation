using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Games
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private bool _isAdmin;

    private List<Game>? _games;

    protected override async Task OnInitializedCoreAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.IsAdmin();
    }

    protected override async Task LoadAsync()
    {
        // Details variant loads the period lineups so we can flag games missing one.
        var result = await GameService.GetAllWithDetailsAsync(SeasonId);
        _games = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

    /// <summary>Read through <see cref="TimeProvider"/> for the same reason the services do it:
    /// a date nobody can control is a date nobody can test.</summary>
    private DateTime Today => Time.GetLocalNow().Date;

    /// <summary>A game that has already been played but has no lineup entered — its playing
    /// time can't be computed, so the data is incomplete. Future games are legitimately empty.</summary>
    private bool IsIncomplete(Game game) =>
        game.Date.Date < Today && !game.HasLineup;

    /// <summary>Still to be played. Today's fixture counts as upcoming all day, so a match being
    /// played sits at the top of the fixture list rather than dropping in among the results
    /// halfway through the afternoon.</summary>
    private bool IsUpcoming(Game game) => game.Date.Date >= Today;

    /// <summary>One headed block of the games list.</summary>
    private sealed record GameSection(string Title, List<Game> Games);

    /// <summary>
    /// The page reads as two lists, because a fixture and a result are two different things to
    /// look at: what is coming, soonest first, and then what has been played, in the order it was
    /// played. A single list has to put one of them at the wrong end.
    /// <para>Either block is dropped when it is empty — a season yet to start is all fixtures, and
    /// one that is over is all results.</para>
    /// </summary>
    private IEnumerable<GameSection> Sections()
    {
        if (_games is null) yield break;

        var fixtures = _games.Where(IsUpcoming).OldestFirst();
        if (fixtures.Count > 0) yield return new GameSection(L["Fixtures"], fixtures);

        var results = _games.Where(game => !IsUpcoming(game)).OldestFirst();
        if (results.Count > 0) yield return new GameSection(L["Results"], results);
    }

    /// <summary>The live screen runs a real clock and writes real substitution timings, so
    /// opening it on a fixture weeks out banks minutes against a match nobody is playing. The
    /// button that leads there only appears on the day itself.</summary>
    private bool IsMatchDay(Game game) => game.Date.Date == Today;

    /// <summary>
    /// Whether the scoreline is settled. A live match writes `ScoreHome`/`ScoreAway` as the goals
    /// go in, so a score on its own only means the game has *started* — the state has to be
    /// checked too. Once it is settled there is nothing left to run live, and the result page is
    /// where the game's information lives.
    /// </summary>
    private static bool HasFinalScore(Game game) =>
        game.MatchState != MatchState.InProgress
        && game.ScoreHome.HasValue
        && game.ScoreAway.HasValue;

    private async Task OpenAddDialog()
    {
        var game = await ShowGameDialogAsync(L["New Game"]);
        if (game is null) return;

        var result = await GameService.CreateAsync(game);
        Snackbar.Report(L, result, L["Game vs {0} created", game.Opponent]);
        await LoadAsync();
    }

    private async Task OpenEditDialog(Game game)
    {
        var updated = await ShowGameDialogAsync(L["Edit Game"], game);
        if (updated is null) return;

        var result = await GameService.UpdateAsync(updated);
        Snackbar.Report(L, result, L["Game vs {0} updated", updated.Opponent]);
        await LoadAsync();
    }

    /// <summary>Row click: a match under way beats everything; then finished games open the
    /// result; admins build formations; visitors get the overview.</summary>
    private void OpenGame(Game game)
    {
        if (game.MatchState == MatchState.InProgress)
            OpenLive(game.Id);
        else if (HasFinalScore(game))
            OpenResult(game.Id);
        else if (_isAdmin)
            OpenFormation(game.Id);
        else
            OpenOverview(game.Id);
    }

    private async Task DeleteGame(Game game)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Game"],
            L["Are you sure you want to delete the game vs {0}?", game.Opponent]);
        if (!confirmed) return;

        var result = await GameService.DeleteAsync(game.Id);
        Snackbar.Report(L, result, L["Game vs {0} deleted", game.Opponent], Severity.Warning);
        await LoadAsync();
    }

    private void OpenFormation(int gameId) => Navigation.NavigateTo(AppRoutes.Formation(gameId));

    private void OpenOverview(int gameId) => Navigation.NavigateTo(AppRoutes.Overview(gameId));

    private void OpenResult(int gameId) => Navigation.NavigateTo(AppRoutes.Result(gameId));

    private void OpenLive(int gameId) => Navigation.NavigateTo(AppRoutes.Live(gameId));

    /// <summary>Returns the edited game, or null when the dialog was cancelled.</summary>
    private async Task<Game?> ShowGameDialogAsync(string title, Game? game = null)
    {
        return await DialogService.PromptAsync<GameDialog, Game>(title, p =>
        {
            if (game is not null) p.Add(x => x.Game, game);
        });
    }
}
