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

    /// <summary>The kick-off time as the native time input reads and writes it ("HH:mm"), kept
    /// apart from <see cref="Date"/> so a blank field round-trips as no time at all rather than
    /// midnight — see <c>Game.HasStartTime</c>.</summary>
    private string? StartTimeText { get; set; }
    private FormationType SelectedFormationType { get; set; } = FormationType.F442;
    private GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    private MatchType SelectedMatchType { get; set; } = MatchType.Competition;
    private int GameDurationMinutes { get; set; } = 60;
    private bool IsHomeGame { get; set; } = true;
    private IReadOnlyCollection<int> UnavailablePlayerIds { get; set; } = [];
    private IReadOnlyCollection<int> GuestPlayerIds { get; set; } = [];

    private List<Season> Seasons { get; set; } = [];

    /// <summary>0 = "auto by date", which <c>GameService.CreateAsync</c> resolves from
    /// <see cref="Date"/>. Editing a game shows its real season, and changing the date never
    /// silently moves it — reassigning is an explicit choice.</summary>
    private int SelectedSeasonId { get; set; }

    /// <summary>The squad of whichever season this game will land in. Reloaded whenever the season
    /// or the date changes, so the two player pickers always offer that season's people.</summary>
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;

    /// <summary>True once a date resolves to no season at all — a fixture opening a new season.</summary>
    private bool SeasonNotCreatedYet { get; set; }

    /// <summary>The season whose defaults are currently in the form, so they are re-applied only
    /// when the game actually moves to a different season.</summary>
    private int _defaultsFromSeasonId;

    /// <summary>Full members offered in the "Unavailable Players" picker. Injured players are left
    /// out — they are already out of the roster for every game, so offering them here would only be
    /// a second, redundant way to say the same thing.</summary>
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
            // Preferences are per season, and the date comes out of them (the match day), so the
            // season has to be picked before the date exists: the one the viewer is filtering by.
            // The date that follows lands inside that season, so "Auto (by date)" still resolves
            // to it and ReloadSquadAsync leaves the defaults alone.
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

    /// <summary>
    /// Resolves the squad for whichever season the game will land in: the explicit choice, or — on
    /// "Auto (by date)" — the season covering the date currently in the picker.
    /// </summary>
    private async Task ReloadSquadAsync()
    {
        var seasonId = SelectedSeasonId;
        SeasonNotCreatedYet = false;

        if (seasonId == 0)
        {
            // FindForDateAsync, not GetOrCreateForDateAsync: typing a date into a dialog the user
            // may still cancel must never create a season. GameService.CreateAsync creates it on save.
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

        // Formation, split and duration are season defaults too, so moving a *new* game to another
        // season re-applies them. Only for a new game: an existing game's own settings are history
        // and must never be rewritten by a season change.
        if (Game is null && seasonId != 0 && seasonId != _defaultsFromSeasonId)
            await ApplySeasonDefaultsAsync(seasonId);

        // Drop selections that aren't valid for this season, so switching can't smuggle a stale id
        // through to Submit. (Post-backfill nobody is outside a squad, but a later removal could.)
        // Deliberately not filtered on injury: an existing game's stored UnavailablePlayerIds is
        // history — a player marked injured after the fact must not silently erase that this game
        // already recorded them as absent. SquadPlayers below is what stops a *new* selection.
        UnavailablePlayerIds = [.. UnavailablePlayerIds.Where(Squad.IsFullMember)];
        GuestPlayerIds = [.. GuestPlayerIds.Where(id => Squad.Contains(id) && Squad.IsGuest(id))];

        StateHasChanged();
    }

    /// <summary>Fills formation, split and duration from a season's preferences.</summary>
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
