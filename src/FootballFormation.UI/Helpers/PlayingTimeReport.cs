using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

public enum PeriodPlayStatus
{
    NotPlaying,
    Starting,
    Substitute
}

/// <summary>What one player did in one period.</summary>
public class PeriodDetail
{
    public PeriodPlayStatus Status { get; set; }
    public PlayerPosition? Position { get; set; }
    public PositionFit? Fit { get; set; }
}

/// <summary>One player's involvement across every period of a game.</summary>
public class PlayingTimeRow
{
    public required Player Player { get; init; }
    public required Dictionary<int, PeriodDetail> PeriodDetails { get; init; }
    public int TotalMinutes { get; init; }
    public double Percentage { get; init; }

    public int PlayerId => Player.Id;
    public string PlayerName => Player.DisplayName;
    public int? ShirtNumber => Player.ShirtNumber;
}

/// <summary>
/// Turns the per-period lineups into the playing-time table, so the builder page only
/// has to render it. Pure computation — no state, no service calls.
/// </summary>
public static class PlayingTimeReport
{
    public static List<PlayingTimeRow> Build(
        Game game,
        IEnumerable<Player> roster,
        IReadOnlyDictionary<int, List<GamePlayerPosition>> periodLineups)
    {
        var orderedPeriods = game.Periods.OrderBy(p => p.PeriodType).ToList();

        return roster
            .Select(player => BuildRow(game, player, orderedPeriods, periodLineups))
            .OrderByDescending(r => r.TotalMinutes)
            .ThenBy(r => r.ShirtNumber ?? 99)
            .ThenBy(r => r.PlayerName)
            .ToList();
    }

    private static PlayingTimeRow BuildRow(
        Game game,
        Player player,
        List<GamePeriod> orderedPeriods,
        IReadOnlyDictionary<int, List<GamePlayerPosition>> periodLineups)
    {
        var details = new Dictionary<int, PeriodDetail>();
        var periodsPlaying = 0;

        foreach (var period in orderedPeriods)
        {
            var lineup = periodLineups.GetValueOrDefault(period.Id, []);
            var entry = lineup.FirstOrDefault(p => p.PlayerId == player.Id);

            details[period.Id] = Describe(player, entry);

            if (entry is { IsSubstitute: false }) periodsPlaying++;
        }

        var totalMinutes = periodsPlaying * game.PeriodDurationMinutes;

        // Against playable minutes, not GameDurationMinutes: with odd durations the
        // integer period split drops a minute (45 in halves → 2×22), and playing every
        // period should still read 100%.
        var playableMinutes = orderedPeriods.Count * game.PeriodDurationMinutes;

        return new PlayingTimeRow
        {
            Player = player,
            PeriodDetails = details,
            TotalMinutes = totalMinutes,
            Percentage = playableMinutes > 0
                ? Math.Round((double)totalMinutes / playableMinutes * 100, 0)
                : 0
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
