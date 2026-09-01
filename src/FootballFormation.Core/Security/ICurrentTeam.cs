namespace FootballFormation.Core.Security;

/// The team a request is about — the subject the write guard needs now that admin authority names one. Deliberately apart from
/// <see cref="ICurrentUser"/>: which team is being looked at is a view choice anyone can make, and who may change it is not.
public interface ICurrentTeam
{
    /// Null only while no team exists at all, which is true of a database that has not been seeded yet.
    Task<int?> GetIdAsync();
}
