namespace FootballFormation.UI.Pages;

/// Who could go off and come on in one played half, judged by the line-up it finished with — the one the service lays the change over.
public record AddSubHalf(PeriodType Half, int FirstMinute, int LastMinute, List<Player> OnPitch, List<Player> Bench);

public record AddSubChoice(PeriodType Half, int PlayerOffId, int PlayerOnId, int Minute, bool Injured);

public partial class AddSubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public List<AddSubHalf> Halves { get; set; } = [];

    /// Bound through properties — see EditSubDialog.
    private PeriodType SelectedHalf { get; set; }
    private int? SelectedOffId { get; set; }
    private int? SelectedOnId { get; set; }
    private int SelectedMinute { get; set; }
    private bool SelectedInjured { get; set; }

    private AddSubHalf Current => Halves.FirstOrDefault(h => h.Half == SelectedHalf) ?? Halves[0];

    protected override void OnInitialized() => ChooseHalf(Halves[0].Half);

    private void ChooseHalf(PeriodType half)
    {
        SelectedHalf = half;
        SelectedOffId = null;
        SelectedOnId = null;
        SelectedMinute = Current.FirstMinute;
    }

    private bool HasChoice => SelectedOffId is not null && SelectedOnId is not null && SelectedOffId != SelectedOnId;

    private void Submit()
    {
        if (SelectedOffId is not { } offId || SelectedOnId is not { } onId) return;

        var minute = Math.Clamp(SelectedMinute, Current.FirstMinute, Math.Max(Current.FirstMinute, Current.LastMinute));
        MudDialog.Close(DialogResult.Ok(new AddSubChoice(SelectedHalf, offId, onId, minute, SelectedInjured)));
    }

    private void Cancel() => MudDialog.Cancel();
}
