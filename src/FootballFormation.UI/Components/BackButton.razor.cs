using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FootballFormation.UI.Components;

/// <summary>
/// The back arrow a detail page shows. It returns to the page the visitor actually came from (see
/// <see cref="NavigationTrail"/>) and names that page in its tooltip, so "back" is never a guess.
/// <see cref="PageHeader"/> renders one of these — reach for it directly only if a page's header is
/// too bespoke for PageHeader, which none currently is.
/// </summary>
public partial class BackButton
{
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    /// <summary>
    /// Where to go when there is nothing behind us — a shared link opened cold, a bookmark, or a
    /// refresh. On a normal visit the trail wins and this is never used.
    /// </summary>
    [Parameter, EditorRequired] public string Fallback { get; set; } = null!;

    /// <summary>
    /// Resolved in one place so the tooltip and the destination can never disagree. A page the route
    /// table cannot name — /login, /not-found — is not somewhere we offer to go "back" to.
    /// </summary>
    private string Target =>
        Trail.Previous is { } previous && AppNav.PageNameKey(previous) is not null ? previous : Fallback;

    private string Label => L["Back to {0}", L[AppNav.PageNameKey(Target) ?? "Start"]];

    private void GoBack() => Navigation.NavigateTo(Target);
}
