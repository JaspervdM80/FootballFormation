namespace FootballFormation.UI.Helpers;

/// <summary>
/// The drag currently in flight on the formation builder. Keeping the fields together
/// stops the page from having to reset them by hand after every drop.
/// </summary>
public class LineupDragState
{
    public int? PlayerId { get; private set; }

    /// <summary>Set only when the drag started from a pitch slot, which makes the drop a swap.</summary>
    public int? FromSlotIndex { get; private set; }

    /// <summary>Set when the drag started from the substitute bench.</summary>
    public bool FromSub { get; private set; }

    public void StartFromList(int playerId)
    {
        PlayerId = playerId;
        FromSlotIndex = null;
        FromSub = false;
    }

    public void StartFromPitch(int playerId, int slotIndex)
    {
        PlayerId = playerId;
        FromSlotIndex = slotIndex;
        FromSub = false;
    }

    public void StartFromSub(int playerId)
    {
        PlayerId = playerId;
        FromSlotIndex = null;
        FromSub = true;
    }

    public void Clear()
    {
        PlayerId = null;
        FromSlotIndex = null;
        FromSub = false;
    }
}
