namespace FootballFormation.UI.Pages;

/// <summary>What the caller picked: who joins, and whether they all join as guests or injured.</summary>
public record SquadMemberChoice(IReadOnlyList<int> PlayerIds, bool IsGuest, bool IsInjured);

/// <summary>Adds people already on file to a season's squad. Like every dialog here it never
/// calls a service — the page persists the choice.</summary>
public partial class SquadMemberDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    private readonly HashSet<int> _picked = [];
    private bool IsGuest { get; set; }
    private bool IsInjured { get; set; }

    private void Toggle(int playerId)
    {
        if (!_picked.Remove(playerId)) _picked.Add(playerId);
    }

    private void Submit()
    {
        if (_picked.Count == 0) return;

        var ids = Candidates.Where(p => _picked.Contains(p.Id)).Select(p => p.Id).ToList();
        MudDialog.Close(DialogResult.Ok(new SquadMemberChoice(ids, IsGuest, IsInjured)));
    }

    private void Cancel() => MudDialog.Cancel();
}
