namespace FootballFormation.Core.Reporting;

/// The order the live dialogs offer players in, so the likeliest pick is the first button a thumb reaches.
public static class LiveCandidateOrder
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    /// Whoever is on the pitch first, since a scorer nearly always still is, then the most goals and then the name. <paramref name="seasonGoals"/>
    /// counts complete matches only, so the goals already scored in <paramref name="game"/> are added on top.
    public static List<Player> Scorers(
        IEnumerable<Player> candidates, IReadOnlySet<int> onPitch, IReadOnlyDictionary<int, int> seasonGoals, Game game) =>
        [.. candidates
            .OrderByDescending(p => onPitch.Contains(p.Id))
            .ThenByDescending(p => seasonGoals.GetValueOrDefault(p.Id) + game.Goals.Count(g => g.ScorerId == p.Id && !g.IsOwnGoal))
            .ThenBy(p => p.DisplayName, Names)];

    /// Best fit for the position being vacated first, then the name.
    public static List<Player> ForPosition(IEnumerable<Player> bench, PlayerPosition position) =>
        [.. bench
            .OrderBy(p => PositionFitHelper.GetFit(p, position))
            .ThenBy(p => p.DisplayName, Names)];
}
