namespace FootballFormation.Core.Data;

internal static class PlayerQueries
{
    // By shirt, then name, with the unnumbered last — the order every list of the club's players is read in.
    internal static IQueryable<Player> InSquadOrder(this IQueryable<Player> players) =>
        players
            .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.Surname);

    // A season's squad as every reader takes it: untracked, each member with the player she is.
    internal static IQueryable<SeasonSquadMember> SquadOf(this AppDbContext db, int seasonId) =>
        db.SeasonSquadMembers
            .AsNoTracking()
            .Where(m => m.SeasonId == seasonId)
            .Include(m => m.Player);
}
