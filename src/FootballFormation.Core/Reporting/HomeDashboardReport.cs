namespace FootballFormation.Core.Reporting;

public record ScorerLine(Player Scorer, int Goals);

public record HomeDashboard(Game? NextGame, Game? LastGame, List<ScorerLine> LastScorers, SeasonStats Record)
{
    public static HomeDashboard Empty { get; } = new(null, null, [], SeasonStats.Empty);
}

/// No minutes anywhere: the home page is public, and playing time is not.
public static class HomeDashboardReport
{
    /// Scorer names are only there if the games were loaded with their scorers.
    public static HomeDashboard Build(IEnumerable<Game> games, DateTime today)
    {
        var ordered = games.OldestFirst();

        // From tomorrow on: a fixture today already has the match-day banner, and one card for it is enough.
        var next = ordered.FirstOrDefault(g =>
            g.Date.Date > today.Date && g.MatchState == MatchState.NotStarted && !g.HasFinalScore);

        // NewestFirst rather than the last of the list, so a double-header agrees with the results list and the form guide.
        var last = ordered.NewestFirst().FirstOrDefault(g => g.HasFinalScore);

        return new HomeDashboard(
            next,
            last,
            last is null ? [] : ScorersOf(last),
            SeasonStatsReport.Build([], ordered, SeasonSquads.Empty));
    }

    private static List<ScorerLine> ScorersOf(Game game) =>
        game.Goals
            .Where(g => g.CountsForUs && g.Scorer is not null)
            .GroupBy(g => g.ScorerId)
            .Select(group => new ScorerLine(group.First().Scorer!, group.Count()))
            .OrderByDescending(line => line.Goals)
            .ThenBy(line => line.Scorer.DisplayName, StringComparer.CurrentCulture)
            .ToList();
}
