using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>What the dialog edited: the person, and whether they are a guest in the season being
/// edited. Two things, because guest status belongs to a season's squad and not to the person —
/// the page makes them two writes.</summary>
public record PlayerEdit(Player Player, bool IsGuest);

public partial class PlayerDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public Player? Player { get; set; }

    /// <summary>The member's guest flag in the season being edited, seeded from the squad row.</summary>
    [Parameter]
    public bool IsGuest { get; set; }

    /// <summary>Named in the caption under the switch, so it is clear the flag is per season.</summary>
    [Parameter]
    public string? SeasonName { get; set; }

    private MudForm Form { get; set; } = null!;
    private string FirstName { get; set; } = string.Empty;
    private string? Surname { get; set; }
    private int? ShirtNumber { get; set; }
    private PlayerPosition PreferredPosition { get; set; } = PlayerPosition.CM;
    private IReadOnlyCollection<PlayerPosition> AlternativePositions { get; set; } = Array.Empty<PlayerPosition>();

    /// <summary>What the switch edits. Seeded from <see cref="IsGuest"/> like every other field is
    /// seeded from <see cref="Player"/>, rather than binding the parameter itself.</summary>
    private bool Guest { get; set; }

    protected override void OnParametersSet()
    {
        Guest = IsGuest;

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

        MudDialog.Close(DialogResult.Ok(new PlayerEdit(player, Guest)));
    }

    private void Cancel() => MudDialog.Cancel();
}
