using FootballFormation.UI.State;

namespace FootballFormation.UI.Navigation;

/// <summary>
/// Every route in the app, in one place. Pages build URLs from here rather than interpolating a
/// string at the call site, so renaming a route is one edit instead of hunting two dozen literals.
/// The <c>@page</c> directives stay literal — Razor needs a compile-time constant — so keep the two in step.
/// </summary>
public static class AppRoutes
{
    public const string Home = "/";
    public const string Players = "/players";
    public const string Games = "/games";
    public const string SeasonStats = "/stats";
    public const string PositionDevelopment = "/stats/positions";
    public const string Settings = "/settings";
    public const string Users = "/users";
    public const string Login = "/login";

    public static string PlayerStats(int playerId) => $"/players/{playerId}/stats";

    public static string Formation(int gameId) => $"/games/{gameId}/formation";

    public static string Overview(int gameId) => $"/games/{gameId}/overview";

    public static string Live(int gameId) => $"/games/{gameId}/live";

    public static string Result(int gameId) => $"/games/{gameId}/result";

    /// <summary>
    /// The two chrome controls that are links to an endpoint rather than pages of their own. Both
    /// write a cookie and send the visitor back where they were, because both settings are fixed
    /// for the lifetime of a render: the circuit's culture is set at startup, and the season is
    /// read off the request.
    /// </summary>
    public static string SetCulture(string culture, string returnUrl) => $"/culture/set?culture={culture}&redirectUri={Uri.EscapeDataString(returnUrl)}";

    public static string SetSeason(int? seasonId, string returnUrl) => $"/season/set?season={SeasonPreference.Format(seasonId)}&redirectUri={Uri.EscapeDataString(returnUrl)}";
}
