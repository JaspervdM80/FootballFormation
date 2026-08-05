using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Components;

/// <summary>
/// The title block every page opens with: heading, subtitle, an optional back arrow and an optional
/// action area. It replaces ten hand-rolled copies that had drifted apart on heading size, subtitle
/// colour and spacing.
/// <para>
/// Title and subtitle each come in two flavours. Pass the string when it is plain text; pass the
/// <c>RenderFragment</c> when the page needs its own markup — a coloured shirt number, a count that
/// depends on loaded data, or a class its scoped stylesheet targets. Fragment content is compiled
/// into the <em>calling</em> page, so that page's scoped CSS still reaches inside it. Anything on
/// an element this component renders — the wrapper, the heading — needs a rule in app.css instead.
/// </para>
/// </summary>
public partial class PageHeader
{
    /// <summary>Plain-text heading. Ignored when <see cref="TitleContent"/> is set.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Heading markup, for titles that are more than a string.</summary>
    [Parameter] public RenderFragment? TitleContent { get; set; }

    /// <summary>Plain-text subtitle, rendered in the shared .page-header-subtitle style.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Subtitle markup. Rendered raw — supply your own MudText.</summary>
    [Parameter] public RenderFragment? SubtitleContent { get; set; }

    /// <summary>
    /// Sits on the heading's own line, to its right — the formation builder's venue, date and
    /// formation badges, which belong beside the opponent rather than underneath it.
    /// </summary>
    [Parameter] public RenderFragment? Meta { get; set; }

    /// <summary>Right-aligned block: an Add button, a menu, the squad actions.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>
    /// Set to render a back arrow. The value is where to go when the visitor arrived here directly;
    /// otherwise the arrow follows the trail. Leave null on a top-level page.
    /// </summary>
    [Parameter] public string? BackFallback { get; set; }

    /// <summary>Heading size. Top-level pages use h4, detail pages h5 or h6.</summary>
    [Parameter] public Typo TitleTypo { get; set; } = Typo.h4;

    /// <summary>
    /// A class on the heading element itself. MudText renders that element, so the rule belongs in
    /// app.css — a page's scoped stylesheet cannot reach it.
    /// </summary>
    [Parameter] public string? TitleClass { get; set; }

    /// <summary>Appended to the wrapper — the page's bottom margin, and any app.css hook.</summary>
    [Parameter] public string Class { get; set; } = "mb-4";
}
