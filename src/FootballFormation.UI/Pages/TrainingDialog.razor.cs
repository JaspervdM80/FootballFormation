using FootballFormation.UI.State;

namespace FootballFormation.UI.Pages;

public partial class TrainingDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;

    [Parameter]
    public Training? Training { get; set; }

    private MudForm Form { get; set; } = null!;
    private DateTime? Date { get; set; } = DateTime.Today;

    /// Kept apart from <see cref="Date"/> so a blank field round-trips as no time at all rather than midnight — see
    /// Training.HasStartTime.
    private string? StartTimeText { get; set; }
    private string? Notes { get; set; }
    private bool DidNotTakePlace { get; set; }
    private IReadOnlyCollection<int> UnavailablePlayerIds { get; set; } = [];

    /// 0 is "resolve from the date", which TrainingService.CreateAsync does on save. An existing session keeps its own season, so
    /// retyping a date never silently moves it.
    private int SeasonId { get; set; }

    /// Reloaded whenever the date changes, so the picker always offers that season's people.
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;

    /// True once a date resolves to no season at all — a session opening a new season.
    private bool SeasonNotCreatedYet { get; set; }

    /// Injured players are left out — already out of the roster for every game, so offering them here says the same thing twice.
    private List<Player> SquadPlayers => [.. Squad.FullMembers.Where(p => !Squad.IsInjured(p.Id))];

    protected override async Task OnInitializedAsync()
    {
        await SeasonState.EnsureLoadedAsync();

        if (Training is null)
        {
            // The training days are per season, so a season has to be picked before the date exists. The date that follows lands inside
            // it, so resolving the season from that date still finds the same one.
            var seasonId = SeasonState.SelectedSeasonId ?? 0;

            var dateResult = await PreferencesService.GetNextTrainingDateAsync(seasonId, Cancellation);
            if (dateResult.IsSuccess)
            {
                Date = dateResult.Value;
            }
        }

        // Runs after OnParametersSet has copied an existing session's season and date in.
        await ReloadSquadAsync();
    }

    private Task OnDateChanged(DateTime? date)
    {
        Date = date;

        // Only matters while the season is still being resolved from the date.
        return SeasonId == 0 ? ReloadSquadAsync() : Task.CompletedTask;
    }

    private async Task ReloadSquadAsync()
    {
        var seasonId = SeasonId;
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

        // Drops ids not valid for this season, so moving the date cannot smuggle a stale one through to Submit. Deliberately not
        // filtered on injury: a player marked injured later must not erase that this session already recorded her as absent.
        UnavailablePlayerIds = [.. UnavailablePlayerIds.Where(Squad.IsFullMember)];

        StateHasChanged();
    }

    protected override void OnParametersSet()
    {
        if (Training is not null)
        {
            Date = Training.Date;
            StartTimeText = Training.HasStartTime ? Training.Date.ToString("HH:mm") : null;
            Notes = Training.Notes;
            DidNotTakePlace = Training.DidNotTakePlace;
            SeasonId = Training.SeasonId;
            UnavailablePlayerIds = Training.UnavailablePlayerIds.ToList();
        }
    }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        var training = Training ?? new Training();
        var startTime = TimeSpan.TryParse(StartTimeText, out var parsed) ? parsed : TimeSpan.Zero;
        training.Date = (Date ?? DateTime.Today).Date + startTime;
        training.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        training.DidNotTakePlace = DidNotTakePlace;
        training.SeasonId = SeasonId;
        training.UnavailablePlayerIds = DidNotTakePlace ? [] : UnavailablePlayerIds.ToList();

        MudDialog.Close(DialogResult.Ok(training));
    }

    private void Cancel() => MudDialog.Cancel();
}
