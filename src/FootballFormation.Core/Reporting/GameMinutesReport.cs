using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

public class GameMinutes
{
    /// <summary>Player id → position → seconds spent on the pitch in that position.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyDictionary<PlayerPosition, int>> SecondsByPlayer { get; init; }

    /// <summary>Everyone named in a lineup or a substitution, including players with zero seconds.</summary>
    public required IReadOnlySet<int> PlayerIds { get; init; }

    /// <summary>Who is on the pitch right now. Only populated while a half is being played.</summary>
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
/// A game that was run live carries the truth in its half timings and its
/// <see cref="GameSubstitution"/> rows. The lineup alone cannot express it —
/// <c>MatchSubstitutionService</c> rewrites it in place, so afterwards it shows only the
/// <em>final</em> occupants.
/// </para>
/// <para>
/// The choice is made per game, not per line-up, on <see cref="Game.HasActualTimings"/>: once a
/// match has been run live, a line-up with no kick-off is one the coach worked towards by hand
/// inside a half that is already accounted for, and crediting it a full period's minutes would
/// invent playing time.
/// </para>
/// <para>
/// Known limitation: only a substitution records a position change. The walk below starts from the
/// lineup as it <em>finally</em> stands and rewinds substitution rows, so a player who shifts
/// position mid-half without one is credited the position they ended in for the whole half,
/// the minutes before the shift included. The live screen's position swap
/// (<c>MatchSubstitutionService.SwapPositionsAsync</c>) is exactly that case: it rewrites the
/// lineup and writes nothing down, because a <see cref="GameSubstitution"/> would say someone left
/// the pitch. Totals stay right; only the split by position does. That is a gap in what gets
/// recorded, not in this calculation.
/// </para>
/// </summary>
public static class GameMinutesReport
{
    /// <param name="elapsedSeconds">The match clock right now, which closes off the running half.
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
                    Credit(seconds, entry.PlayerId, entry.Position, game.PeriodDurationSeconds);

                continue;
            }

            // A line-up that was never kicked off contributes no time — it is a plan for the
            // middle of a half whose minutes the half's own line-up already accounts for.
            if (period.StartedAtSeconds is not { } start) continue;

            var isLive = game.LivePeriodId == period.Id;
            var end = period.EndedAtSeconds ?? (isLive ? elapsedSeconds : start);

            var subs = game.Substitutions
                .Where(s => s.GamePeriodId == period.Id)
                .OrderBy(s => s.AtSeconds)
                // Two changes in the same second are a double substitution, and the walk below
                // only rewinds to the right kick-off lineup if it takes them in the order they
                // were made. The id is what says so — RecordedAt can be the same instant too.
                .ThenBy(s => s.Id)
                .ToList();

            // The lineup records where everyone stands *now*. Rewinding this half's
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
    /// Rounds seconds to the nearest minute rather than truncating: a half whistled at 29:50 is
    /// 30 minutes played, not 29. Every report that turns these seconds into minutes goes through
    /// here, so the same match cannot read one minute shorter on one page than on another.
    /// </summary>
    public static int ToMinutes(int seconds) => (int)Math.Round(seconds / 60.0);

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
