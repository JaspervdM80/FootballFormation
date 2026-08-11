using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>
/// Shows what rolling on to the next line-up will actually do, and asks first. The button used to
/// carry it out on the tap, which meant reading the card further down the screen to find out what
/// was about to change — and there is no undo for a period that has been advanced.
/// </summary>
public partial class LiveNextLineupDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The swaps and moves the next period's line-up implies, measured against the pitch.</summary>
    [Parameter, EditorRequired]
    public PlannedChanges Changes { get; set; } = PlannedChanges.None;

    private void Accept() => MudDialog.Close(DialogResult.Ok(true));

    private void Cancel() => MudDialog.Cancel();
}
