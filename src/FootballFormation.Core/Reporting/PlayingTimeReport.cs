using FootballFormation.Core.Models;

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

    /// <summary>False when the minutes are the planned <c>periods × period length</c> estimate
    /// because the game was never run live. See <see cref="GameMinutesReport"/>.</summary>
    public bool IsActual { get; init; }

    public int PlayerId => Player.Id;
    public string PlayerName => Player.DisplayName;
    public int? ShirtNumber => Player.ShirtNumber;
}

/// <summary>
/// Turns the per-period lineups into the playing-time table, so the builder page only
/// has to render it. Pure computation — no state, no service calls.
/// <para>
/// Minutes come from <see cref="GameMinutesReport"/> once a game has been run live: the match
/// clock and the substitutions say what really happened, and the lineup — which
/// <c>MatchSubstitutionService</c> rewrites in place — no longer does. A game that was never run
/// live has no timings to read, so the plan is the only answer available and the estimate stands.
/// The choice is per game, and every row of one table shares it.
/// </para>
/// <para>
/// The per-period cells are a different question and always come from the lineups this page is
/// editing, so a change made here shows up immediately rather than waiting for a save.
/// </para>
/// </summary>
public static class PlayingTimeReport
{
    public static List<PlayingTimeRow> Build(
        Game game,
        IEnumerable<Player> roster,
        IReadOnlyDictionary<int, List<GamePlayerPosition>> periodLineups)
    {
        var orderedPeriods = game.Periods.OrderBy(p => p.PeriodType).ToList();

        var actual = game.HasActualTimings ? GameMinutesReport.Build(game) : null;

        // Against playable time, not GameDurationMinutes: a game whose periods are not all written
        // up yet has less than a full match to share out, and playing every period there should
        // still read 100%. A tracked game is measured against the time it really ran, for the same
        // reason — a half whistled off early must not cap everyone who played it at 80%.
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
            TotalMinutes = GameMinutesReport.ToMinutes(seconds),
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
