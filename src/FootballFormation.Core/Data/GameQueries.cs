using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Data;

/// <summary>
/// The shapes a <see cref="Game"/> is loaded in, each named once and composed at the call site.
/// <para>
/// A game is read in five places across three services, and two of those want the same six-level
/// include chain. Spelled out at each call site that chain drifts silently: nothing fails when one
/// copy gains a level, the page just renders with a navigation quietly unpopulated. Named here, a
/// change to a shape is a change everywhere it is used.
/// </para>
/// <para>
/// These stay <see cref="IQueryable{T}"/> on purpose — deliberately not a repository. Tracking,
/// filtering, tagging and ordering all remain the caller's, because each of those is a decision
/// somebody made for a documented reason: <c>LiveMatchService.GetLiveAsync</c> needs
/// <c>AsNoTrackingWithIdentityResolution</c> for the Blazor circuit, <c>GetTodaysMatchAsync</c>
/// needs its <see cref="QueryTags.ComparesDatesInSql"/> opt-out. Only the include chain is shared.
/// </para>
/// <para>
/// The three pairs below run shallow-then-deep over the same navigation. Compose one of each pair,
/// never both: EF rejects a filtered and an unfiltered include of one navigation in a single query.
/// </para>
/// </summary>
internal static class GameQueries
{
    /// <summary>The periods alone — how the match is split, with nobody placed in them yet.</summary>
    internal static IQueryable<Game> WithPeriods(this IQueryable<Game> games) =>
        games.Include(g => g.Periods);

    /// <summary>
    /// The periods with the slots filled in, players by ID. Enough to reconstruct who was on the
    /// pitch; not enough to name them.
    /// </summary>
    internal static IQueryable<Game> WithPeriodLineups(this IQueryable<Game> games) =>
        games
            .Include(g => g.Periods)
                .ThenInclude(p => p.PlayerPositions);

    /// <summary>
    /// The periods in playing order, with each slot's player loaded — everything a lineup needs to
    /// be drawn on the pitch.
    /// </summary>
    internal static IQueryable<Game> WithNamedLineups(this IQueryable<Game> games) =>
        games
            .Include(g => g.Periods.OrderBy(p => p.PeriodType))
                .ThenInclude(p => p.PlayerPositions)
                    .ThenInclude(pp => pp.Player);

    /// <summary>The goals alone, unordered. Enough to count a scoreline.</summary>
    internal static IQueryable<Game> WithGoals(this IQueryable<Game> games) =>
        games.Include(g => g.Goals);

    /// <summary>
    /// The goals with both players named. Order is the caller's — <c>MatchResult.razor</c> sorts
    /// for itself.
    /// </summary>
    internal static IQueryable<Game> WithGoalsAndScorers(this IQueryable<Game> games) =>
        // Goals are included twice because EF needs a fresh Include to hang a second ThenInclude
        // off the same navigation. Both must be spelled identically — a filtered and an unfiltered
        // include of one collection is ambiguous.
        games
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Scorer)
            .Include(g => g.Goals)
                .ThenInclude(gl => gl.Assister);

    /// <summary>
    /// The substitutions alone, players by ID. Statistics reconstruct playing time from these (see
    /// <c>GameMinutesReport</c>); without them a live-tracked game reads as if the final lineup had
    /// been on the pitch from kick-off.
    /// </summary>
    internal static IQueryable<Game> WithSubstitutions(this IQueryable<Game> games) =>
        games.Include(g => g.Substitutions);

    /// <summary>The substitutions with both players named, for a touchline log that reads.</summary>
    internal static IQueryable<Game> WithSubstitutionPlayers(this IQueryable<Game> games) =>
        // Included twice for the same reason as WithGoalsAndScorers.
        games
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOff)
            .Include(g => g.Substitutions)
                .ThenInclude(s => s.PlayerOn);
}
