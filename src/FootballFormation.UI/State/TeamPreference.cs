namespace FootballFormation.UI.State;

/// A cookie for the same reason the season is one — every deploy drops every open circuit — but a long one: which team you follow is not
/// a match-day choice, and the point of remembering it is that the next visit opens where the last one left off.
public static class TeamPreference
{
    public const string CookieName = "ff.team";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    /// An absent cookie and a hand-edited one are the same thing here: CurrentTeam falls back to the first team either way.
    public static int? Parse(string? cookieValue) =>
        int.TryParse(cookieValue, out var id) && id > 0 ? id : null;

    public static string Format(int teamId) => teamId.ToString();
}
