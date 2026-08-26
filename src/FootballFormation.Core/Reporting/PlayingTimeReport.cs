namespace FootballFormation.Core.Reporting;

public enum PeriodPlayStatus
{
    NotPlaying,
    Starting,
    Substitute
}

public class PeriodDetail
{
    public PeriodPlayStatus Status { get; set; }
    public PlayerPosition? Position { get; set; }
    public PositionFit? Fit { get; set; }
}

public class PlayingTimeRow
{
    public required Player Player { get; init; }
    public required Dictionary<int, PeriodDetail> PeriodDetails { get; init; }
    public int TotalMinutes { get; init; }
    public double Percentage { get; init; }

    /// False when the minutes are the planned estimate — see <see cref="GameMinutesReport"/>.
    public bool IsActual { get; init; }

    public int PlayerId => Player.Id;
    public string PlayerName => Player.DisplayName;
    public int? ShirtNumber => Player.ShirtNumber;
}

/// The totals follow <see cref="GameMinutesReport"/>, so a game run live is measured on what happened rather than what was planned. The
/// per-period cells are a different question and always come from the line-ups being edited, so a change shows up before a save.
public static class PlayingTimeReport
{
    public static List<PlayingTimeRow> Build(
        Game game,
        IEnumerable<Player> roster,
        IReadOnlyDictionary<int, List<GamePlayerPosition>> periodLineups)
    {
        var orderedPeriods = game.Periods.OrderBy(p => p.PeriodType).ToList();

        var actual = game.HasActualTimings ? GameMinutesReport.Build(game) : null;

        // Against playable time, not GameDurationMinutes: a game whose periods are not all written up has less than a full match to
        // share out, and a half whistled off early must not cap everyone who played it at 80%.
        var playableSeconds = actual is not null
            ? game.PlayedDurationSeconds
            : orderedPeriods.Count * game.PeriodDurationSeconds;

        return roster
            .Select(player => BuildRow(game, player, orderedPeriods, periodLineups, actual, playableSeconds))
            .OrderByDescending(r => r.TotalMinutes)
            .ThenBy(r => r.ShirtNumber ?? 99)
            .ThenBy(r => r.PlayerName)
            .ToList();
    }

    private static PlayingTimeRow BuildRow(
        Game game,
        Player player,
        List<GamePeriod> orderedPeriods,
        IReadOnlyDictionary<int, List<GamePlayerPosition>> periodLineups,
        GameMinutes? actual,
        int playableSeconds)
    {
        var details = new Dictionary<int, PeriodDetail>();
        var plannedSeconds = 0;

        foreach (var period in orderedPeriods)
        {
            var lineup = periodLineups.GetValueOrDefault(period.Id, []);
            var entry = lineup.FirstOrDefault(p => p.PlayerId == player.Id);

            details[period.Id] = Describe(player, entry);

            if (entry is { IsSubstitute: false }) plannedSeconds += game.PeriodDurationSeconds;
        }

        var seconds = actual?.SecondsFor(player.Id) ?? plannedSeconds;

        return new PlayingTimeRow
        {
            Player = player,
            PeriodDetails = details,
            TotalMinutes = Game.SecondsToMinutes(seconds),
            Percentage = playableSeconds > 0
                ? Math.Round((double)seconds / playableSeconds * 100, 0)
                : 0,
            IsActual = actual is not null
        };
    }

    private static PeriodDetail Describe(Player player, GamePlayerPosition? entry)
    {
        if (entry is null)
            return new PeriodDetail { Status = PeriodPlayStatus.NotPlaying };

        return new PeriodDetail
        {
            Status = entry.IsSubstitute ? PeriodPlayStatus.Substitute : PeriodPlayStatus.Starting,
            Position = entry.Position,
            Fit = PositionFitHelper.GetFit(player, entry.Position)
        };
    }
}
