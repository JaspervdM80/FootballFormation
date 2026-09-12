namespace FootballFormation.UI.Pages;

/// The corrected goal. Which side it counts for is not in here — that is a different goal, removed and logged again. Like every dialog it
/// never calls a service.
public record EditGoalChoice(int? ScorerId, int? AssisterId, bool IsOwnGoal, int Minute);

public partial class EditGoalDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Everyone who could have scored it, the current scorer among them so it can stay unchanged. Empty for an opponent goal.
    [Parameter]
    public List<Player> Candidates { get; set; } = [];

    [Parameter]
    public int? ScorerId { get; set; }

    [Parameter]
    public int? AssisterId { get; set; }

    [Parameter]
    public bool IsOwnGoal { get; set; }

    /// Minute only, and named so the coach can see whose goal is being corrected.
    [Parameter]
    public bool IsOpponentGoal { get; set; }

    [Parameter]
    public string Opponent { get; set; } = string.Empty;

    [Parameter]
    public int Minute { get; set; }

    [Parameter]
    public int MaxMinute { get; set; } = 90;

    /// Bound through properties, not the parameters directly — see EditSubDialog.
    private int? SelectedScorerId { get; set; }
    private int? SelectedAssisterId { get; set; }
    private bool SelectedIsOwnGoal { get; set; }
    private int SelectedMinute { get; set; }

    protected override void OnInitialized()
    {
        SelectedScorerId = ScorerId;
        SelectedAssisterId = AssisterId;
        SelectedIsOwnGoal = IsOwnGoal;
        SelectedMinute = Minute;
    }

    private bool HasChoice => SelectedMinute >= 1 && (IsOpponentGoal || SelectedScorerId is not null);

    /// Math.Clamp throws when its bounds cross, and a game with no duration on file leaves MaxMinute at zero.
    private int Ceiling => Math.Max(1, MaxMinute);

    private void Submit() =>
        MudDialog.Close(DialogResult.Ok(new EditGoalChoice(
            IsOpponentGoal ? null : SelectedScorerId,
            IsOpponentGoal ? null : SelectedAssisterId,
            !IsOpponentGoal && SelectedIsOwnGoal,
            Math.Clamp(SelectedMinute, 1, Ceiling))));

    private void Cancel() => MudDialog.Cancel();
}
