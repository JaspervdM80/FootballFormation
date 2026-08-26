namespace FootballFormation.UI.Pages;

/// A player standing on the pitch right now, with the position she is holding.
public record PitchPlayer(Player Player, PlayerPosition Position);

/// <paramref name="IsInjury"/> rides along with a substitution rather than replacing it — a player who is hurt still hands her place to
/// whoever comes on. <paramref name="PlayerId"/> is null only when nobody does.
public record LiveSubChoice(int? PlayerId, bool IsPositionSwap, bool IsInjury);

/// Picking a position swap clears the other two controls and marking her injured clears the swap, so the dialog answers with exactly one
/// change. Like every dialog here it never calls a service; the page persists the choice.
public partial class LiveSubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// The player who was tapped on the pitch.
    [Parameter, EditorRequired]
    public Player Player { get; set; } = null!;

    /// The position she is holding, shown so the swap list reads against something.
    [Parameter]
    public PlayerPosition Position { get; set; }

    /// Everyone available to come on for the period currently being played.
    [Parameter, EditorRequired]
    public List<Player> Bench { get; set; } = [];

    /// The rest of the pitch — who this player can trade positions with.
    [Parameter, EditorRequired]
    public List<PitchPlayer> OnPitch { get; set; } = [];

    private int? _playerOnId;
    private int? _swapWithId;
    private bool _injured;

    /// Nullable so the select opens genuinely empty: an int binds to 0, which is nobody's id but still renders as a chosen value.
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

    /// Why she is going off, not a third thing that can happen to her, so it leaves <see cref="PlayerOnId"/> alone. It does clear the
    /// swap: a player being helped off is not trading positions with anyone.
    private bool Injured
    {
        get => _injured;
        set
        {
            _injured = value;
            if (value) _swapWithId = null;
        }
    }

    /// The one button says what it is about to do, which is whichever control was used.
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
