namespace FootballFormation.Core.Reporting;

/// <summary>
/// A player coming off for one coming on. Either side can be null when the line-ups do not
/// balance — eleven out and ten in is a line-up worth flagging, not one worth hiding.
/// </summary>
public record PlannedSubstitution(Player? PlayerOff, Player? PlayerOn, PlayerPosition Position);

public record PlannedMove(Player Player, PlayerPosition From, PlayerPosition To);

/// <summary>
/// The same swap in terms of the line-up rows rather than the players: a slot and a position
/// change hands, and neither is a property of a name.
/// </summary>
internal record PlannedSwap(GamePlayerPosition? Off, GamePlayerPosition? On);

/// <summary>What the next line-up does: who is swapped, and who shifts position.</summary>
public record PlannedChanges(List<PlannedSubstitution> Substitutions, List<PlannedMove> Moves)
{
    public static readonly PlannedChanges None = new([], []);

    public bool IsEmpty => Substitutions.Count == 0 && Moves.Count == 0;
}

/// <summary>
/// The changes the planned line-ups imply. A quarters game is planned as two line-ups per half,
/// and the difference between them is exactly what is due midway through that half — so the live
/// screen can offer it as a reference without ever mentioning a quarter.
/// <para>
/// Substitutions and position moves are kept apart on purpose. Rewriting a back four commonly
/// touches every slot while only one player actually leaves the pitch, and a flat list of slot
/// differences buries that one substitution among six shuffles.
/// </para>
/// <para>
/// Only the swaps still open to the coach are reported. Play overtakes a plan: once the player it
/// takes off has been taken off live, the difference between the two line-ups still names their
/// slot, but it now proposes to withdraw whoever came on for them — a substitution nobody planned.
/// </para>
/// </summary>
public static class PlannedChangesReport
{
    /// <param name="half">The line-up the half is being played with. Live substitutions have
    /// already been applied to it, so the changes shown stay true to who is on the pitch.</param>
    /// <param name="plan">The line-up planned to take over partway through that half.</param>
    /// <param name="findPlayer">Resolves an id to a player; unknown ids come back as null.</param>
    /// <param name="liveChanges">The substitutions already made in <paramref name="half"/>.
    /// They decide which swaps are still worth showing — see <see cref="KickOffStarters"/>.</param>
    public static PlannedChanges Build(
        GamePeriod half,
        GamePeriod plan,
        Func<int, Player?> findPlayer,
        IEnumerable<GameSubstitution> liveChanges)
    {
        var before = StartersBySlot(half);
        var after = StartersBySlot(plan);

        return new PlannedChanges(
            [.. PairUp(before, after, KickOffStarters(before.Values, liveChanges))
                .Select(swap => Name(swap, findPlayer))],
            Moves(before, after, findPlayer));
    }

    /// <summary>
    /// A swap as the screen says it. The position is the one being taken over, which for a player
    /// coming off with nobody named to replace them is the one they are vacating.
    /// </summary>
    private static PlannedSubstitution Name(PlannedSwap swap, Func<int, Player?> findPlayer) =>
        new(swap.Off is null ? null : findPlayer(swap.Off.PlayerId),
            swap.On is null ? null : findPlayer(swap.On.PlayerId),
            (swap.On ?? swap.Off)!.Position);

    /// <summary>
    /// Whether the coach can still make this swap as planned. It names the player the plan takes
    /// off, and once the touchline has already taken them off the swap is about somebody else —
    /// whoever inherited the slot — which is not what was planned for them. A swap with nobody
    /// named to come off is kept: an unbalanced line-up is worth flagging.
    /// </summary>
    private static bool IsStillViable(PlannedSwap swap, HashSet<int> kickOffStarters) =>
        swap.Off is null || kickOffStarters.Contains(swap.Off.PlayerId);

