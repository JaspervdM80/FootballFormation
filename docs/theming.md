# Theming & Club Branding

Colors are centralized as CSS custom properties so the app can be re-skinned for a
different club by editing one file. No SCSS/build step — plain CSS variables, resolved
at runtime.

## Where the tokens live

`src/FootballFormation.Web/wwwroot/theme.css` is the single source of truth. It is
loaded **before** `app.css` in `App.razor`, so every stylesheet and inline style can
reference the tokens.

The active theme is **GJS Gorinchem (light)**: white page, light-green sections, crest
red primary, crest banner green accent. Colors were sampled from the club crest
(`wwwroot/icons/icon-512.png`).

## Token groups

| Group | Tokens | Notes |
|---|---|---|
| Brand | `--club-primary` `--club-primary-bright` `--club-primary-deep` `--club-on-primary` | `-bright` is the emphasis shade for text on light surfaces; `-deep` is the gradient partner |
| Accent | `--club-accent` `--club-accent-bright` `--club-accent-deep` | crest green |
| Identity | `--club-logo` `--club-logo-bg` | logo is a `background-image` URL, rendered on a `.app-title-logo` span |
| Surfaces | `--surface-page` `--surface-card(-alt)` `--surface-appbar(-alt)` | `-alt` tokens are gradient partners |
| Text | `--ink` | near-black with a green cast; **all text derives from this** |
| Semantic | `--color-guest(-bright)` `--color-danger(-bright)` `--color-success-bright` | club-independent |
| Gradients | `--gradient-primary` `--gradient-accent` `--gradient-card` `--gradient-appbar` | composed from the tokens above |

## Conventions

- **Derived shades use `color-mix`**, not separate tokens per opacity:
  `color-mix(in srgb, var(--club-primary) 12%, transparent)`. Text opacities derive from
  `--ink` the same way (e.g. a muted label is `color-mix(in srgb, var(--ink) 45%, transparent)`).
- **Text on colored fills stays literal white** (`#fff`): primary buttons, position chips
  on the pitch, the success snackbar. Only text *on surfaces* derives from `--ink`.
- **Success is always green** (`--gradient-accent`), never the club primary — with a red
  club, a red "success" toast reads as an error.
- **On-pitch colors are physical, not themed**: the green field, white markings, and the
  5-tier position-fit colors stay fixed whatever the club palette is. The drop-ready
  highlight is white so it survives any palette.

## MudBlazor is a second source — keep it in sync

MudBlazor's palette lives in C#, not CSS, so it can't read `theme.css`. The palette in
`MainLayout.razor.cs` (`PaletteLight`, with `IsDarkMode="false"` in `MainLayout.razor`)
duplicates the same red/green/ink values and **must be updated alongside `theme.css`**.
Its text/line shades are the `#182b1f` ink at various alphas.

## Re-theming for another club

1. Edit the tokens in `theme.css`.
2. Mirror primary/accent/surfaces/ink into the `MainLayout.razor.cs` palette.
3. Replace `wwwroot/icons/icon-*.png` (or point `--club-logo` elsewhere) and set
   `--club-logo-bg`.
4. Update the white PWA chrome if the page color changes: `theme-color` meta in
   `App.razor` and `theme_color`/`background_color` in `manifest.webmanifest`.
5. `screenshot.js` reads `--surface-appbar-alt` for the export background — no change
   needed, it follows the theme.

The eventual step-2 refactor is a `ClubTheme` config record (colors + logo + name) that
feeds both `theme.css` and the MudTheme from one source; not built yet.

## Naming debt

`.badge-gold` / `.btn-gold` / `.gold-separator` are historical names from the old amber
theme — they are now club-primary (red), not gold. Left un-renamed to keep diffs small.
