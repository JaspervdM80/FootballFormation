using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>What the caller picked: which person, and whether they join as a guest.</summary>
public record SquadMemberChoice(int PlayerId, bool IsGuest);

/// <summary>Adds someone already on file to a season's squad. Like every dialog here it never
/// calls a service — the page persists the choice.</summary>
public partial class SquadMemberDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public List<Player> Candidates { get; set; } = [];

    private MudForm Form { get; set; } = null!;
    private int PlayerId { get; set; }
    private bool IsGuest { get; set; }

    protected override void OnParametersSet() =>
        PlayerId = PlayerId == 0 ? Candidates.FirstOrDefault()?.Id ?? 0 : PlayerId;

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid || PlayerId == 0) return;

        MudDialog.Close(DialogResult.Ok(new SquadMemberChoice(PlayerId, IsGuest)));
    }

    private void Cancel() => MudDialog.Cancel();
}
