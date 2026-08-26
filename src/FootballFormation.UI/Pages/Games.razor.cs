using Microsoft.AspNetCore.Components.Authorization;

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
        // The details variant loads the period line-ups, which is what lets a game missing one be flagged.
        var result = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
        _games = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

    private DateTime Today => Time.GetLocalNow().Date;

    /// Played but with no line-up entered, so no playing time can be computed. A future game is legitimately empty.
    private bool IsIncomplete(Game game) =>
        game.Date.Date < Today && !game.HasLineup;

    private sealed record GameSection(string Title, List<Game> Games);

    /// Two lists, soonest-first and newest-first, because a single one has to put one of them at the wrong end. The scoreline decides
    /// which, not the calendar — so a match never played stays among the fixtures, which is the prompt to delete it.
    private IEnumerable<GameSection> Sections()
    {
        if (_games is null) yield break;

        var fixtures = _games.Where(game => !game.HasFinalScore).OldestFirst();
        if (fixtures.Count > 0) yield return new GameSection(L["Fixtures"], fixtures);

        var results = _games.Where(game => game.HasFinalScore).NewestFirst();
        if (results.Count > 0) yield return new GameSection(L["Results"], results);
    }

    /// The live screen runs a real clock, so opening it on a fixture weeks out would bank minutes against a match nobody is playing.
    private bool IsMatchDay(Game game) => game.Date.Date == Today;

    /// No result to read and none to enter yet, so the card leaves the result page out. MatchResult says the same to anyone arriving by URL.
    private bool IsFuture(Game game) => game.Date.Date > Today;

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

    /// A match under way beats everything; then finished games open the result; admins build formations; visitors get the overview.
    private void OpenGame(Game game)
    {
        if (game.MatchState == MatchState.InProgress)
            OpenLive(game.Id);
        else if (game.HasFinalScore)
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

    /// Null when the dialog was cancelled.
    private async Task<Game?> ShowGameDialogAsync(string title, Game? game = null)
    {
        return await DialogService.PromptAsync<GameDialog, Game>(title, p =>
        {
            if (game is not null) p.Add(x => x.Game, game);
        });
    }
}
