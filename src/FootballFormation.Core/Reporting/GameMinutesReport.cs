using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>Who was on the pitch, for how long, and in which position, over one game.</summary>
public class GameMinutes
{
    /// <summary>Player id → position → seconds spent on the pitch in that position.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyDictionary<PlayerPosition, int>> SecondsByPlayer { get; init; }

    /// <summary>Everyone named in a lineup or a substitution, including players with zero seconds.</summary>
    public required IReadOnlySet<int> PlayerIds { get; init; }

    /// <summary>Who is on the pitch right now. Only populated while a period is live.</summary>
    public required IReadOnlySet<int> OnPitchNow { get; init; }

    /// <summary>
    /// False when the figures are the planned <c>periods × period length</c> estimate rather than
    /// real timings, so callers can label them as such.
    /// </summary>
    public required bool IsActual { get; init; }

    public IReadOnlyDictionary<PlayerPosition, int> PositionsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions)
            ? positions
            : new Dictionary<PlayerPosition, int>();

    public int SecondsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions) ? positions.Values.Sum() : 0;
}

/// <summary>
/// Playing time for one game, per player and per position. The single place that decides whether
/// a game's minutes come from what actually happened or from what was planned.
/// <para>
/// A game that was run live carries the truth: <see cref="GamePeriod.StartedAtSeconds"/> and
/// <see cref="GamePeriod.EndedAtSeconds"/> say when each period ran, and the
/// <see cref="GameSubstitution"/> rows say who swapped with whom, when, and into which position.
/// The lineup alone cannot express any of that — <c>LiveMatchService.SubstituteAsync</c> rewrites
/// it in place, so afterwards it only shows the <em>final</em> occupants. A game that was never run
/// live has no timings at all, and there the planned lineup is the only answer available.
/// </para>
/// <para>
/// The choice is made per game, not per period, on <see cref="Game.HasActualTimings"/>: once a
/// match has been run live, a period with no kick-off is one that was never played, and crediting
/// its lineup a full period's minutes would invent playing time.
/// </para>
/// <para>
/// Known limitation: the live screen only records a position change as part of a substitution, so
/// a player who shifts from one position to another mid-period without a swap keeps the earlier
/// position for those minutes. That is a gap in what gets recorded, not in this calculation.
/// </para>
/// </summary>
public static class GameMinutesReport
{
    /// <param name="elapsedSeconds">The match clock right now, which closes off a running period.
    /// Irrelevant for a settled game — any value will do.</param>
    public static GameMinutes Build(Game game, int elapsedSeconds = 0)
    {
        var seconds = new Dictionary<int, Dictionary<PlayerPosition, int>>();
        var known = new HashSet<int>();
        var onPitchNow = new HashSet<int>();
        var isActual = game.HasActualTimings;

        foreach (var period in game.Periods.OrderBy(p => p.PeriodType))
        {
            foreach (var entry in period.PlayerPositions) known.Add(entry.PlayerId);

            if (!isActual)
            {
                // No timings anywhere in this game: everyone fielded gets the whole period in the
                // position they were planned for. Substitutes get nothing, as before.
                foreach (var entry in period.PlayerPositions.Where(p => !p.IsSubstitute))
                    Credit(seconds, entry.PlayerId, entry.Position, game.PeriodDurationMinutes * 60);

                continue;
            }

            // A period that was never kicked off contributes no time — only a planned lineup.
            if (period.StartedAtSeconds is not { } start) continue;

            var isLive = game.LivePeriodId == period.Id;
            var end = period.EndedAtSeconds ?? (isLive ? elapsedSeconds : start);

            var subs = game.Substitutions
                .Where(s => s.GamePeriodId == period.Id)
                .OrderBy(s => s.AtSeconds)
                .ThenBy(s => s.RecordedAt)
                .ToList();

            // The lineup records where everyone stands *now*. Rewinding this period's
            // substitutions recovers who stood where when it kicked off, which is the only point
            // the forward walk below can start from. GameSubstitution.Position is the position
            // that changed hands, so it hands the slot back to the player who came off.
            var onPitch = period.PlayerPositions
                .Where(p => !p.IsSubstitute)
                .ToDictionary(p => p.PlayerId, p => p.Position);

            for (var i = subs.Count - 1; i >= 0; i--)
            {
                onPitch.Remove(subs[i].PlayerOnId);
                onPitch[subs[i].PlayerOffId] = subs[i].Position;
            }

            var cursor = start;
            foreach (var sub in subs)
            {
                CreditAll(seconds, onPitch, sub.AtSeconds - cursor);
                onPitch.Remove(sub.PlayerOffId);
                onPitch[sub.PlayerOnId] = sub.Position;
                known.Add(sub.PlayerOnId);
                cursor = sub.AtSeconds;
            }

            CreditAll(seconds, onPitch, end - cursor);

            if (isLive) onPitchNow = [.. onPitch.Keys];
        }

        return new GameMinutes
        {
            SecondsByPlayer = seconds.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<PlayerPosition, int>)kv.Value),
            PlayerIds = known,
            OnPitchNow = onPitchNow,
            IsActual = isActual
        };
    }

    /// <summary>
    /// Adds a stretch of time to everyone on the pitch, each in the position they held for it.
    /// Non-positive spans are ignored — two substitutions in the same second are normal and must
    /// not subtract time.
    /// </summary>
    private static void CreditAll(
        Dictionary<int, Dictionary<PlayerPosition, int>> seconds,
        Dictionary<int, PlayerPosition> onPitch,
        int span)
    {
        if (span <= 0) return;

        foreach (var (playerId, position) in onPitch)
            Credit(seconds, playerId, position, span);
    }

    private static void Credit(
        Dictionary<int, Dictionary<PlayerPosition, int>> seconds,
        int playerId,
        PlayerPosition position,
        int span)
    {
        if (span <= 0) return;

        if (!seconds.TryGetValue(playerId, out var positions))
            seconds[playerId] = positions = [];

        positions[position] = positions.GetValueOrDefault(position) + span;
    }
}
