namespace FootballFormation.UI.Pages;

public record SwapPositionsEntry(Player Player, PlayerPosition Position);

/// Who stood where when the half was whistled off — the line-up the service swaps in.
public record SwapPositionsHalf(PeriodType Half, List<SwapPositionsEntry> OnPitch);

public record SwapPositionsChoice(PeriodType Half, int PlayerAId, int PlayerBId);

public partial class SwapPositionsDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public List<SwapPositionsHalf> Halves { get; set; } = [];

    /// Bound through properties — see EditSubDialog.
    private PeriodType SelectedHalf { get; set; }
    private int? SelectedAId { get; set; }
    private int? SelectedBId { get; set; }

    private SwapPositionsHalf Current => Halves.FirstOrDefault(h => h.Half == SelectedHalf) ?? Halves[0];

    protected override void OnInitialized() => ChooseHalf(Halves[0].Half);

    private void ChooseHalf(PeriodType half)
    {
        SelectedHalf = half;
        SelectedAId = null;
        SelectedBId = null;
    }

    private void ChooseA(int? playerId)
    {
        SelectedAId = playerId;
        if (SelectedBId == playerId) SelectedBId = null;
    }

    private bool HasChoice => SelectedAId is not null && SelectedBId is not null && SelectedAId != SelectedBId;

    private void Submit()
    {
        if (SelectedAId is not { } aId || SelectedBId is not { } bId || aId == bId) return;

        MudDialog.Close(DialogResult.Ok(new SwapPositionsChoice(SelectedHalf, aId, bId)));
    }

    private void Cancel() => MudDialog.Cancel();
}
