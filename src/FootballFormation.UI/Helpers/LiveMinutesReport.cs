using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>How long one player has actually been on the pitch in a live match.</summary>
public record LiveMinutesRow(Player Player, int Seconds, bool IsOnPitch)
{
    public int Minutes => Seconds / 60;
}

/// <summary>
/// Exact playing time for a match being run live, from the period clock anchors and the
/// substitution events. This is the real thing, unlike <see cref="PlayingTimeReport"/>, which can
/// only estimate <c>periods played × period length</c> because a planned lineup has no timings.
/// It stays separate rather than replacing it: a game that was never run live has no anchors to
/// read, so the estimate remains the only answer there.
/// </summary>
public static class LiveMinutesReport
{
    /// <param name="elapsedSeconds">The match clock right now, which closes off the running period.</param>
    /// <param name="findPlayer">Resolves an id to a player; rows for unknown ids are dropped.</param>
    public static List<LiveMinutesRow> Build(Game game, int elapsedSeconds, Func<int, Player?> findPlayer)
    {
        var seconds = new Dictionary<int, int>();
        var known = new HashSet<int>();
        var onPitchNow = new HashSet<int>();

        foreach (var period in game.Periods.OrderBy(p => p.PeriodType))
        {
            foreach (var entry in period.PlayerPositions) known.Add(entry.PlayerId);

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
            // substitutions recovers who was on the pitch when it kicked off, which is the only
            // point the forward walk below can start from.
            var onPitch = period.PlayerPositions
                .Where(p => !p.IsSubstitute)
                .Select(p => p.PlayerId)
                .ToHashSet();

            for (var i = subs.Count - 1; i >= 0; i--)
            {
                onPitch.Remove(subs[i].PlayerOnId);
                onPitch.Add(subs[i].PlayerOffId);
            }

            var cursor = start;
            foreach (var sub in subs)
            {
                Credit(seconds, onPitch, sub.AtSeconds - cursor);
                onPitch.Remove(sub.PlayerOffId);
                onPitch.Add(sub.PlayerOnId);
                known.Add(sub.PlayerOnId);
                cursor = sub.AtSeconds;
            }

            Credit(seconds, onPitch, end - cursor);

            if (isLive) onPitchNow = onPitch;
        }

        return [.. known
            .Select(id => (Id: id, Player: findPlayer(id)))
            .Where(x => x.Player is not null)
            .Select(x => new LiveMinutesRow(x.Player!, seconds.GetValueOrDefault(x.Id), onPitchNow.Contains(x.Id)))
            .OrderByDescending(r => r.Seconds)
            .ThenBy(r => r.Player.ShirtNumber ?? 99)
            .ThenBy(r => r.Player.DisplayName)];
    }

    /// <summary>
    /// Adds a stretch of time to everyone who was on the pitch for it. Non-positive spans are
    /// ignored — two substitutions in the same second are normal and must not subtract time.
    /// </summary>
    private static void Credit(Dictionary<int, int> seconds, HashSet<int> onPitch, int span)
    {
        if (span <= 0) return;

        foreach (var id in onPitch)
            seconds[id] = seconds.GetValueOrDefault(id) + span;
    }
}
