namespace FootballFormation.UI.Components;

/// Fragment content compiles into the calling page, so that page's scoped CSS reaches inside it — but anything on an element this
/// component renders needs a rule in app.css instead.
public partial class PageHeader
{
    /// Ignored when <see cref="TitleContent"/> is set.
    [Parameter] public string? Title { get; set; }

    [Parameter] public RenderFragment? TitleContent { get; set; }

    [Parameter] public string? Subtitle { get; set; }

    /// Rendered raw — supply your own MudText.
    [Parameter] public RenderFragment? SubtitleContent { get; set; }

    /// Sits on the heading's own line, to its right.
    [Parameter] public RenderFragment? Meta { get; set; }

    [Parameter] public RenderFragment? Actions { get; set; }

    /// The fallback for a visitor who arrived directly; otherwise the arrow follows the trail. Null renders no arrow at all.
    [Parameter] public string? BackFallback { get; set; }

    /// Top-level pages use h4, detail pages h5 or h6.
    [Parameter] public Typo TitleTypo { get; set; } = Typo.h4;

    /// MudText renders the heading element, so the rule belongs in app.css — a page's scoped stylesheet cannot reach it.
    [Parameter] public string? TitleClass { get; set; }

    [Parameter] public string Class { get; set; } = "mb-4";
}
