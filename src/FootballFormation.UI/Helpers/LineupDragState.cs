namespace FootballFormation.UI.Helpers;

/// Keeping the fields together stops the page from having to reset them by hand after every drop.
public class LineupDragState
{
    public int? PlayerId { get; private set; }

    /// Set only when the drag started from a pitch slot, which makes the drop a swap.
    public int? FromSlotIndex { get; private set; }

    /// Set when the drag started from the substitute bench.
    public bool FromSub { get; private set; }

    /// Picked up by a tap rather than a drag, so it stays in hand until a second tap places it.
    public bool IsTapSelection { get; private set; }

    public int? SelectedPlayerId => IsTapSelection ? PlayerId : null;

    public int? SelectedSlotIndex => IsTapSelection ? FromSlotIndex : null;

    public void StartFromList(int playerId, bool tapped = false)
    {
        PlayerId = playerId;
        FromSlotIndex = null;
        FromSub = false;
        IsTapSelection = tapped;
    }

    public void StartFromPitch(int playerId, int slotIndex, bool tapped = false)
    {
        PlayerId = playerId;
        FromSlotIndex = slotIndex;
        FromSub = false;
        IsTapSelection = tapped;
    }

    public void StartFromSub(int playerId, bool tapped = false)
    {
        PlayerId = playerId;
        FromSlotIndex = null;
        FromSub = true;
        IsTapSelection = tapped;
    }

    public void Clear()
    {
        PlayerId = null;
        FromSlotIndex = null;
        FromSub = false;
        IsTapSelection = false;
    }
}
