namespace FootballFormation.Core.Reporting;

public record LiveMinutesRow(Player Player, int Seconds, bool IsOnPitch)
{
    public int Minutes => Seconds / 60;
}

/// <summary>
/// The live match screen's view of playing time: exact seconds on the pitch, ordered for the
/// bench-management table. The calculation itself lives in <see cref="GameMinutesReport"/>, which
/// the season and player statistics read too, so the live screen and the stats pages can never
/// disagree about how long someone played.
/// </summary>
public static class LiveMinutesReport
{
    /// <param name="elapsedSeconds">The match clock right now, which closes off the running half.</param>
    /// <param name="findPlayer">Resolves an id to a player; rows for unknown ids are dropped.</param>
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
