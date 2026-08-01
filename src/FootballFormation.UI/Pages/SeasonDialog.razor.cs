using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class SeasonDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Season? Season { get; set; }

    private MudForm Form { get; set; } = null!;
    private string Name { get; set; } = string.Empty;
    private DateTime? StartDate { get; set; }
    private DateTime? EndDate { get; set; }

    protected override void OnInitialized()
    {
        if (Season is not null) return;

        // Create mode: pre-fill the season covering today, so the common case is one click.
        // Qualified because the Season parameter shadows the type name here.
        var suggested = Core.Models.Season.CreateFor(DateTime.Today);
        Name = suggested.Name;
        StartDate = suggested.StartDate;
        EndDate = suggested.EndDate;
    }

    protected override void OnParametersSet()
    {
        if (Season is null) return;

        Name = Season.Name;
        StartDate = Season.StartDate;
        EndDate = Season.EndDate;
    }

    /// <summary>Date range and overlap rules live in <c>SeasonService</c>, so they apply to every
    /// caller; the dialog only checks what the form itself can.</summary>
    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        var season = Season ?? new Season { Name = Name };
        season.Name = Name;
        season.StartDate = StartDate ?? DateTime.Today;
        season.EndDate = EndDate ?? DateTime.Today.AddYears(1).AddDays(-1);

        MudDialog.Close(DialogResult.Ok(season));
    }

    private void Cancel() => MudDialog.Cancel();
}
