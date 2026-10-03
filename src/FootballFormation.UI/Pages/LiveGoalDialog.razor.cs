namespace FootballFormation.UI.Pages;

public record LiveGoalChoice(int ScorerId, int? AssisterId, bool IsOwnGoal);

/// Like every dialog here it never calls a service — the page persists the choice.
public partial class LiveGoalDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Players who can be credited, in the order LiveCandidateOrder.Scorers gives them.
    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    private bool IsOwnGoal { get; set; }

    private Player? Scorer { get; set; }

    private List<Player> Assisters => Candidates.Where(p => p.Id != Scorer?.Id).ToList();

    // Nobody assists an own goal, so it is saved on the first tap.
    private void PickScorer(Player scorer)
    {
        if (IsOwnGoal)
            Close(scorer, assister: null);
        else
            Scorer = scorer;
    }

    private void PickAssister(Player assister) => Close(Scorer!, assister.Id);

    private void NoAssist() => Close(Scorer!, assister: null);

    private void Back() => Scorer = null;

    private void Close(Player scorer, int? assister) =>
        MudDialog.Close(DialogResult.Ok(new LiveGoalChoice(scorer.Id, assister, IsOwnGoal)));

    private void Cancel() => MudDialog.Cancel();
}
