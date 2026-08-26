using FootballFormation.UI.State;

namespace FootballFormation.UI.Pages;

public partial class GameDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;

    [Parameter]
    public Game? Game { get; set; }

    private MudForm Form { get; set; } = null!;
    private string Opponent { get; set; } = string.Empty;
    private DateTime? Date { get; set; } = DateTime.Today;

    /// Kept apart from <see cref="Date"/> so a blank field round-trips as no time at all rather than midnight — see Game.HasStartTime.
    private string? StartTimeText { get; set; }
    private FormationType SelectedFormationType { get; set; } = FormationType.F442;
    private GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    private MatchType SelectedMatchType { get; set; } = MatchType.Competition;
    private int GameDurationMinutes { get; set; } = 60;
    private bool IsHomeGame { get; set; } = true;
    private IReadOnlyCollection<int> UnavailablePlayerIds { get; set; } = [];
    private IReadOnlyCollection<int> GuestPlayerIds { get; set; } = [];

    private List<Season> Seasons { get; set; } = [];

    /// 0 is "auto by date", resolved by GameService.CreateAsync. Editing a game shows its real season, and changing the date never
    /// silently moves it — reassigning is an explicit choice.
    private int SelectedSeasonId { get; set; }

    /// Reloaded whenever the season or the date changes, so the two player pickers always offer that season's people.
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;

    /// True once a date resolves to no season at all — a fixture opening a new season.
    private bool SeasonNotCreatedYet { get; set; }

    /// Tracks whose defaults are in the form, so they are re-applied only when the game actually moves to a different season.
    private int _defaultsFromSeasonId;

    /// Injured players are left out — already out of the roster for every game, so offering them here says the same thing twice.
    private List<Player> SquadPlayers => [.. Squad.FullMembers.Where(p => !Squad.IsInjured(p.Id))];
    private List<Player> GuestPlayers => Squad.Guests;

    protected override async Task OnInitializedAsync()
    {
        await SeasonState.EnsureLoadedAsync();

        var seasonsResult = await SeasonService.GetAllAsync(Cancellation);
        if (seasonsResult.IsSuccess)
        {
            Seasons = seasonsResult.Value!;
        }

        if (Game is null)
        {
            // Preferences are per season and the match day comes out of them, so a season has to be picked before the date exists. The
            // date that follows lands inside it, so "Auto (by date)" still resolves to the same one.
            var seasonId = SeasonState.SelectedSeasonId
                ?? Seasons.FirstOrDefault(s => s.IsCurrent)?.Id
                ?? Seasons.FirstOrDefault()?.Id
                ?? 0;

            await ApplySeasonDefaultsAsync(seasonId);

            var dateResult = await PreferencesService.GetNextMatchDateAsync(seasonId, Cancellation);
            if (dateResult.IsSuccess)
            {
                Date = dateResult.Value;
            }
        }

        // Runs after OnParametersSet has copied an existing game's season and date in.
        await ReloadSquadAsync();
    }

    private Task OnSeasonSelected(int seasonId)
    {
        SelectedSeasonId = seasonId;
        return ReloadSquadAsync();
    }

    private Task OnDateChanged(DateTime? date)
    {
        Date = date;

        // Only matters while the season is left on "auto by date".
        return SelectedSeasonId == 0 ? ReloadSquadAsync() : Task.CompletedTask;
    }

    private async Task ReloadSquadAsync()
    {
        var seasonId = SelectedSeasonId;
        SeasonNotCreatedYet = false;

        if (seasonId == 0)
        {
            // FindForDateAsync, not GetOrCreateForDateAsync: typing a date into a dialog the user may still cancel must never create a
            // season. CreateAsync does that on save.
            var seasonResult = await SeasonService.FindForDateAsync(Date ?? DateTime.Today, Cancellation);
            seasonId = seasonResult.IsSuccess ? seasonResult.Value?.Id ?? 0 : 0;
            SeasonNotCreatedYet = seasonId == 0;
        }

        if (seasonId == 0)
        {
            Squad = SeasonSquad.Empty;
        }
        else
        {
            var squadResult = await SquadService.GetSquadAsync(seasonId, Cancellation);
            Squad = squadResult.IsSuccess ? squadResult.Value! : SeasonSquad.Empty;
        }

        // Only for a new game: an existing game's formation, split and duration are history, and a season change must not rewrite them.
        if (Game is null && seasonId != 0 && seasonId != _defaultsFromSeasonId)
            await ApplySeasonDefaultsAsync(seasonId);

        // Drops ids not valid for this season, so switching cannot smuggle a stale one through to Submit. Deliberately not filtered on
        // injury: a player marked injured after the fact must not erase that this game already recorded her as absent.
        UnavailablePlayerIds = [.. UnavailablePlayerIds.Where(Squad.IsFullMember)];
        GuestPlayerIds = [.. GuestPlayerIds.Where(id => Squad.Contains(id) && Squad.IsGuest(id))];

        StateHasChanged();
    }

    /// Fills formation, split and duration from a season's preferences.
    private async Task ApplySeasonDefaultsAsync(int seasonId)
    {
        if (seasonId <= 0) return;

        var prefsResult = await PreferencesService.GetAsync(seasonId, Cancellation);
        if (prefsResult.IsFailure) return;

        var prefs = prefsResult.Value!;
        SelectedFormationType = prefs.DefaultFormation;
        SplitType = prefs.DefaultSplitType;
        GameDurationMinutes = prefs.GameDurationMinutes;
        _defaultsFromSeasonId = seasonId;
    }

    protected override void OnParametersSet()
    {
        if (Game is not null)
        {
            Opponent = Game.Opponent;
            Date = Game.Date;
            StartTimeText = Game.HasStartTime ? Game.Date.ToString("HH:mm") : null;
            SelectedFormationType = Game.FormationType;
            SplitType = Game.SplitType;
            SelectedMatchType = Game.MatchType;
            GameDurationMinutes = Game.GameDurationMinutes;
            IsHomeGame = Game.IsHomeGame;
            SelectedSeasonId = Game.SeasonId;
            UnavailablePlayerIds = Game.UnavailablePlayerIds.ToList();
            GuestPlayerIds = Game.GuestPlayerIds.ToList();
        }
    }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        var game = Game ?? new Game { Opponent = Opponent };
        game.Opponent = Opponent;
        var startTime = TimeSpan.TryParse(StartTimeText, out var parsed) ? parsed : TimeSpan.Zero;
        game.Date = (Date ?? DateTime.Today).Date + startTime;
        game.FormationType = SelectedFormationType;
        game.SplitType = SplitType;
        game.MatchType = SelectedMatchType;
        game.GameDurationMinutes = GameDurationMinutes;
        game.IsHomeGame = IsHomeGame;
        game.SeasonId = SelectedSeasonId;
        game.UnavailablePlayerIds = UnavailablePlayerIds.ToList();
        game.GuestPlayerIds = GuestPlayerIds.ToList();

        MudDialog.Close(DialogResult.Ok(game));
    }

    private void Cancel() => MudDialog.Cancel();
}
