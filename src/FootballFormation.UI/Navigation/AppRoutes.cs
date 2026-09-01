using FootballFormation.UI.State;

namespace FootballFormation.UI.Navigation;

/// Pages build URLs from here rather than interpolating at the call site, so renaming a route is one edit. The <c>@page</c> directives
/// stay literal because Razor needs a compile-time constant, so keep the two in step.
public static class AppRoutes
{
    public const string Home = "/";
    public const string Players = "/players";
    public const string Games = "/games";
    public const string Trainings = "/trainings";
    public const string SeasonStats = "/stats";
    public const string PositionDevelopment = "/stats/positions";
    public const string Settings = "/settings";
    public const string Users = "/users";
    public const string Teams = "/teams";
    public const string Login = "/login";

    public static string PlayerStats(int playerId) => $"/players/{playerId}/stats";

    public static string Formation(int gameId) => $"/games/{gameId}/formation";

    public static string Overview(int gameId) => $"/games/{gameId}/overview";

    public static string Live(int gameId) => $"/games/{gameId}/live";

    public static string Result(int gameId) => $"/games/{gameId}/result";

    /// Endpoints, not pages: both settings are fixed for the lifetime of a render — the circuit's culture at startup, the season off the
    /// request — so each writes a cookie and sends the visitor back where they were.
    public static string SetCulture(string culture, string returnUrl) => $"/culture/set?culture={culture}&redirectUri={Uri.EscapeDataString(returnUrl)}";

    public static string SetSeason(int? seasonId, string returnUrl) => $"/season/set?season={SeasonPreference.Format(seasonId)}&redirectUri={Uri.EscapeDataString(returnUrl)}";

    /// <inheritdoc cref="SetSeason"/>
    public static string SetTeam(int teamId, string returnUrl) => $"/team/set?team={TeamPreference.Format(teamId)}&redirectUri={Uri.EscapeDataString(returnUrl)}";
}
