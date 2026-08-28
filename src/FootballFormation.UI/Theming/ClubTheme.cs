namespace FootballFormation.UI.Theming;

/// The app styles through two systems — CSS custom properties and a <see cref="MudTheme"/> — and both derive from here, so re-skinning
/// for another club means editing <see cref="Gjs"/> and nothing else. Shades are mixed where used, not stored per alpha level.
public sealed record ClubTheme
{
    /// What a <see cref="FootballFormation.Core.Models.Club"/> stores instead of the styles, which are not editable.
    public required string Name { get; init; }

    /// Crest red.
    public required string Primary { get; init; }

    /// Emphasis red, for text on light surfaces.
    public required string PrimaryBright { get; init; }

    public required string PrimaryDeep { get; init; }

    public required string OnPrimary { get; init; }

    /// Crest banner green.
    public required string Accent { get; init; }

    /// Emphasis green, for text on light surfaces.
    public required string AccentBright { get; init; }

    public required string AccentDeep { get; init; }

    /// A bare path under wwwroot, not a CSS value: the app bar renders it as an <c>img</c> and ToCssVariables wraps it for the
    /// custom property, so the two cannot name different files.
    public required string LogoPath { get; init; }

    /// The chip behind the crest in the app bar.
    public required string LogoBackground { get; init; }

    public required string SurfacePage { get; init; }
    public required string SurfaceCard { get; init; }

    public required string SurfaceCardAlt { get; init; }

    public required string SurfaceAppbar { get; init; }

    public required string SurfaceAppbarAlt { get; init; }

    /// Near-black with a green cast. Every text and line shade derives from it.
    public required string Ink { get; init; }

    public required string CornerRadius { get; init; }

    /// The GJS Gorinchem light theme, taken from the club crest: red shield, green banner, white page, light-green sections.
    public static readonly ClubTheme Gjs = new()
    {
        Name = Club.DefaultTheme,
        Primary = "#e11d24",
        PrimaryBright = "#c8151c",
        PrimaryDeep = "#a3141a",
        OnPrimary = "#ffffff",
        Accent = "#0a8f3d",
        AccentBright = "#0c7a37",
        AccentDeep = "#076b2e",

        LogoPath = "icons/icon-192.png",
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

    /// Every theme a club can be put on. Adding one is a code change by design — see the type comment.
    public static readonly IReadOnlyList<ClubTheme> All = [Gjs];

    /// Falls back rather than throwing: a club naming a theme this build no longer has should render in the default one, not fail.
    public static ClubTheme Named(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Gjs;

    /// The crest to draw for a club: its own if it has one, otherwise its theme's. The one place that fallback lives.
    public static string LogoFor(Club? club) =>
        club?.LogoUrl is { Length: > 0 } logo ? logo : Named(club?.ThemeName).LogoPath;

    /// Emitted into the document head, so the tokens exist before any stylesheet that reads them.
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

    /// The shades are mixed from <see cref="Ink"/> here rather than in CSS, because a MudBlazor palette does not take color-mix.
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

    /// The ink color at a given opacity, as <c>rgba(...)</c>.
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
