# GJS Meiden design system — conventions

Foundations only: tokens, fonts and a base stylesheet. There is no component bundle — the real app is Blazor/MudBlazor — so build UI from plain elements styled with these tokens.

## Setup
Link `styles.css`. It imports `tokens/club.css` (club colours), `tokens/theme.css` (ink ramp, status, position fit, gradients) and `tokens/fonts.css` (DM Sans). Without it text falls back to Helvetica and every `var(--*)` is empty.

## Styling idiom: CSS custom properties
No utility classes. Style with `var(--token)`; make tints in place with `color-mix(in srgb, var(--token) N%, transparent)` rather than new hex values.

| Family | Tokens |
|---|---|
| Club | `--club-primary` (crest red), `--club-primary-bright`, `--club-primary-deep`, `--club-on-primary`, `--club-accent` (banner green), `--club-accent-bright`, `--club-accent-deep` |
| Surfaces | `--surface-page` (white), `--surface-card`, `--surface-card-alt`, `--surface-appbar`, `--surface-appbar-alt` |
| Text | `--ink` body/headings; `--ink-muted` small text that carries meaning; `--ink-subtle` empty states; `--ink-faint` icons and dividers only |
| Status | `--color-danger`, `--color-success-bright`, `--color-warning`, `--color-guest`, `--color-away` — each with a `-bright` shade; use `-bright` for text and on a 12% tint of itself |
| Position fit | `--fit-preferred`, `--fit-natural`, `--fit-alternative`, `--fit-compatible`, `--fit-out-of-position`, each with `-edge` |
| Shape | `--corner-radius` (12px), `--gradient-primary`, `--gradient-accent`, `--gradient-card`, `--gradient-appbar` |

## Rules
- Light theme only. Text in `-bright` club shades on light surfaces; the plain shades are fills.
- Cards: `background: var(--gradient-card)`, `border: 1px solid color-mix(in srgb, var(--ink) 5%, transparent)`, `border-radius: var(--corner-radius)`.
- Home is club green, away is `--color-away`.
- Position-fit colours are never replaced with club colours: green-to-red means good-to-bad, on a green pitch.
- Mobile first (coaches use it on the touchline): every tap target at least 44px tall, at least 8px apart.
- UI copy is Dutch by default.

## Example
```html
<article style="background:var(--gradient-card);border:1px solid color-mix(in srgb,var(--ink) 5%,transparent);border-radius:var(--corner-radius);padding:16px">
  <div style="font-size:13px;color:var(--ink-muted)">za 20 sep · Thuis</div>
  <h3 style="margin:4px 0;color:var(--ink)">GJS JO11-1 – VVAC JO11-2</h3>
  <button style="min-height:44px;padding:0 16px;border:0;border-radius:var(--corner-radius);background:var(--club-primary);color:var(--club-on-primary);font:inherit;font-weight:600">Opstelling</button>
</article>
```
