using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>A player standing on the pitch right now, with the position they are holding.</summary>
public record PitchPlayer(Player Player, PlayerPosition Position);

/// <summary>
/// What the touchline decided about the player who was tapped: someone comes on for them, they
/// trade positions with a team-mate who stays on, or they go off hurt.
/// <para>
/// <paramref name="IsInjury"/> rides along with the first rather than replacing it — a player who
/// is hurt still hands her place to whoever comes on. <paramref name="PlayerId"/> is null only when
/// nobody does: she is off injured and the bench had nobody left.
/// </para>
/// </summary>
public record LiveSubChoice(int? PlayerId, bool IsPositionSwap, bool IsInjury);

/// <summary>
/// The things that can happen to a player already on the pitch: two dropdowns and a switch, and
/// one button that says which of them it is about to do. Picking a position swap clears the other
/// two, and marking her injured clears the swap — the dialog answers with exactly one change.
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
    private bool _injured;

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
            if (value is null) return;

            _playerOnId = null;
            _injured = false;
        }
    }

    /// <summary>
    /// Why she is going off, not a third thing that can happen to her, so it leaves
    /// <see cref="PlayerOnId"/> alone — that is still where her replacement is named. It does clear
    /// the swap: a player being helped off is not trading positions with anyone.
    /// </summary>
    private bool Injured
    {
        get => _injured;
        set
        {
            _injured = value;
            if (value) _swapWithId = null;
        }
    }

    /// <summary>The one button says what it is about to do, which is whichever control was used.</summary>
    private bool IsPositionSwap => _swapWithId is not null;

    private bool HasChoice => _playerOnId is not null || _swapWithId is not null || _injured;

    private void Submit()
    {
        if (_swapWithId is { } swapWith)
        {
            MudDialog.Close(DialogResult.Ok(
                new LiveSubChoice(swapWith, IsPositionSwap: true, IsInjury: false)));
            return;
        }

        if (_injured || _playerOnId is not null)
            MudDialog.Close(DialogResult.Ok(
                new LiveSubChoice(_playerOnId, IsPositionSwap: false, IsInjury: _injured)));
    }

    private void Cancel() => MudDialog.Cancel();
}
