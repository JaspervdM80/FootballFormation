namespace FootballFormation.Core.Data;

/// Named once because an include chain spelled out per call site drifts silently — nothing fails when one copy loses a level, the page
/// just renders with an empty navigation. Compose one of each shallow/deep pair, never both: EF rejects two includes of one navigation.
internal static class GameQueries
{
    internal static IQueryable<Game> WithPeriods(this IQueryable<Game> games) =>
        games.Include(g => g.Periods);

    internal static IQueryable<Game> WithPeriodLineups(this IQueryable<Game> games) =>
        games
            .Include(g => g.Periods)
                .ThenInclude(p => p.PlayerPositions);

    internal static IQueryable<Game> WithNamedLineups(this IQueryable<Game> games) =>
        games
            .Include(g => g.Periods.OrderBy(p => p.PeriodType))
                .ThenInclude(p => p.PlayerPositions)
                    .ThenInclude(pp => pp.Player);

    internal static IQueryable<Game> WithGoals(this IQueryable<Game> games) =>
        games.Include(g => g.Goals);

    /// Unordered — MatchResult.razor sorts for itself.
    internal static IQueryable<Game> WithGoalsAndScorers(this IQueryable<Game> games) =>
        games
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Scorer)
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Assister);

    /// GameMinutesReport reconstructs playing time from these; without them a live-tracked game reads as if the final line-up had been
    /// on the pitch from kick-off.
    internal static IQueryable<Game> WithSubstitutions(this IQueryable<Game> games) =>
        games.Include(g => g.Substitutions);

    /// Never compose this without <see cref="WithSubstitutions"/>: Game.WasReplaced needs those rows, and with none loaded every injury
    /// looks unreplaced, so GameMinutesReport walks a replaced player off the pitch twice.
    internal static IQueryable<Game> WithInjuries(this IQueryable<Game> games) =>
        games.Include(g => g.Injuries);

    internal static IQueryable<Game> WithSubstitutionPlayers(this IQueryable<Game> games) =>
        games
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOff)
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOn);
}
