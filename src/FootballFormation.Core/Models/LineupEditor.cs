namespace FootballFormation.Core.Models;

// The builder's edits to one period's line-up, in place. Who stands where is always FormationSlots.Assign's answer, so an edit here and
// the pitch drawing it can never disagree about which slot a player is in.
public sealed class LineupEditor(List<GamePlayerPosition> lineup, PlayerPosition[] slots)
{
    public GamePlayerPosition?[] Assignments() => FormationSlots.Assign(slots, lineup);

    public void MoveOnPitch(int fromSlot, int toSlot)
    {
        var assignments = Assignments();
        if (assignments[fromSlot] is not { } moving) return;

        var displaced = assignments[toSlot];
        Place(moving, toSlot);
        if (displaced is not null) Place(displaced, fromSlot);
    }

    // From the bench the player she replaces goes back to it; from the squad list she replaces them outright.
    public void PutOnPitch(Player player, int slot, bool fromBench)
    {
        lineup.RemoveAll(entry => entry.PlayerId == player.Id);

        if (Assignments()[slot] is { } occupant)
        {
            if (fromBench) occupant.SendToBench();
            else lineup.Remove(occupant);
        }

        lineup.Add(Starter(player, slots[slot], slot));
    }

    public void PutOnBench(Player player)
    {
        lineup.RemoveAll(entry => entry.PlayerId == player.Id);
        lineup.Add(Substitute(player));
    }

    // False when either of them is not where the caller thought, which a stale drag can leave behind.
    public bool SwapWithSub(int starterId, int starterSlot, int subId)
    {
        var starter = lineup.FirstOrDefault(entry => entry.PlayerId == starterId && !entry.IsSubstitute);
        var sub = lineup.FirstOrDefault(entry => entry.PlayerId == subId && entry.IsSubstitute);
        if (starter is null || sub is null) return false;

        starter.SendToBench();
        sub.IsSubstitute = false;
        Place(sub, starterSlot);
        return true;
    }

    public void BenchAt(int slot) => Assignments()[slot]?.SendToBench();

    public void Remove(int playerId) => lineup.RemoveAll(entry => entry.PlayerId == playerId);

    public static GamePlayerPosition Starter(Player player, PlayerPosition position, int slot) =>
        new() { PlayerId = player.Id, Player = player, Position = position, SlotIndex = slot };

    public static GamePlayerPosition Substitute(Player player) =>
        new() { PlayerId = player.Id, Player = player, Position = player.PreferredPosition, IsSubstitute = true };

    // Fresh entries with Id = 0, so saving the copy inserts rows rather than claiming the source's.
    public static List<GamePlayerPosition> Copy(IEnumerable<GamePlayerPosition> source) =>
    [
        .. source.Select(entry => new GamePlayerPosition
        {
            PlayerId = entry.PlayerId,
            Player = entry.Player,
            Position = entry.Position,
            SlotIndex = entry.SlotIndex,
            IsSubstitute = entry.IsSubstitute
        })
    ];

    private void Place(GamePlayerPosition entry, int slot)
    {
        entry.Position = slots[slot];
        entry.SlotIndex = slot;
    }
}
