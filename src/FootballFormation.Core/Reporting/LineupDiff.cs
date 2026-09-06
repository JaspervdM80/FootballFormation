namespace FootballFormation.Core.Reporting;

/// One on-for-off change between two line-ups. Both sides are always present — an unbalanced change carries no substitution, so
/// <see cref="LineupDiff.Swaps"/> drops it.
public readonly record struct LineupSwap(int PlayerOffId, int PlayerOnId, int SlotIndex, PlayerPosition Position);

/// The slot-by-slot difference between two starting line-ups. Shared by <see cref="PlannedChangesReport"/> — what a coach is about to do —
/// and the half-time substitution generator — what actually happened when the next half kicked off — so the two can never drift apart.
public static class LineupDiff
{
    /// TryAdd rather than Add: a slot can only be held once, but a line-up saved by an older build is not guaranteed to honour that, and
    /// throwing on data already stored helps nobody.
    public static Dictionary<int, GamePlayerPosition> StartersBySlot(GamePeriod lineup)
    {
        var bySlot = new Dictionary<int, GamePlayerPosition>();

        foreach (var position in lineup.PlayerPositions.Where(p => !p.IsSubstitute && p.SlotIndex is not null))
            bySlot.TryAdd(position.SlotIndex!.Value, position);

        return bySlot;
    }

    /// An arrival pairs with whoever held the slot she is taking, which is the swap a coach would call out; when that player is staying on
    /// — a shuffle rather than a straight swap — the next unpaired departure is used instead. Unbalanced sides come back null on purpose:
    /// eleven out and ten in is worth a caller flagging, not hiding.
    public static List<(GamePlayerPosition? Off, GamePlayerPosition? On)> Pairs(
        Dictionary<int, GamePlayerPosition> before,
        Dictionary<int, GamePlayerPosition> after)
    {
        var beforeIds = before.Values.Select(p => p.PlayerId).ToHashSet();
        var afterIds = after.Values.Select(p => p.PlayerId).ToHashSet();

        // Slot order, so the list reads like a team sheet rather than in whatever order the line-up rows were stored.
        var leaving = before.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !afterIds.Contains(p.PlayerId)).ToList();
        var arriving = after.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !beforeIds.Contains(p.PlayerId)).ToList();

        var unpaired = new List<GamePlayerPosition>(leaving);
        var pairs = new List<(GamePlayerPosition? Off, GamePlayerPosition? On)>();

        foreach (var on in arriving)
        {
            var predecessor = before.GetValueOrDefault(on.SlotIndex!.Value);
            var off = unpaired.FirstOrDefault(p => p.PlayerId == predecessor?.PlayerId)
                ?? unpaired.FirstOrDefault();

            if (off is not null) unpaired.Remove(off);

            pairs.Add((off, on));
        }

        pairs.AddRange(unpaired.Select(off => ((GamePlayerPosition?)off, (GamePlayerPosition?)null)));

        return pairs;
    }

    /// The complete on-for-off swaps between two line-ups: the ones a <see cref="GameSubstitution"/> can carry, since it needs both a
    /// player off and a player on. A change touching anyone in <paramref name="excludedPlayerIds"/> is dropped — an injured player is
    /// already off the pitch and must not be handed a phantom half-time move.
    public static List<LineupSwap> Swaps(GamePeriod before, GamePeriod after, ISet<int> excludedPlayerIds) =>
        [.. Pairs(StartersBySlot(before), StartersBySlot(after))
            .Where(pair => pair.Off is not null && pair.On is not null)
            .Where(pair => !excludedPlayerIds.Contains(pair.Off!.PlayerId)
                           && !excludedPlayerIds.Contains(pair.On!.PlayerId))
            .Select(pair => new LineupSwap(
                pair.Off!.PlayerId, pair.On!.PlayerId, pair.On!.SlotIndex!.Value, pair.On!.Position))];
}
