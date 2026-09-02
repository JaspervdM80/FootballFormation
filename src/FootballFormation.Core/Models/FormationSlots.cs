namespace FootballFormation.Core.Models;

/// The one answer to "which positions does this shape field, and who is standing in each", so a line-up cannot be laid out one way on
/// the builder and another on the live screen.
public static class FormationSlots
{
    /// Slot 0 is always the goalkeeper; 1–10 follow <see cref="FormationTypeExtensions.DefaultPositions"/>, which is what
    /// <see cref="GamePlayerPosition.SlotIndex"/> refers to.
    public static PlayerPosition[] For(FormationType formation) =>
        [PlayerPosition.GK, .. formation.DefaultPositions()];

    /// The two passes cannot be collapsed into one: <see cref="GamePlayerPosition.SlotIndex"/> is the source of truth, and an entry
    /// saved before slots were recorded must not take a slot an explicit one is entitled to.
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

    public static GamePlayerPosition?[] Assign(
        FormationType formation, IEnumerable<GamePlayerPosition> lineup) =>
        Assign(For(formation), lineup);

    /// Moves a line-up from one shape into another: everyone keeps the slot she was standing in, and her recorded position follows that
    /// slot. The pitch reads the slot, but the playing-time table and the position statistics read what is stored on the entry.
    public static void Reshape(
        IEnumerable<GamePlayerPosition> lineup, PlayerPosition[] from, PlayerPosition[] to)
    {
        var standing = Assign(from, lineup);

        for (var slot = 0; slot < standing.Length && slot < to.Length; slot++)
        {
            if (standing[slot] is not { } entry) continue;

            entry.SlotIndex = slot;
            entry.Position = to[slot];
        }
    }

    /// The pitch spreads duplicates — two centre-backs, three midfielders — across fixed coordinates, so it needs the ordinal as well as
    /// the position.
    public static (int Index, int Count) OrdinalOf(PlayerPosition[] slots, int slotIndex)
    {
        var position = slots[slotIndex];

        return (
            Index: slots.Take(slotIndex).Count(p => p == position),
            Count: slots.Count(p => p == position));
    }
}
