namespace FootballFormation.UI.Pages;

public partial class TeamDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Null when adding a team rather than editing one.
    [Parameter] public Team? Team { get; set; }

    [Parameter] public IReadOnlyList<Club> Clubs { get; set; } = [];

    private MudForm Form { get; set; } = null!;

    private string Name { get; set; } = string.Empty;
    private int ClubId { get; set; }

    protected override void OnParametersSet()
    {
        if (Team is not null)
        {
            Name = Team.Name;
            ClubId = Team.ClubId;
            return;
        }

        if (ClubId == 0 && Clubs.Count > 0) ClubId = Clubs[0].Id;
    }

    /// Duplicate names and the unknown-club case live in TeamService so they apply to every caller; this checks only what the form can.
    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        MudDialog.Close(DialogResult.Ok(new Model { Name = Name, ClubId = ClubId }));
    }

    private void Cancel() => MudDialog.Cancel();

    public sealed class Model
    {
        public string Name { get; init; } = string.Empty;
        public int ClubId { get; init; }
    }
}
