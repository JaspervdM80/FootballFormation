namespace FootballFormation.Core.Reporting;

public readonly record struct PitchSpot(int? SlotIndex, PlayerPosition Position);

/// Who stood where from <see cref="FromSeconds"/> until the next change. Zero-length when two changes share a second.
public record LineupStretch(int FromSeconds, int ToSeconds, IReadOnlyDictionary<int, PitchSpot> OnPitch);

public enum LineupChangeKind { Substitution, Injury, Swap }

/// <see cref="PlayerId"/> is who goes off, gets hurt, or is the first of a swap; <see cref="OtherPlayerId"/> who comes on or the second.
public record LineupChange(
    LineupChangeKind Kind, int AtSeconds, DateTime RecordedAt, int Id, int PlayerId, int? OtherPlayerId);

public record HalfWalkResult(
    List<LineupStretch> Stretches,
    IReadOnlyDictionary<int, PitchSpot> AtWhistle,
    IReadOnlyDictionary<(LineupChangeKind Kind, int Id), PitchSpot> SpotOf,
    IReadOnlyList<LineupChange> Skipped);

/// A half's line-up stores only where everyone stood at the whistle, so this rewinds its changes to the kick-off and walks them forward
/// again. A substitute takes over the spot of whoever went off, as she stood then, so a timed swap before or after carries through.
public sealed record HalfLineupWalk
{
    public required IReadOnlyDictionary<int, PitchSpot> KickOff { get; init; }
    public required IReadOnlyList<LineupChange> Changes { get; init; }

    /// The changes at the seconds they were stored at. A correction settles from these: settling twice could cross swaps it must not.
    private IReadOnlyList<LineupChange> Recorded { get; init; } = [];

    public static HalfLineupWalk Rewind(Game game, GamePeriod period)
    {
        var recordedChanges = ChangesIn(game, period);
        var changes = SettleArrivals([.. recordedChanges]);
        var onPitch = period.PlayerPositions
            .Where(p => !p.IsSubstitute)
            .ToDictionary(p => p.PlayerId, p => new PitchSpot(p.SlotIndex, p.Position));

        var recorded = RecordedSpots(game, period);

        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            switch (change.Kind)
            {
                case LineupChangeKind.Swap:
                    Exchange(onPitch, change.PlayerId, change.OtherPlayerId!.Value);
                    break;

                case LineupChangeKind.Substitution:
                    // Follows the slot, as undoing a substitution does: a swap that wrote no row can have moved her since.
                    var spot = onPitch.Remove(change.OtherPlayerId!.Value, out var heldBy) ? heldBy : recorded[change];
                    onPitch[change.PlayerId] = spot;
                    break;

                case LineupChangeKind.Injury:
                    onPitch[change.PlayerId] = recorded[change];
                    break;
            }
        }

