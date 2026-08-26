namespace FootballFormation.UI.Components;

/// Rendered from the one list in <c>AppNav.Menu</c>, and from one component in both the app bar and the drawer, so neither can drift.
public partial class NavItems
{
    /// The drawer shows an icon beside each label; the app bar hides it via .topbar-nav-link and styles the links as compact pills.
    [Parameter] public bool ShowIcons { get; set; }
}
