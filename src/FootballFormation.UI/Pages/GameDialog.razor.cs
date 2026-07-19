using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class GameDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject] private PlayerService PlayerService { get; set; } = null!;
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
    private List<Player> AllPlayers { get; set; } = [];
    private IReadOnlyCollection<int> UnavailablePlayerIds { get; set; } = [];
    private IReadOnlyCollection<int> GuestPlayerIds { get; set; } = [];

    private List<Player> SquadPlayers => AllPlayers.Where(p => !p.IsGuest).ToList();
    private List<Player> GuestPlayers => AllPlayers.Where(p => p.IsGuest).ToList();

    protected override async Task OnInitializedAsync()
    {
        var playersResult = await PlayerService.GetAllAsync();
        if (playersResult.IsSuccess)
        {
            AllPlayers = playersResult.Value!;
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
        game.UnavailablePlayerIds = UnavailablePlayerIds.ToList();
        game.GuestPlayerIds = GuestPlayerIds.ToList();

        MudDialog.Close(DialogResult.Ok(game));
    }

    private void Cancel() => MudDialog.Cancel();
}
