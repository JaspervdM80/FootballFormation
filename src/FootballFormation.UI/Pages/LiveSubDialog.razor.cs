using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>A player standing on the pitch right now, with the position they are holding.</summary>
public record PitchPlayer(Player Player, PlayerPosition Position);

/// <summary>
/// What the touchline decided about the player who was tapped: either someone comes on for them,
/// or they trade positions with a team-mate who stays on.
/// </summary>
public record LiveSubChoice(int PlayerId, bool IsPositionSwap);

/// <summary>
/// The two things that can happen to a player already on the pitch. Both are one dropdown and one
/// button, and choosing in either clears the other — the dialog answers with exactly one change.
/// Like every dialog here it never calls a service; the page persists the choice.
/// </summary>
public partial class LiveSubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The player who was tapped on the pitch.</summary>
    [Parameter, EditorRequired]
    public Player Player { get; set; } = null!;

    /// <summary>The position they are holding, shown so the swap list reads against something.</summary>
    [Parameter]
    public PlayerPosition Position { get; set; }

    /// <summary>Everyone available to come on for the period currently being played.</summary>
    [Parameter, EditorRequired]
    public List<Player> Bench { get; set; } = [];

    /// <summary>The rest of the pitch — who this player can trade positions with.</summary>
    [Parameter, EditorRequired]
    public List<PitchPlayer> OnPitch { get; set; } = [];

    private int? _playerOnId;
    private int? _swapWithId;

    /// <summary>
    /// Nullable so the select opens genuinely empty: an int binds to 0, which is nobody's id but
    /// still renders as a chosen value — the same trap LiveGoalDialog documents.
    /// </summary>
    private int? PlayerOnId
    {
        get => _playerOnId;
        set
        {
            _playerOnId = value;
            if (value is not null) _swapWithId = null;
        }
    }

    private int? SwapWithId
    {
        get => _swapWithId;
        set
        {
            _swapWithId = value;
            if (value is not null) _playerOnId = null;
        }
    }

    /// <summary>The one button says what it is about to do, which is whichever list was used.</summary>
    private bool IsPositionSwap => _swapWithId is not null;

    private bool HasChoice => _playerOnId is not null || _swapWithId is not null;

    private void Submit()
    {
        if (_swapWithId is { } swapWith)
        {
            MudDialog.Close(DialogResult.Ok(new LiveSubChoice(swapWith, IsPositionSwap: true)));
            return;
        }

        if (_playerOnId is { } playerOn)
            MudDialog.Close(DialogResult.Ok(new LiveSubChoice(playerOn, IsPositionSwap: false)));
    }

    private void Cancel() => MudDialog.Cancel();
}
