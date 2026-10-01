namespace FootballFormation.UI.Pages;

/// A player standing on the pitch right now, with the position she is holding.
public record PitchPlayer(Player Player, PlayerPosition Position);

/// <paramref name="IsInjury"/> rides along with a substitution rather than replacing it — a player who is hurt still hands her place to
/// whoever comes on. <paramref name="PlayerId"/> is null only when nobody does.
public record LiveSubChoice(int? PlayerId, bool IsPositionSwap, bool IsInjury);

/// Tapping who comes on closes the dialog with that change. The footer button is for the two changes nobody comes on in: a position swap,
/// and an injury with nobody to replace her. Like every dialog here it never calls a service; the page persists the choice.
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

    /// Off at the break: the next half is being set up, not played, so there is no clock for an injury to stop and a position swap is just
    /// line-up editing — only who comes on for whom applies.
    [Parameter] public bool AllowSwapAndInjury { get; set; } = true;

    private int? _swapWithId;
    private bool _injured;

    /// Nullable so the select opens genuinely empty: an int binds to 0, which is nobody's id but still renders as a chosen value.
    private int? SwapWithId
    {
        get => _swapWithId;
        set
        {
            _swapWithId = value;
            if (value is not null) _injured = false;
        }
    }

    /// A player being helped off is not trading positions with anyone, so it clears the swap.
    private bool Injured
    {
        get => _injured;
        set
        {
            _injured = value;
            if (value) _swapWithId = null;
        }
    }

    private bool IsPositionSwap => _swapWithId is not null;

    private void BringOn(Player playerOn) =>
        MudDialog.Close(DialogResult.Ok(new LiveSubChoice(playerOn.Id, IsPositionSwap: false, IsInjury: _injured)));

    private void Submit()
    {
        if (_swapWithId is { } swapWith)
            MudDialog.Close(DialogResult.Ok(new LiveSubChoice(swapWith, IsPositionSwap: true, IsInjury: false)));
        else if (_injured)
            MudDialog.Close(DialogResult.Ok(new LiveSubChoice(null, IsPositionSwap: false, IsInjury: true)));
    }

    private void Cancel() => MudDialog.Cancel();
}
