namespace FootballFormation.UI.Theming;

public enum TokenKind
{
    Color,
    Gradient,
    Radius,
    Size,
}

public sealed record TokenGroup(string NameKey, TokenKind Kind, IReadOnlyList<string> Tokens);

/// What /styleguide draws a swatch for. styleguide.spec.js asserts every custom property the page's stylesheets declare is listed here, so
/// a token added to <see cref="ClubTheme"/> or theme.css without one turns the browser check red instead of going unnoticed.
public static class DesignTokens
{
    public static readonly IReadOnlyList<TokenGroup> Groups =
    [
        new("Club", TokenKind.Color,
        [
            "--club-primary",
            "--club-primary-bright",
            "--club-primary-deep",
            "--club-on-primary",
            "--club-accent",
            "--club-accent-bright",
            "--club-accent-deep",
            "--club-logo-bg",
        ]),
        new("Surfaces", TokenKind.Color,
        [
            "--surface-page",
            "--surface-card",
            "--surface-card-alt",
            "--surface-appbar",
            "--surface-appbar-alt",
        ]),
        new("Ink", TokenKind.Color,
        [
            "--ink",
            "--ink-muted",
            "--ink-subtle",
            "--ink-faint",
        ]),
        new("Semantic", TokenKind.Color,
        [
            "--color-guest",
            "--color-guest-bright",
            "--color-danger",
            "--color-danger-bright",
            "--color-success-bright",
            "--color-success-deep",
            "--color-warning",
            "--color-warning-bright",
            "--color-away",
            "--color-away-bright",
        ]),
        new("Position Fit", TokenKind.Color,
        [
            "--fit-preferred",
            "--fit-preferred-edge",
            "--fit-natural",
            "--fit-natural-edge",
            "--fit-alternative",
            "--fit-alternative-edge",
            "--fit-compatible",
            "--fit-compatible-edge",
            "--fit-out-of-position",
            "--fit-out-of-position-edge",
        ]),
        new("Gradients", TokenKind.Gradient,
        [
            "--gradient-primary",
            "--gradient-accent",
            "--gradient-card",
            "--gradient-appbar",
        ]),
        new("Shape", TokenKind.Radius,
        [
            "--corner-radius",
        ]),
        // Drawn at the token's own size, so it grows to the 44px floor on a coarse pointer the way the real buttons do.
        new("Sizing", TokenKind.Size,
        [
            "--action-btn-size",
        ]),
    ];
}
