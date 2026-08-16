using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace FootballFormation.UI.Navigation;

/// <summary>An entry in the main menu, rendered identically by the app bar and the drawer.</summary>
/// <param name="Path">Where it goes — an <see cref="AppRoutes"/> constant.</param>
/// <param name="LabelKey">Localization key.</param>
/// <param name="Icon">Shown in the drawer only; the app bar hides it via .topbar-nav-link.</param>
/// <param name="Match">How the active-page highlight matches the current URL.</param>
/// <param name="AdminOnly">Wrapped in an AuthorizeView restricted to the Admin role when true.</param>
public sealed record NavItem(
    string Path,
    string LabelKey,
    string Icon,
    NavLinkMatch Match,
    bool AdminOnly = false);

/// <summary>
/// What a route is called and where it belongs in the chrome. The menu, the "Back to …" labels and
/// the season picker's visibility all read from here, so the three cannot drift apart.
/// </summary>
public static class AppNav
{
    /// <summary>
    /// The main menu, in order. Home is deliberately absent: the "GJS Meiden" title is already a
    /// home link in both the app bar and the drawer, so a Start item only says it a second time.
    /// </summary>
    public static readonly IReadOnlyList<NavItem> Menu =
    [
        new(AppRoutes.Players, PageNameKey(AppRoutes.Players)!, Icons.Material.Filled.Groups, NavLinkMatch.Prefix),
        new(AppRoutes.Games, PageNameKey(AppRoutes.Games)!, Icons.Material.Filled.SportsSoccer, NavLinkMatch.Prefix),
        new(AppRoutes.SeasonStats, PageNameKey(AppRoutes.SeasonStats)!, Icons.Material.Filled.BarChart, NavLinkMatch.Prefix),
        new(AppRoutes.Settings, PageNameKey(AppRoutes.Settings)!, Icons.Material.Filled.Settings, NavLinkMatch.All, AdminOnly: true),
        new(AppRoutes.Users, PageNameKey(AppRoutes.Users)!, Icons.Material.Filled.ManageAccounts, NavLinkMatch.All, AdminOnly: true),
    ];

    /// <summary>
    /// What to call the page at <paramref name="path"/>, as a localization key — this is what names
    /// the menu entries and fills in the "Back to {0}" label, so a page is called the same thing
    /// wherever it is referred to.
    /// <para>
    /// Null for anything outside the app's own routes (/login, /not-found, /Error). That is how the
    /// back arrow knows to take its fallback rather than offer to return to a page it cannot name.
    /// </para>
    /// </summary>
    public static string? PageNameKey(string? path) => Segments(path) switch
    {
        [] => "Start",
        ["players"] => "Squad",
        ["players", _, "stats"] => "Player Stats",
        ["games"] => "Games",
        ["games", _, "formation"] => "Formation Builder",
        ["games", _, "overview"] => "Formation Overview",
        ["games", _, "live"] => "Live Match",
        ["games", _, "result"] => "Match Result",
        ["stats"] => "Season",
        ["settings"] => "Preferences",
        ["users"] => "Users",
        _ => null,
    };

    /// <summary>
    /// Where a season filter actually changes what is on screen: the games list, the squad and the
    /// two stats pages. Hidden on the single-game routes, and on /settings it would be actively
    /// confusing while the admin edits the season list itself. The start page is the exception —
    /// nothing there is filtered, but it is where a visit begins, so the season can be set before
    /// navigating anywhere.
    /// </summary>
    public static bool IsSeasonAware(string path) => Segments(path) switch
    {
        [] or ["players"] or ["games"] or ["stats"] => true,
        ["players", _, "stats"] => true,
        _ => false,
    };

    private static string[] Segments(string? path) =>
        (path ?? "").Split('?')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
