using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class GameDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;

    [Parameter]
    public Game? Game { get; set; }

    private MudForm Form { get; set; } = null!;
    private string Opponent { get; set; } = string.Empty;
    private DateTime? Date { get; set; } = DateTime.Today;
    private FormationType SelectedFormationType { get; set; } = FormationType.F442;
    private GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    private string? Notes { get; set; }
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

    private List<Player> SquadPlayers => Squad.FullMembers;
    private List<Player> GuestPlayers => Squad.Guests;

    protected override async Task OnInitializedAsync()
    {
        var seasonsResult = await SeasonService.GetAllAsync();
        if (seasonsResult.IsSuccess)
        {
            Seasons = seasonsResult.Value!;
        }

        if (Game is null)
        {
            var prefsResult = await PreferencesService.GetAsync();
            if (prefsResult.IsSuccess)
            {
                var prefs = prefsResult.Value!;
                SelectedFormationType = prefs.DefaultFormation;
                SplitType = prefs.DefaultSplitType;
                GameDurationMinutes = prefs.GameDurationMinutes;
            }

            var dateResult = await PreferencesService.GetNextMatchDateAsync();
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
            var seasonResult = await SeasonService.FindForDateAsync(Date ?? DateTime.Today);
            seasonId = seasonResult.IsSuccess ? seasonResult.Value?.Id ?? 0 : 0;
            SeasonNotCreatedYet = seasonId == 0;
        }

        if (seasonId == 0)
        {
            Squad = SeasonSquad.Empty;
        }
        else
        {
            var squadResult = await SquadService.GetSquadAsync(seasonId);
            Squad = squadResult.IsSuccess ? squadResult.Value! : SeasonSquad.Empty;
        }

        // Drop selections that aren't valid for this season, so switching can't smuggle a stale id
        // through to Submit. (Post-backfill nobody is outside a squad, but a later removal could.)
        UnavailablePlayerIds = [.. UnavailablePlayerIds.Where(Squad.IsFullMember)];
        GuestPlayerIds = [.. GuestPlayerIds.Where(id => Squad.Contains(id) && Squad.IsGuest(id))];

        StateHasChanged();
    }

    protected override void OnParametersSet()
    {
        if (Game is not null)
        {
            Opponent = Game.Opponent;
            Date = Game.Date;
            SelectedFormationType = Game.FormationType;
            SplitType = Game.SplitType;
            Notes = Game.Notes;
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
        game.Date = Date ?? DateTime.Today;
        game.FormationType = SelectedFormationType;
        game.SplitType = SplitType;
        game.Notes = Notes;
        game.GameDurationMinutes = GameDurationMinutes;
        game.IsHomeGame = IsHomeGame;
        game.SeasonId = SelectedSeasonId;
        game.UnavailablePlayerIds = UnavailablePlayerIds.ToList();
        game.GuestPlayerIds = GuestPlayerIds.ToList();

        MudDialog.Close(DialogResult.Ok(game));
    }

    private void Cancel() => MudDialog.Cancel();
}
