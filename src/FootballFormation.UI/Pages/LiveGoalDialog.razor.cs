using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>Who scored, who set it up, and whether it went in at the wrong end.</summary>
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
    private int ScorerId { get; set; }
    private int AssisterId { get; set; }
    private bool IsOwnGoal { get; set; }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid || ScorerId == 0) return;

        var assister = AssisterId == 0 || AssisterId == ScorerId ? (int?)null : AssisterId;
        MudDialog.Close(DialogResult.Ok(new LiveGoalChoice(ScorerId, assister, IsOwnGoal)));
    }

    private void Cancel() => MudDialog.Cancel();
}
