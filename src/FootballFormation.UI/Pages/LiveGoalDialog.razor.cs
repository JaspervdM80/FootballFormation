namespace FootballFormation.UI.Pages;

public record LiveGoalChoice(int ScorerId, int? AssisterId, bool IsOwnGoal);

/// Like every dialog here it never calls a service — the page persists the choice.
public partial class LiveGoalDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Players who can be credited, on-pitch first — see LiveMatch.GoalCandidates.
    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    /// Nullable so the select opens genuinely empty: an int binds to 0, which is nobody's id but still renders as a chosen value.
    private int? AssisterId { get; set; }

    private bool IsOwnGoal { get; set; }

    private void Score(Player scorer)
    {
        var assister = AssisterId == scorer.Id ? null : AssisterId;
        MudDialog.Close(DialogResult.Ok(new LiveGoalChoice(scorer.Id, assister, IsOwnGoal)));
    }

    private void Cancel() => MudDialog.Cancel();
}
