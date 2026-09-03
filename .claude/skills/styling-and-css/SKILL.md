---
name: styling-and-css
description: Writing CSS, picking a colour, or fighting a MudBlazor 9.x style. Covers the silent scoped-CSS failure, app.css vs .razor.css, the theme tokens and ink ramp, and the global .mud-* rules that broke layout. Use whenever a .razor.css, app.css or theme.css rule is added or changed.
---

# Styling and CSS

## Scoped CSS has a silent failure mode

A class in `Foo.razor.css` compiles to `.cls[b-<fooHash>]` and **will not match identical markup on
another page** — no warning, it just renders unstyled. `.action-btn` defined in `Games.razor.css`
never matched the same markup on `/settings`, and those buttons rendered as native browser chrome for
as long as nobody looked.

**The same trap catches a rule that never leaves its own page: a child component's root element has no
scope attribute either.** `.live-control-row > *` in `LiveMatch.razor.css` matched nothing, because
every child of that row is a `MudButton` and the `<button>` MudBlazor renders carries no `b-<hash>`.
The tell is that the *container* is styled and the *children* are not.

**Anything used by more than one page, or selecting past a MudBlazor component's root element, goes in
`Web/wwwroot/app.css`.** `.action-btn`, `.badge-*`, `.stat-tile*`, `.stacked-table`,
`.live-scoreboard` and `.live-action-btn` are all there for this reason.

**It cuts the other way too: a class in `app.css` is global.** `.overview-capture` named both the
block being screenshotted on `/games/{id}/overview` and, in `app.css`, the buttons under it — so the
button rule's `display: inline-flex` laid the capture block out as a row, and the page's own scoped
rule could not win because it never declared `display`. Check the page's `.razor.css` before reusing
a name.

## Colours come from tokens

`ClubTheme.Gjs` is the single palette for both styling systems — the MudBlazor palette it builds and
the CSS custom properties in `theme.css`. `theme.css` keeps only what is *not* club branding: the
semantic status colours, the five position-fit tiers, and gradients composed from the club tokens via
`var()`.

Muted text uses the named ink ramp — `--ink-muted` / `--ink-subtle` / `--ink-faint` — **never an ad-hoc
`color-mix` percentage**.

If the page colour changes, the `theme-color` meta in `App.razor` and `theme_color`/`background_color`
in `manifest.webmanifest` must change with it, or the PWA chrome keeps the old brand.

`.badge-gold` / `.btn-gold` are historical names from the old amber theme — they are club-primary
(red) now, left un-renamed to keep diffs small.

## MudBlazor 9.x rules that have already cost time

- **Never let a global `.mud-*` rule touch layout.** app.css's card rule set `position: relative` on
  `.mud-paper`, which `MudPopover` also carries — same specificity, later in source order, so it won,
  and every dropdown rendered as a full-width band across the top of the page. The fix is the card
  rule never claiming a popover in the first place:
  `.mud-paper:not(.mud-popover):not(.mud-dialog)`. **Excluding the popover beats overriding it back.**
- **`MudMenu`'s `Class` lands on the root wrapper, not the activator button.** `Class="btn-gold"`
  painted an invisible `div` while the button kept MudBlazor's defaults. There is no `ActivatorClass`
  in 9.7 — style `.<your-class>.mud-menu .mud-button-root` instead.
- **The popover, dialog and snackbar providers must be inside an interactive render mode.** They are
  in `<InteractiveShell />`, rendered by each interactive page — not in `MainLayout`, which renders
  statically on every page. `MudThemeProvider` is the exception and stays in the layout.
- `MudForm.Validate()` is obsolete — use `ValidateAsync()`.
- Multi-select binding takes `IReadOnlyCollection<T>`, not `IEnumerable<T>`.
- `ShowMessageBox` is gone — use the custom `ConfirmDialog`. Dialogs must not close on backdrop click.
- `Position` was renamed to `PlayerPosition` because `MudBlazor.Position` exists.

## Two traps when measuring geometry from the DOM

- **An open dialog shrinks `<body>`.** MudBlazor adds `scroll-locked-no-padding` — `overflow: hidden`
  on a `<body>` box shorter than the viewport. Walking ancestors to intersect every clipping box then
  reports every dialog as half scrolled out of view. It is not: an ancestor's overflow only clips a
  descendant it is a containing block for, and MudBlazor's dialog container is `position: fixed`.
  `scripts/touch-targets.mjs` carries that rule as `containsFor`.
- **The date picker's month header slides**, so for a moment after *Previous month* it still reports
  the month you just left. Wait for the header text to *change* before reading it again, and choose
  the direction from that settled value so an overshoot walks back instead of spiralling. Anything
  stepping a MudBlazor picker has this shape.

Detail: [docs/theming.md](../../../docs/theming.md) ·
[docs/known_issues/](../../../docs/known_issues/blazor-mudblazor.md)
