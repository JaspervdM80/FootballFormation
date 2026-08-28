using FootballFormation.UI.Theming;

namespace FootballFormation.UI.State;

/// Who the app says it is: the club and team in the app bar, the drawer, the home page, the install banner and the web manifest all read
/// it from here, so a rename lands in one place. Scoped and memoized for the same reason <see cref="SeasonState"/> is — the chrome and
/// the page both need it during their own OnInitializedAsync.
public class TeamState(TeamService teams)
{
    /// Only reachable before <see cref="TeamService.EnsureSeededAsync"/> has run, which startup does before the app serves anything.
    private const string Unnamed = "Football Formation";

    private Task? _loading;

    public Team? Current { get; private set; }

    /// "GJS MO15-2".
    public string DisplayName => Current?.FullName ?? Unnamed;

    public string LogoUrl => ClubTheme.LogoFor(Current?.Club);

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    /// Re-reads after /teams renames a club, so the page can tell whether the chrome it was rendered beside is now out of date.
    public async Task RefreshAsync()
    {
        _loading = null;
        await EnsureLoadedAsync();
    }

    private async Task LoadAsync()
    {
        var result = await teams.GetCurrentAsync();

        // Swallowed on purpose, as in SeasonState: the service has already logged it, and the chrome falls back to a name rather than
        // failing to render.
        if (result.IsSuccess) Current = result.Value;
    }
}
