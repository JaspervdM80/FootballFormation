namespace FootballFormation.Core.Reporting;

public record LiveMinutesRow(Player Player, int Seconds, bool IsOnPitch)
{
    public int Minutes => Seconds / 60;
}

/// Ordering and shaping only — the calculation is <see cref="GameMinutesReport"/>'s, which the stats pages read too, so the live screen
/// and the season table can never disagree about how long someone played.
public static class LiveMinutesReport
{
    /// Rows whose id <paramref name="findPlayer"/> cannot resolve are dropped.
    public static List<LiveMinutesRow> Build(Game game, int elapsedSeconds, Func<int, Player?> findPlayer)
    {
        var minutes = GameMinutesReport.Build(game, elapsedSeconds);

        return [.. minutes.PlayerIds
            .Select(id => (Id: id, Player: findPlayer(id)))
            .Where(x => x.Player is not null)
            .Select(x => new LiveMinutesRow(
                x.Player!,
                minutes.SecondsFor(x.Id),
                minutes.OnPitchNow.Contains(x.Id)))
            .OrderByDescending(r => r.Seconds)
            .ThenBy(r => r.Player.ShirtNumber ?? 99)
            .ThenBy(r => r.Player.DisplayName)];
    }
}
