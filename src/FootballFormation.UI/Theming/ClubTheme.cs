using MudBlazor;

namespace FootballFormation.UI.Theming;

/// <summary>
/// A club's whole visual identity, in one place.
/// <para>
/// The app renders through two separate styling systems — CSS custom properties for everything
/// hand-written, and a <see cref="MudTheme"/> for MudBlazor's own components — and they used to
/// hold the same hex codes twice, in <c>theme.css</c> and in <c>MainLayout.razor.cs</c>, with a
/// comment asking whoever edited one to remember the other. Both are now derived from this record,
/// so re-skinning for another club means editing <see cref="Gjs"/> and nothing else.
/// </para>
/// <para>
/// Shades are derived where they are used via <c>color-mix(in srgb, var(--token) N%, transparent)</c>
/// rather than stored per alpha level. Text derives from <see cref="Ink"/> the same way.
/// </para>
/// </summary>
public sealed record ClubTheme
{
    // ---- Brand -------------------------------------------------------------------------------

    /// <summary>Crest red.</summary>
    public required string Primary { get; init; }

    /// <summary>Emphasis red, for text on light surfaces.</summary>
    public required string PrimaryBright { get; init; }

    public required string PrimaryDeep { get; init; }

    public required string OnPrimary { get; init; }

    /// <summary>Crest banner green.</summary>
    public required string Accent { get; init; }

    /// <summary>Emphasis green, for text on light surfaces.</summary>
    public required string AccentBright { get; init; }

    public required string AccentDeep { get; init; }

    // ---- Identity ----------------------------------------------------------------------------

    public required string LogoUrl { get; init; }

    /// <summary>The chip behind the crest in the app bar.</summary>
    public required string LogoBackground { get; init; }

    // ---- Surfaces ----------------------------------------------------------------------------

    public required string SurfacePage { get; init; }
    public required string SurfaceCard { get; init; }

    public required string SurfaceCardAlt { get; init; }

    public required string SurfaceAppbar { get; init; }

    public required string SurfaceAppbarAlt { get; init; }

    /// <summary>Near-black with a green cast. Every text and line shade derives from it.</summary>
    public required string Ink { get; init; }

    public required string CornerRadius { get; init; }

    /// <summary>
    /// The GJS Gorinchem light theme, taken from the club crest: red shield, green banner, on a
    /// white page with light-green sections.
    /// </summary>
    public static readonly ClubTheme Gjs = new()
    {
        Primary = "#e11d24",
        PrimaryBright = "#c8151c",
        PrimaryDeep = "#a3141a",
        OnPrimary = "#ffffff",
        Accent = "#0a8f3d",
        AccentBright = "#0c7a37",
        AccentDeep = "#076b2e",

        LogoUrl = "url('icons/icon-192.png')",
        LogoBackground = "#ffffff",

        SurfacePage = "#ffffff",
        SurfaceCard = "#eef7f1",
        SurfaceCardAlt = "#e0efe6",
        SurfaceAppbar = "#ffffff",
        SurfaceAppbarAlt = "#e9f4ed",

        Ink = "#182b1f",
        CornerRadius = "12px"
    };

    public static ClubTheme Current { get; } = Gjs;

    /// <summary>
    /// The brand as CSS custom properties, for the hand-written stylesheets. Emitted into the
    /// document head so the tokens exist before any stylesheet that reads them.
    /// </summary>
    public string ToCssVariables() =>
        $$"""
        :root {
            --club-primary: {{Primary}};
            --club-primary-bright: {{PrimaryBright}};
            --club-primary-deep: {{PrimaryDeep}};
            --club-on-primary: {{OnPrimary}};
            --club-accent: {{Accent}};
            --club-accent-bright: {{AccentBright}};
            --club-accent-deep: {{AccentDeep}};

            --club-logo: {{LogoUrl}};
            --club-logo-bg: {{LogoBackground}};

            --surface-page: {{SurfacePage}};
            --surface-card: {{SurfaceCard}};
            --surface-card-alt: {{SurfaceCardAlt}};
            --surface-appbar: {{SurfaceAppbar}};
            --surface-appbar-alt: {{SurfaceAppbarAlt}};

            --ink: {{Ink}};
            --corner-radius: {{CornerRadius}};
        }
        """;

    /// <summary>
    /// The same brand as a MudBlazor palette. The shades MudBlazor wants as literal colors are
    /// mixed from <see cref="Ink"/> here, since its palette does not take <c>color-mix</c>.
    /// </summary>
    public MudTheme ToMudTheme() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Primary,
            PrimaryContrastText = OnPrimary,
            Secondary = Accent,
            Tertiary = PrimaryBright,
            AppbarBackground = SurfaceAppbar,
            AppbarText = InkAt(0.85),
            Surface = SurfaceCard,
            Background = SurfacePage,
            DrawerBackground = SurfacePage,
            DrawerText = InkAt(0.8),
            TextPrimary = InkAt(0.92),
            TextSecondary = InkAt(0.6),
            ActionDefault = InkAt(0.55),
            ActionDisabled = InkAt(0.25),
            Divider = InkAt(0.1),
            TableHover = InkAt(0.04),
            TableStriped = InkAt(0.02),
            LinesDefault = InkAt(0.12),
            OverlayDark = "rgba(0,0,0,0.35)"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = CornerRadius
        }
    };

    /// <summary>The ink color at a given opacity, as <c>rgba(...)</c>.</summary>
    private string InkAt(double opacity)
    {
        var hex = Ink.TrimStart('#');
        var red = Convert.ToInt32(hex[..2], 16);
        var green = Convert.ToInt32(hex.Substring(2, 2), 16);
        var blue = Convert.ToInt32(hex.Substring(4, 2), 16);

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"rgba({red},{green},{blue},{opacity})");
    }
}
