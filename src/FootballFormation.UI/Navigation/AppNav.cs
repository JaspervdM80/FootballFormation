using FootballFormation.Core.Security;
using Microsoft.AspNetCore.Components.Routing;

namespace FootballFormation.UI.Navigation;

/// The drawer keeps <c>Administration</c> in a group of its own at its foot, under a rule. The app bar renders the menu in one row.
public enum NavGroup
{
    Primary,
    Administration,
}

/// <paramref name="Icon"/> shows in the drawer only — the app bar hides it via .topbar-nav-link.
/// <paramref name="RequiresRole"/> is null for the entries everyone sees.
public sealed record NavItem(string Path, string LabelKey, string Icon, NavLinkMatch Match, string? RequiresRole = null, NavGroup Group = NavGroup.Primary);

/// The menu, the "Back to …" labels and the season picker's visibility all read from here, so the three cannot drift apart.
public static class AppNav
{
    /// Home is deliberately absent: the club-and-team title is already a home link in both the app bar and the drawer.
    public static readonly IReadOnlyList<NavItem> Menu =
    [
        new(AppRoutes.Players, PageNameKey(AppRoutes.Players)!, Icons.Material.Filled.Groups, NavLinkMatch.Prefix),
        new(AppRoutes.Games, PageNameKey(AppRoutes.Games)!, Icons.Material.Filled.SportsSoccer, NavLinkMatch.Prefix),
        new(AppRoutes.Trainings, PageNameKey(AppRoutes.Trainings)!, Icons.Material.Filled.FitnessCenter, NavLinkMatch.Prefix, RequiresRole: AppRoles.Admin),
        new(AppRoutes.SeasonStats, PageNameKey(AppRoutes.SeasonStats)!, Icons.Material.Filled.BarChart, NavLinkMatch.Prefix),
        new(AppRoutes.Preferences, PageNameKey(AppRoutes.Preferences)!, Icons.Material.Filled.Tune, NavLinkMatch.All, RequiresRole: AppRoles.Admin),
        new(AppRoutes.Settings, PageNameKey(AppRoutes.Settings)!, Icons.Material.Filled.Settings, NavLinkMatch.All, Group: NavGroup.Administration),
        new(AppRoutes.Users, PageNameKey(AppRoutes.Users)!, Icons.Material.Filled.ManageAccounts, NavLinkMatch.All, RequiresRole: AppRoles.Admin, Group: NavGroup.Administration),
        new(AppRoutes.Teams, PageNameKey(AppRoutes.Teams)!, Icons.Material.Filled.Shield, NavLinkMatch.All, RequiresRole: AppRoles.ApplicationAdmin, Group: NavGroup.Administration),
    ];

    /// Names the menu entries and fills in "Back to {0}", so a page is called the same thing wherever it is referred to. Null outside the
    /// app's own routes (/login, /not-found, /Error), which is how back.js knows to step past them.
    public static string? PageNameKey(string? path) => Segments(path) switch
    {
        [] => "Start",
        ["players"] => "Squad",
        ["players", _, "stats"] => "Player Stats",
        ["games"] => "Games",
        ["games", "duties"] => "Duties",
        ["games", _, "formation"] => "Formation Builder",
        ["games", _, "overview"] => "Formation Overview",
        ["games", _, "live"] => "Live Match",
        ["games", _, "result"] => "Match Result",
        ["trainings"] => "Trainings",
        ["stats"] => "Season",
        ["stats", "positions"] => "Position Development",
        ["preferences"] => "Preferences",
        ["settings"] => "Settings",
        ["users"] => "Users",
        ["teams"] => "Teams",
        ["styleguide"] => "Style Guide",
        _ => null,
    };

    /// Hidden on the single-game routes, and on /settings where it would be confusing while the admin edits the season list itself. The
    /// start page is the exception: nothing there is filtered, but it is where a visit begins.
    public static bool IsSeasonAware(string path) => Segments(path) switch
    {
        [] or ["players"] or ["games"] or ["trainings"] or ["stats"] => true,
        ["players", _, "stats"] or ["stats", "positions"] or ["games", "duties"] => true,
        _ => false,
    };

    /// A route naming one game or player belongs to the team being left, so switching from it starts over at home. The query is dropped
    /// for the same reason: a filter can name a player too. So does the choice page, which would only ask again.
    public static string AfterTeamChange(string path)
    {
        var segments = Segments(path);
        var target = "/" + string.Join('/', segments);

        return segments.Any(s => s.All(char.IsAsciiDigit)) || target.Equals(AppRoutes.ChooseTeam, StringComparison.OrdinalIgnoreCase)
            ? AppRoutes.Home
            : target;
    }

    private static string[] Segments(string? path) => (path ?? "").Split('?')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
