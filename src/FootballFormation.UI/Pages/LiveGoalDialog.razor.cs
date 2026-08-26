namespace FootballFormation.UI.Pages;

public record LiveGoalChoice(int ScorerId, int? AssisterId, bool IsOwnGoal);

/// <summary>
/// Logs one of our goals during a live match. Like every dialog here it never calls a service —
/// the page persists the choice.
/// </summary>
public partial class LiveGoalDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>Players who can be credited, on-pitch first — see LiveMatch.GoalCandidates.</summary>
    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    private MudForm Form { get; set; } = null!;

    /// <summary>
    /// Nullable so the select opens genuinely empty. An int would bind to 0, which is nobody's id
    /// but still renders as a chosen value and reads like the field is already filled in.
    /// </summary>
    private int? ScorerId { get; set; }

    private int? AssisterId { get; set; }
    private bool IsOwnGoal { get; set; }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid || ScorerId is not { } scorerId) return;

        var assister = AssisterId == scorerId ? null : AssisterId;
        MudDialog.Close(DialogResult.Ok(new LiveGoalChoice(scorerId, assister, IsOwnGoal)));
    }

    private void Cancel() => MudDialog.Cancel();
}
