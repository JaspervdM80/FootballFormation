namespace FootballFormation.Core.Models;

/// <summary>
/// Turns a formation and a lineup into the eleven slots a pitch draws, in slot order.
/// <para>
/// Every pitch in the app — the drag-drop builder, the shareable overview and the live screen —
/// answers the same two questions: which positions does this shape field, and who is standing in
/// each one. Keeping the answer here means a lineup can never be laid out one way on one screen
/// and another way on the next.
/// </para>
/// </summary>
public static class FormationSlots
{
    /// <summary>The formation's slots, keeper first. Slot 0 is always the goalkeeper; 1–10 are the
    /// outfield positions in the order <see cref="FormationTypeExtensions.DefaultPositions"/> lists
    /// them, which is what <see cref="GamePlayerPosition.SlotIndex"/> refers to.</summary>
    public static PlayerPosition[] For(FormationType formation) =>
        [PlayerPosition.GK, .. formation.DefaultPositions()];

    /// <summary>
    /// Places the lineup's starters into <paramref name="slots"/>, returning one entry per slot and
    /// null where the slot is empty. Substitutes are never placed.
    /// <para>
    /// <see cref="GamePlayerPosition.SlotIndex"/> is the source of truth and is honoured first.
    /// Only entries without one — lineups saved before slots were recorded — fall back to matching
    /// on position, which is why the two passes cannot be collapsed into one: a legacy entry must
    /// not take a slot that an explicit one is entitled to.
    /// </para>
    /// </summary>
    public static GamePlayerPosition?[] Assign(
        PlayerPosition[] slots, IEnumerable<GamePlayerPosition> lineup)
    {
        var assignments = new GamePlayerPosition?[slots.Length];
        var starters = lineup.Where(entry => !entry.IsSubstitute).ToList();

        foreach (var entry in starters.Where(entry => entry.SlotIndex is not null).ToList())
        {
            var slot = entry.SlotIndex!.Value;
            if (slot >= 0 && slot < slots.Length && assignments[slot] is null)
            {
                assignments[slot] = entry;
                starters.Remove(entry);
            }
        }

        for (var slot = 0; slot < slots.Length; slot++)
        {
            if (assignments[slot] is not null) continue;

            var match = starters.FirstOrDefault(entry => entry.Position == slots[slot]);
            if (match is not null)
            {
                assignments[slot] = match;
                starters.Remove(match);
            }
        }

        return assignments;
    }

    /// <summary>Convenience overload for callers that only have the formation.</summary>
    public static GamePlayerPosition?[] Assign(
        FormationType formation, IEnumerable<GamePlayerPosition> lineup) =>
        Assign(For(formation), lineup);

    /// <summary>
    /// How many slots share <paramref name="slotIndex"/>'s position, and which of them this one is.
    /// The pitch spreads duplicates (two centre-backs, three midfielders) across fixed coordinates,
    /// so it needs the ordinal, not just the position.
    /// </summary>
    public static (int Index, int Count) OrdinalOf(PlayerPosition[] slots, int slotIndex)
    {
        var position = slots[slotIndex];

        return (
            Index: slots.Take(slotIndex).Count(p => p == position),
            Count: slots.Count(p => p == position));
    }
}