        return new HalfLineupWalk { KickOff = onPitch, Changes = changes, Recorded = recordedChanges };
    }

    /// The walk with one change added or left out — what a correction would make of the half — rather than the one on file.
    public HalfLineupWalk With(LineupChange? added = null, LineupChange? removed = null)
    {
        var recorded = Recorded.Where(c => removed is null || (c.Kind, c.Id) != (removed.Kind, removed.Id)).ToList();
        if (added is not null) recorded.Add(added);

        var sorted = Sorted(recorded);
        return this with { Changes = SettleArrivals([.. sorted]), Recorded = sorted };
    }

    public HalfWalkResult Walk(int startSeconds, int endSeconds)
    {
        var onPitch = new Dictionary<int, PitchSpot>(KickOff);
        var stretches = new List<LineupStretch>();
        var spotOf = new Dictionary<(LineupChangeKind, int), PitchSpot>();
        var skipped = new List<LineupChange>();

        var cursor = startSeconds;
        foreach (var change in Changes)
        {
            stretches.Add(new LineupStretch(cursor, Math.Max(cursor, change.AtSeconds), new Dictionary<int, PitchSpot>(onPitch)));
            cursor = Math.Max(cursor, change.AtSeconds);

            switch (change.Kind)
            {
                case LineupChangeKind.Swap:
                    if (!Exchange(onPitch, change.PlayerId, change.OtherPlayerId!.Value)) skipped.Add(change);
                    break;

                case LineupChangeKind.Substitution:
                    if (!onPitch.Remove(change.PlayerId, out var vacated))
                    {
                        skipped.Add(change);
                        break;
                    }
                    spotOf[(change.Kind, change.Id)] = vacated;
                    onPitch[change.OtherPlayerId!.Value] = vacated;
                    break;

                case LineupChangeKind.Injury:
                    if (onPitch.Remove(change.PlayerId, out var left)) spotOf[(change.Kind, change.Id)] = left;
                    else skipped.Add(change);
                    break;
            }
        }

        stretches.Add(new LineupStretch(cursor, Math.Max(cursor, endSeconds), new Dictionary<int, PitchSpot>(onPitch)));

        return new HalfWalkResult(stretches, onPitch, spotOf, skipped);
    }

    /// The stretch in force at <paramref name="atSeconds"/>: a change at that very second has already happened.
    public static LineupStretch At(HalfWalkResult walk, int atSeconds) =>
        walk.Stretches.LastOrDefault(s => s.FromSeconds <= atSeconds) ?? walk.Stretches[0];

    /// An injury a substitution already accounts for is left out, or the rewind would take the same player off twice.
    private static List<LineupChange> ChangesIn(Game game, GamePeriod period)
    {
        var subs = game.Substitutions
            .Where(s => s.GamePeriodId == period.Id)
            .Select(s => new LineupChange(LineupChangeKind.Substitution, s.AtSeconds, s.RecordedAt, s.Id, s.PlayerOffId, s.PlayerOnId));

        var unreplaced = game.Injuries
            .Where(i => i.GamePeriodId == period.Id && !game.WasReplaced(i))
            .Select(i => new LineupChange(LineupChangeKind.Injury, i.AtSeconds, i.RecordedAt, i.Id, i.PlayerId, null));

        var swaps = game.PositionSwaps
            .Where(s => s.GamePeriodId == period.Id)
            .Select(s => new LineupChange(LineupChangeKind.Swap, s.AtSeconds, s.RecordedAt, s.Id, s.PlayerAId, s.PlayerBId));

        return Sorted(subs.Concat(unreplaced).Concat(swaps));
    }

    /// A swap this soon after a player came on is where she was put to play — the spot she entered in was only the way onto the pitch.
    public const int ArrivalSettlingSeconds = 60;

    /// The order MatchTimelineReport reads them in. Ids are per table, so across kinds only RecordedAt settles two changes in one second.
    private static List<LineupChange> Sorted(IEnumerable<LineupChange> changes) =>
        [.. changes.OrderBy(c => c.AtSeconds).ThenBy(c => c.RecordedAt).ThenBy(c => c.Id)];

    /// Only when the arrival is the last change either player was part of: anything between would be walked out of order. Placed straight
    /// after the arrival rather than re-sorted, so the order never rests on the RecordedAt of rows written without one.
    private static List<LineupChange> SettleArrivals(List<LineupChange> changes)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            var swap = changes[i];
            if (swap.Kind != LineupChangeKind.Swap) continue;

            var latest = changes.Take(i).LastOrDefault(c =>
                c.PlayerId == swap.PlayerId || c.OtherPlayerId == swap.PlayerId
                || c.PlayerId == swap.OtherPlayerId || c.OtherPlayerId == swap.OtherPlayerId);

            if (latest is { Kind: LineupChangeKind.Substitution, OtherPlayerId: { } cameOn }
                && (cameOn == swap.PlayerId || cameOn == swap.OtherPlayerId)
                && swap.AtSeconds - latest.AtSeconds < ArrivalSettlingSeconds)
            {
                changes.RemoveAt(i);
                changes.Insert(changes.IndexOf(latest) + 1, swap with { AtSeconds = latest.AtSeconds });
            }
        }

        return changes;
    }

    private static Dictionary<LineupChange, PitchSpot> RecordedSpots(Game game, GamePeriod period)
    {
        var spots = new Dictionary<LineupChange, PitchSpot>();

        foreach (var s in game.Substitutions.Where(s => s.GamePeriodId == period.Id))
            spots[new LineupChange(LineupChangeKind.Substitution, s.AtSeconds, s.RecordedAt, s.Id, s.PlayerOffId, s.PlayerOnId)] =
                new PitchSpot(s.SlotIndex, s.Position);

        foreach (var i in game.Injuries.Where(i => i.GamePeriodId == period.Id))
            spots[new LineupChange(LineupChangeKind.Injury, i.AtSeconds, i.RecordedAt, i.Id, i.PlayerId, null)] =
                new PitchSpot(i.SlotIndex, i.Position);

        return spots;
    }

    private static bool Exchange(Dictionary<int, PitchSpot> onPitch, int a, int b)
    {
        if (!onPitch.TryGetValue(a, out var spotA) || !onPitch.TryGetValue(b, out var spotB)) return false;

        onPitch[a] = spotB;
        onPitch[b] = spotA;
        return true;
    }
}
