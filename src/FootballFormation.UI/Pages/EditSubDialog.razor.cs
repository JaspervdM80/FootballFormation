namespace FootballFormation.UI.Pages;

/// The corrected substitution: who should have come on, and the minute it should read. The player coming off is fixed here — a wrong one is
/// undone and made again — so the dialog only ever answers with a replacement and a time. Like every dialog it never calls a service.
public record EditSubChoice(int PlayerOnId, int Minute);

public partial class EditSubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// The player who came off, shown so the change reads against someone.
    [Parameter, EditorRequired]
    public Player PlayerOff { get; set; } = null!;

    /// Everyone who could be the one coming on, the current choice among them so it can stay unchanged.
    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    [Parameter]
    public int PlayerOnId { get; set; }

    [Parameter]
    public int Minute { get; set; }

    [Parameter]
    public int MaxMinute { get; set; } = 90;

    /// Bound through properties, not the fields directly: MudBlazor's two-way binding does not write back to a raw field here.
    private int? SelectedOnId { get; set; }
    private int SelectedMinute { get; set; }

    protected override void OnInitialized()
    {
        SelectedOnId = PlayerOnId;
        SelectedMinute = Minute;
    }

    private bool HasChoice => SelectedOnId is not null && SelectedMinute >= 1;

    private void Submit()
    {
        if (SelectedOnId is { } onId)
            MudDialog.Close(DialogResult.Ok(new EditSubChoice(onId, Math.Clamp(SelectedMinute, 1, MaxMinute))));
    }

    private void Cancel() => MudDialog.Cancel();
}
