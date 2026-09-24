namespace FootballFormation.UI.Components;

/// <see cref="PageHeader"/> renders one of these; reach for it directly only if a page's header is too bespoke for that.
public partial class BackButton
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    /// Where the arrow goes when this tab has no named page behind it — a shared link opened cold, a bookmark — or when the browser has
    /// no Navigation API for back.js to ask.
    [Parameter, EditorRequired] public string Fallback { get; set; } = null!;

    private string Label => L["Back to {0}", L[AppNav.PageNameKey(Fallback) ?? "Start"]];
}
