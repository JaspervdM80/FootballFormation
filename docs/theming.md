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
  `color-mix(in srgb, var(--club-primary) 12%, transparent)`. That holds for backgrounds and
  borders, where the exact value is a matter of taste.
- **Muted *text* uses the named ink ramp, not an ad-hoc percentage.** `theme.css` defines three:

  | Token | Ratio on `--surface-page` | For |
  |---|---|---|
  | `--ink-muted` | 4.8:1 | body-weight secondary text — dates, subtitles, captions, table headers |
  | `--ink-subtle` | 3.9:1 | large or secondary text — empty states, quieter labels |
  | `--ink-faint` | 3.3:1 | non-text contrast — icons, dividers, placeholder glyphs |

  These replaced inline mixes at 25–60% that were inherited from the old dark theme and, on the
  light one, measured 1.6:1 to 3.0:1 — well under WCAG AA (4.5:1 body, 3:1 large). Small text that
  carries meaning uses `--ink-muted` or darker. The percentages behind the tokens are tuned to
  `#182b1f` on `#ffffff`; **recompute them if either changes** for another club.
- **Text on colored fills stays literal white** (`#fff`): primary buttons, position chips
  on the pitch, the success snackbar. Only text *on surfaces* derives from `--ink`.
- **Success is always green** (`--gradient-accent`), never the club primary — with a red
  club, a red "success" toast reads as an error.
- **On-pitch colors are physical, not themed**: the green field, white markings, and the
  5-tier position-fit colors stay fixed whatever the club palette is. The drop-ready
  highlight is white so it survives any palette.

## One source, two styling systems

MudBlazor's palette lives in C#, not CSS, so it cannot read a stylesheet — the app used to
carry the same red/green/ink values twice and ask whoever edited one to remember the other.

Both now come from **`ClubTheme`** (`src/FootballFormation.UI/Theming/ClubTheme.cs`):

- `ToCssVariables()` emits the `--club-*`, `--surface-*` and `--ink` tokens into a `<style>`
  block in `App.razor`, before every stylesheet that reads them.
- `ToMudTheme()` builds the `PaletteLight` (used with `IsDarkMode="false"` in `MainLayout`).
  Its text/line shades are the ink color at various alphas, mixed in `InkAt`.

`theme.css` keeps only what is *not* club branding — the semantic status colors, the five
position-fit tiers, and the gradients composed from the club tokens via `var()`.

## Re-theming for another club

1. Edit `ClubTheme.Gjs` — colors, logo, corner radius. That is the whole palette, both systems.
2. Replace `wwwroot/icons/icon-*.png` (or point `LogoUrl` elsewhere) and set `LogoBackground`.
3. Update the white PWA chrome if the page color changes: `theme-color` meta in
   `App.razor` and `theme_color`/`background_color` in `manifest.webmanifest`.
4. `screenshot.js` reads `--surface-appbar-alt` for the export background — no change
   needed, it follows the theme.

## Breakpoints

The app is styled with hand-written media queries, not MudBlazor's breakpoint system —
`MudHidden` and `Breakpoint.*` appear nowhere, and only `Home.razor` uses the `MudItem xs/sm/md`
grid. The ladder, deliberately:

| Width | Meaning |
|---|---|
| **959.98px** | MudBlazor's `md`. Formation builder stacks its panels; the overview drops to one pitch column |
| **760px** | The two statistics pages go from four stat tiles to two |
| **700px** | *Content-driven, not a MudBlazor tier*: where the nav links, season picker and admin block stop fitting on one bar. Drawer replaces the top nav |
| **599.98px** | MudBlazor's `xs`, where it stacks a table into per-row cards. `.stacked-table` takes over there |

Always `599.98`, never `599` or `600`. `600` fires *at* the boundary MudBlazor is switching on, and
`599` leaves a fractional gap reachable by browser zoom where half the page has restacked and half
has not — both have been real bugs here.

## Re-theming checklist for the responsive bits

The two `pointer: coarse` blocks (`.action-btn`, `.goal-remove`) grow tap targets to 40px on a
touch screen, and `@media (hover: hover)` guards the hover states so they do not stick after a tap.
Keep both if you add interactive controls.

## Naming debt

`.badge-gold` / `.btn-gold` are historical names from the old amber theme — they are now
club-primary (red), not gold. Left un-renamed to keep diffs small. (`.gold-separator` was in this
list too; it was unused and has been deleted.)
