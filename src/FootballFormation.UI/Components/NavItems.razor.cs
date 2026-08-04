using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

/// <summary>
/// The main menu, rendered from the one list in <c>AppNav.Menu</c>. Like <see cref="SeasonPicker"/>
/// it appears twice — once in the app bar, once in the mobile drawer — from the same component, so
/// the two renderings cannot drift apart the way the hand-written copies did.
/// </summary>
public partial class NavItems
{
    /// <summary>
    /// The drawer shows an icon beside each label; the app bar hides it (.topbar-nav-link) and
    /// styles the links as compact pills instead.
    /// </summary>
    [Parameter] public bool ShowIcons { get; set; }
}
