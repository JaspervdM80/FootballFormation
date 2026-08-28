namespace FootballFormation.UI.Components;

/// Returns to the page the visitor actually came from (see <see cref="NavigationTrail"/>) and names it in the tooltip, so "back" is never
/// a guess. <see cref="PageHeader"/> renders one of these; reach for it directly only if a page's header is too bespoke for that.
public partial class BackButton
{
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    /// Only used when there is nothing behind us — a shared link opened cold, a bookmark, a refresh. On a normal visit the trail wins.
    [Parameter, EditorRequired] public string Fallback { get; set; } = null!;

    /// Resolved in one place so the tooltip and the destination cannot disagree.
    private string Target => Trail.Previous ?? Fallback;

    private string Label => L["Back to {0}", L[AppNav.PageNameKey(Target) ?? "Start"]];
}
