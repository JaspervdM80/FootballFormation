using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Data;

/// <summary>
/// The include chains a <see cref="Game"/> is loaded with, named once and composed at the call
/// site. Spelled out per call site they drift silently — nothing fails when one copy loses a level,
/// the page just renders with an empty navigation.
/// <para>
/// Not a repository: they stay <see cref="IQueryable{T}"/>, so tracking, filtering, ordering and
/// tagging stay the caller's. Each pair below runs shallow then deep over one navigation — compose
/// one of a pair, never both, because EF rejects a filtered and an unfiltered include of the same
/// navigation in one query. The deep ones include their collection twice for the same reason: a
/// second <c>ThenInclude</c> off one navigation needs a fresh <c>Include</c>, spelled identically.
/// </para>
/// </summary>
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

    /// <summary>Unordered — <c>MatchResult.razor</c> sorts for itself.</summary>
    internal static IQueryable<Game> WithGoalsAndScorers(this IQueryable<Game> games) =>
        games
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Scorer)
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Assister);

    /// <summary>
    /// Statistics reconstruct playing time from these (<c>GameMinutesReport</c>); without them a
    /// live-tracked game reads as if the final lineup had been on the pitch from kick-off.
    /// </summary>
    internal static IQueryable<Game> WithSubstitutions(this IQueryable<Game> games) =>
        games.Include(g => g.Substitutions);

    /// <summary>
    /// An injury with nobody coming on for it is a line-up change no substitution row records, and
    /// it is what stops a hurt player's available minutes where she stopped playing
    /// (<c>PlayerStatsReport</c>).
    /// <para>
    /// **Never composed without the substitutions.** <c>Game.WasReplaced</c> reads the substitution
    /// rows to tell an injury somebody came on for from one nobody did; with none loaded every
    /// injury looks unreplaced, and <c>GameMinutesReport</c> would walk a replaced player off the
    /// pitch twice — crediting her replacement the whole half on top of it.
    /// </para>
    /// </summary>
    internal static IQueryable<Game> WithInjuries(this IQueryable<Game> games) =>
        games.Include(g => g.Injuries);

    internal static IQueryable<Game> WithInjuredPlayers(this IQueryable<Game> games) =>
        games
            .Include(g => g.Injuries)
                .ThenInclude(i => i.Player);

    internal static IQueryable<Game> WithSubstitutionPlayers(this IQueryable<Game> games) =>
        games
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOff)
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOn);
}
