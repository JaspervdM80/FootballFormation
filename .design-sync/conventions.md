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
- Status is green, actions are light. A card that *reports* (next match, last result, season record) is filled with `var(--gradient-accent)` and set in `--club-on-primary`; a tile that *goes somewhere* keeps the light card look. Never make a navigation tile green, or the two stop reading apart.
- On a green status card: labels at 85% opacity, secondary lines at 90% — not the ink ramp, which is tuned for white. A pale tinted chip vanishes on green, so a venue badge becomes solid `--club-on-primary` with its home/away text colour, and a win pill turns white with `--club-accent-deep` text; loss stays `--color-danger-bright`, draw is white at 30%.
- A record reads caption over figure: a small `W-G-V` caption, the big `5-2-1` below it, then `Doelsaldo: +7` as its own line — never an abbreviation squeezed beside the numbers.
- A result is the score plus a one-letter result pill (W/G/V), not a coloured score — red and green text both fail on a green card.
- A truncating name gives way before the badge that follows it: ellipsis on the name only, the badge `flex: none`.
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
