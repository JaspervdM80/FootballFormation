using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class PlayerDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Player? Player { get; set; }

    private MudForm Form { get; set; } = null!;
    private string FirstName { get; set; } = string.Empty;
    private string? Surname { get; set; }
    private int? ShirtNumber { get; set; }
    private PlayerPosition PreferredPosition { get; set; } = PlayerPosition.CM;
    private IReadOnlyCollection<PlayerPosition> AlternativePositions { get; set; } = Array.Empty<PlayerPosition>();

    protected override void OnParametersSet()
    {
        if (Player is not null)
        {
            FirstName = Player.FirstName;
            Surname = Player.Surname;
            ShirtNumber = Player.ShirtNumber;
            PreferredPosition = Player.PreferredPosition;
            AlternativePositions = Player.AlternativePositions;
        }
    }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        var player = Player ?? new Player { FirstName = FirstName };
        player.FirstName = FirstName;
        player.Surname = string.IsNullOrWhiteSpace(Surname) ? null : Surname.Trim();
        player.ShirtNumber = ShirtNumber;
        player.PreferredPosition = PreferredPosition;
        player.AlternativePositions = AlternativePositions.ToList();

        MudDialog.Close(DialogResult.Ok(player));
    }

    private void Cancel() => MudDialog.Cancel();
}
