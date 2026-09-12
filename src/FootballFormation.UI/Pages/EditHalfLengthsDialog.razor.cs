namespace FootballFormation.UI.Pages;

/// The corrected lengths, in minutes. A half that was never played stays null, so the caller can tell "unchanged" from "not there".
public record HalfLengthsChoice(int? FirstHalfMinutes, int? SecondHalfMinutes);

public partial class EditHalfLengthsDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Null for a half that was never kicked off — it gets no field and comes back null.
    [Parameter]
    public int? FirstHalfMinutes { get; set; }

    [Parameter]
    public int? SecondHalfMinutes { get; set; }

    [Parameter]
    public int MaxMinute { get; set; } = 90;

    /// Bound through properties, not the parameters directly — see EditSubDialog.
    private int FirstLength { get; set; }
    private int SecondLength { get; set; }

    protected override void OnInitialized()
    {
        FirstLength = FirstHalfMinutes ?? 0;
        SecondLength = SecondHalfMinutes ?? 0;
    }

    private bool HasChoice =>
        (FirstHalfMinutes is null || FirstLength >= 1) && (SecondHalfMinutes is null || SecondLength >= 1);

    /// A half is never longer than the whole match, and a game with no duration on file leaves MaxMinute at zero.
    private int Ceiling => Math.Max(1, MaxMinute);

    private void Submit() =>
        MudDialog.Close(DialogResult.Ok(new HalfLengthsChoice(
            FirstHalfMinutes is null ? null : Math.Clamp(FirstLength, 1, Ceiling),
            SecondHalfMinutes is null ? null : Math.Clamp(SecondLength, 1, Ceiling))));

    private void Cancel() => MudDialog.Cancel();
}