    /// <summary>
    /// Who was on the pitch when the half kicked off. The line-up records where everyone stands
    /// <em>now</em>, so rewinding the substitutions made since is the only way back to the eleven
    /// the plan was written against — the same walk <see cref="GameMinutesReport"/> makes.
    /// </summary>
    private static HashSet<int> KickOffStarters(
        IEnumerable<GamePlayerPosition> onPitchNow, IEnumerable<GameSubstitution> liveChanges)
    {
        var starters = onPitchNow.Select(p => p.PlayerId).ToHashSet();

        // Newest first, so a slot changing hands twice unwinds through the player who held it in
        // between rather than skipping straight past them. The id settles a double substitution,
        // where both changes share a second.
        foreach (var sub in liveChanges.OrderByDescending(s => s.AtSeconds).ThenByDescending(s => s.Id))
        {
            starters.Remove(sub.PlayerOnId);
            starters.Add(sub.PlayerOffId);
        }

        return starters;
    }

    /// <summary>
    /// Matches who goes off to who comes on. An arrival is paired with whoever held the slot they
    /// are taking, which is the swap a coach would call out; when that player is staying on the
    /// pitch — a shuffle rather than a straight swap — the next unpaired departure is used instead.
    /// </summary>
    private static List<PlannedSwap> PairUp(
        Dictionary<int, GamePlayerPosition> before,
        Dictionary<int, GamePlayerPosition> after,
        HashSet<int> kickOffStarters)
    {
        var beforeIds = before.Values.Select(p => p.PlayerId).ToHashSet();
        var afterIds = after.Values.Select(p => p.PlayerId).ToHashSet();

        // In slot order, so the list reads back to front like a team sheet rather than in
        // whatever order the lineup rows happen to have been stored.
        var leaving = before.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !afterIds.Contains(p.PlayerId)).ToList();
        var arriving = after.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !beforeIds.Contains(p.PlayerId)).ToList();

        var unpaired = new List<GamePlayerPosition>(leaving);
        var swaps = new List<PlannedSwap>();

        foreach (var on in arriving)
        {
            var predecessor = before.GetValueOrDefault(on.SlotIndex!.Value);
            var off = unpaired.FirstOrDefault(p => p.PlayerId == predecessor?.PlayerId)
                ?? unpaired.FirstOrDefault();

            if (off is not null) unpaired.Remove(off);

            swaps.Add(new PlannedSwap(off, on));
        }

        // Anyone left over comes off with nobody named to replace them.
        swaps.AddRange(unpaired.Select(off => new PlannedSwap(off, null)));

        return [.. swaps.Where(swap => IsStillViable(swap, kickOffStarters))];
    }

    private static List<PlannedMove> Moves(
        Dictionary<int, GamePlayerPosition> before,
        Dictionary<int, GamePlayerPosition> after,
        Func<int, Player?> findPlayer)
    {
        var stayingBySlot = before.Values.ToDictionary(p => p.PlayerId, p => p);

        return [.. after.OrderBy(e => e.Key).Select(e => e.Value)
            .Select(on => (On: on, Off: stayingBySlot.GetValueOrDefault(on.PlayerId)))
            .Where(x => x.Off is not null && x.Off.Position != x.On.Position)
            .Select(x => (x.On, x.Off, Player: findPlayer(x.On.PlayerId)))
            .Where(x => x.Player is not null)
            .Select(x => new PlannedMove(x.Player!, x.Off!.Position, x.On.Position))];
    }

    /// <summary>
    /// The starters keyed by the slot they occupy. A slot can only be held once, but a lineup
    /// saved by an older build is not guaranteed to honour that, so the first entry wins rather
    /// than the lookup throwing on data that is already stored.
    /// </summary>
    private static Dictionary<int, GamePlayerPosition> StartersBySlot(GamePeriod lineup)
    {
        var bySlot = new Dictionary<int, GamePlayerPosition>();

        foreach (var position in lineup.PlayerPositions.Where(p => !p.IsSubstitute && p.SlotIndex is not null))
            bySlot.TryAdd(position.SlotIndex!.Value, position);

        return bySlot;
    }
}
