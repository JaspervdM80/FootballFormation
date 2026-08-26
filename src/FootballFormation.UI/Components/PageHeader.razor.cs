namespace FootballFormation.UI.Components;

/// <summary>
/// The title block every page opens with.
/// <para>
/// Fragment content is compiled into the <em>calling</em> page, so that page's scoped CSS still
/// reaches inside it. Anything on an element this component renders — the wrapper, the heading —
/// needs a rule in app.css instead.
/// </para>
/// </summary>
public partial class PageHeader
{
    /// <summary>Ignored when <see cref="TitleContent"/> is set.</summary>
    [Parameter] public string? Title { get; set; }

    [Parameter] public RenderFragment? TitleContent { get; set; }

    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Rendered raw — supply your own MudText.</summary>
    [Parameter] public RenderFragment? SubtitleContent { get; set; }

    /// <summary>Sits on the heading's own line, to its right.</summary>
    [Parameter] public RenderFragment? Meta { get; set; }

    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>
    /// Set to render a back arrow. The value is where to go when the visitor arrived here directly;
    /// otherwise the arrow follows the trail. Leave null on a top-level page.
    /// </summary>
    [Parameter] public string? BackFallback { get; set; }

    /// <summary>Top-level pages use h4, detail pages h5 or h6.</summary>
    [Parameter] public Typo TitleTypo { get; set; } = Typo.h4;

    /// <summary>
    /// A class on the heading element itself. MudText renders that element, so the rule belongs in
    /// app.css — a page's scoped stylesheet cannot reach it.
    /// </summary>
    [Parameter] public string? TitleClass { get; set; }

    [Parameter] public string Class { get; set; } = "mb-4";
}
