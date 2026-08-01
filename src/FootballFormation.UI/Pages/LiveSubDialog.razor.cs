using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>
/// Picks the replacement for a player already chosen on the pitch. Like every dialog here it
/// never calls a service — the page persists the choice.
/// </summary>
public partial class LiveSubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public Player PlayerOff { get; set; } = null!;

    /// <summary>Everyone available to come on for the period currently being played.</summary>
    [Parameter, EditorRequired]
    public List<Player> Bench { get; set; } = [];

    private MudForm Form { get; set; } = null!;
    private int PlayerOnId { get; set; }

    protected override void OnParametersSet() =>
        PlayerOnId = PlayerOnId == 0 ? Bench.FirstOrDefault()?.Id ?? 0 : PlayerOnId;

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid || PlayerOnId == 0) return;

        MudDialog.Close(DialogResult.Ok(PlayerOnId));
    }

    private void Cancel() => MudDialog.Cancel();
}
