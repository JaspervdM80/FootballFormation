namespace FootballFormation.UI.Pages;

/// Three things, because guest and injury status belong to a season's squad rather than to the person — the page writes them separately.
public record PlayerEdit(Player Player, bool IsGuest, bool IsInjured);

public partial class PlayerDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Player? Player { get; set; }

    /// The member's guest flag in the season being edited, seeded from the squad row.
    [Parameter]
    public bool IsGuest { get; set; }

    /// The member's injury flag in the season being edited, seeded from the squad row.
    [Parameter]
    public bool IsInjured { get; set; }

    private MudForm Form { get; set; } = null!;
    private string FirstName { get; set; } = string.Empty;
    private string? Surname { get; set; }
    private int? ShirtNumber { get; set; }
    private PlayerPosition PreferredPosition { get; set; } = PlayerPosition.CM;
    private IReadOnlyCollection<PlayerPosition> AlternativePositions { get; set; } = Array.Empty<PlayerPosition>();

    /// Seeded from <see cref="IsGuest"/> rather than binding the parameter itself, like every other field is seeded from Player.
    private bool Guest { get; set; }

    /// Same reasoning as <see cref="Guest"/>, seeded from <see cref="IsInjured"/>.
    private bool Injured { get; set; }

    protected override void OnParametersSet()
    {
        Guest = IsGuest;
        Injured = IsInjured;

        if (Player is not null)
        {
            FirstName = Player.FirstName;
            Surname = Player.Surname;
            ShirtNumber = Player.ShirtNumber;
            PreferredPosition = Player.PreferredPosition;
            AlternativePositions = Player.AlternativePositions;
        }
    }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        var player = Player ?? new Player { FirstName = FirstName };
        player.FirstName = FirstName;
        player.Surname = string.IsNullOrWhiteSpace(Surname) ? null : Surname.Trim();
        player.ShirtNumber = ShirtNumber;
        player.PreferredPosition = PreferredPosition;
        player.AlternativePositions = AlternativePositions.ToList();

        MudDialog.Close(DialogResult.Ok(new PlayerEdit(player, Guest, Injured)));
    }

    private void Cancel() => MudDialog.Cancel();
}
