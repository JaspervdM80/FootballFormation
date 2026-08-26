using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Components;

/// How big the chips are drawn. The pitch itself always fills its container.
public enum PitchSize
{
    /// 52px chips — the overview and live screens, where the pitch is the page.
    Regular,

    /// 44px chips — the builder, where the pitch shares the screen with the bench.
    Compact
}

/// Read-only by default: wire <see cref="OnPlayerClicked"/> to make the chips tappable, or set <see cref="Draggable"/> for the builder.
public partial class Pitch
{
    [Parameter, EditorRequired]
    public FormationType Formation { get; set; }

    [Parameter, EditorRequired]
    public List<GamePlayerPosition> Positions { get; set; } = [];

    [Parameter]
    public PitchSize Size { get; set; } = PitchSize.Regular;

    /// Caps the pitch at 65vh so it fits beside the builder's bench without scrolling.
    [Parameter]
    public bool ConstrainHeight { get; set; }

    /// Draws every chip in the "preferred" colour, for screens where fit is not the point.
    [Parameter]
    public bool HidePositionFit { get; set; }

    /// Enables dragging chips off the pitch, and empty slots as drop targets.
    [Parameter]
    public bool Draggable { get; set; }

    /// Empty slots highlight while one is in hand.
    [Parameter]
    public int? DraggedPlayerId { get; set; }

    /// Raised with the slot index a drag was released over.
    [Parameter]
    public EventCallback<int> OnPlayerDropped { get; set; }

    /// Raised with the slot index when an occupied slot is tapped on a draggable pitch.
    [Parameter]
    public EventCallback<int> OnPlayerRemoved { get; set; }

    /// Raised with the slot index a drag started from.
    [Parameter]
    public EventCallback<int> OnPlayerDragStart { get; set; }

    /// Left unset the pitch stays inert, which is what the read-only overview wants.
    [Parameter]
    public EventCallback<int> OnPlayerClicked { get; set; }

    private PlayerPosition[] Slots => FormationSlots.For(Formation);

    private bool IsDropTarget => Draggable && DraggedPlayerId is not null;

    private string SizeClass => Size == PitchSize.Compact ? "pitch-compact" : "pitch-regular";

    /// A tap means "take this player off" on the builder and "select" everywhere else.
    private Task OnChipClicked(int slotIndex, int playerId) =>
        Draggable
            ? OnPlayerRemoved.InvokeAsync(slotIndex)
            : OnPlayerClicked.InvokeAsync(playerId);

    private string ChipCssClass(PlayerPosition position, Player player)
    {
        var fit = HidePositionFit ? PositionFit.Preferred : PositionFitHelper.GetFit(player, position);

        return $"pitch-player {FitCssClass(fit)} {(IsChipInteractive ? "pitch-clickable" : "")}";
    }

    private bool IsChipInteractive => Draggable || OnPlayerClicked.HasDelegate;

    /// The one place a <see cref="PositionFit"/> becomes a colour.
    public static string FitCssClass(PositionFit fit) => fit switch
    {
        PositionFit.Preferred => "fit-preferred",
        PositionFit.NaturalFit => "fit-natural",
        PositionFit.Alternative => "fit-alternative",
        PositionFit.Compatible => "fit-compatible",
        _ => "fit-out-of-position"
    };

    private string SlotStyle(int slotIndex)
    {
        var slots = Slots;
        var (index, count) = FormationSlots.OrdinalOf(slots, slotIndex);
        var (left, top) = PitchPositionHelper.GetCoordinates(slots[slotIndex], index, count);

        // The app runs in Dutch, where a fractional coordinate would be written with a comma and silently break the inline style.
        return FormattableString.Invariant($"left: {left}%; top: {top}%;");
    }
}
