using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

/// <summary>How big the chips are drawn. The pitch itself always fills its container.</summary>
public enum PitchSize
{
    /// <summary>52px chips — the overview and live screens, where the pitch is the page.</summary>
    Regular,

    /// <summary>44px chips — the builder, where the pitch shares the screen with the bench.</summary>
    Compact
}

/// <summary>
/// The pitch, in its one implementation. Read-only by default; wire
/// <see cref="OnPlayerClicked"/> to make the chips tappable, or set <see cref="Draggable"/> for the
/// full drag-and-drop builder behaviour.
/// </summary>
public partial class Pitch
{
    [Parameter, EditorRequired]
    public FormationType Formation { get; set; }

    [Parameter, EditorRequired]
    public List<GamePlayerPosition> Positions { get; set; } = [];

    [Parameter]
    public PitchSize Size { get; set; } = PitchSize.Regular;

    /// <summary>Caps the pitch at 65vh so it fits beside the builder's bench without scrolling.</summary>
    [Parameter]
    public bool ConstrainHeight { get; set; }

    /// <summary>Draws every chip in the "preferred" colour, for screens where fit is not the point.</summary>
    [Parameter]
    public bool HidePositionFit { get; set; }

    /// <summary>Enables dragging chips off the pitch, and empty slots as drop targets.</summary>
    [Parameter]
    public bool Draggable { get; set; }

    /// <summary>The player being dragged, if any — empty slots highlight while one is in hand.</summary>
    [Parameter]
    public int? DraggedPlayerId { get; set; }

    /// <summary>Raised with the slot index a drag was released over.</summary>
    [Parameter]
    public EventCallback<int> OnPlayerDropped { get; set; }

    /// <summary>Raised with the slot index when an occupied slot is tapped on a draggable pitch.</summary>
    [Parameter]
    public EventCallback<int> OnPlayerRemoved { get; set; }

    /// <summary>Raised with the slot index a drag started from.</summary>
    [Parameter]
    public EventCallback<int> OnPlayerDragStart { get; set; }

    /// <summary>
    /// Raised with the player id when an occupied slot is tapped. Left unset the pitch stays inert,
    /// which is what the read-only overview wants; the live match screen uses it to open the
    /// substitution sheet.
    /// </summary>
    [Parameter]
    public EventCallback<int> OnPlayerClicked { get; set; }

    private PlayerPosition[] Slots => FormationSlots.For(Formation);

    private bool IsDropTarget => Draggable && DraggedPlayerId is not null;

    private string SizeClass => Size == PitchSize.Compact ? "pitch-compact" : "pitch-regular";

    /// <summary>A tap means "take this player off" on the builder and "select" everywhere else.</summary>
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

    /// <summary>The one place a <see cref="PositionFit"/> becomes a colour.</summary>
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

        // Invariant: the app runs in Dutch, where a fractional coordinate would be written with a
        // comma and silently break the inline style.
        return FormattableString.Invariant($"left: {left}%; top: {top}%;");
    }
}
