using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// The drag currently in flight on the formation builder. Keeping the two fields together
/// stops the page from having to reset them by hand after every drop.
/// </summary>
public class LineupDragState
{
    public int? PlayerId { get; private set; }

    /// <summary>Set only when the drag started from a pitch slot, which makes the drop a swap.</summary>
    public PlayerPosition? FromPosition { get; private set; }

    public void StartFromList(int playerId)
    {
        PlayerId = playerId;
        FromPosition = null;
    }

    public void StartFromPitch(int playerId, PlayerPosition from)
    {
        PlayerId = playerId;
        FromPosition = from;
    }

    public void Clear()
    {
        PlayerId = null;
        FromPosition = null;
    }
}
