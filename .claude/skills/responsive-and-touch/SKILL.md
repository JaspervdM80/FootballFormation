---
name: responsive-and-touch
description: Anything that a thumb touches or that changes at a breakpoint — tap-target sizing, the 44px/8px floors, phone dialogs and the .dialog-sheet, the MudBlazor date picker, and why a width-only media query misses a landscape phone. Use when adding an interactive control or writing a media query.
---

# Responsive and touch

## The breakpoint ladder

| Width | Meaning |
|---|---|
| **959.98px** | MudBlazor's `md`. Formation builder stacks its panels; the overview drops to one pitch column |
| **760px** | The two statistics pages go from four stat tiles to two |
| **700px** | *Content-driven*: where the nav links, season picker and admin block stop fitting on one bar. Drawer replaces the top nav |
| **599.98px** | MudBlazor's `xs`, where it stacks a table into per-row cards. `.stacked-table` takes over |

**Always `599.98`, never `599` or `600`.** `600` fires *at* the boundary MudBlazor is switching on;
`599` leaves a fractional gap reachable by browser zoom where half the page has restacked and half has
not. Both have been real bugs here.

## A width-only media query does not cover a phone in landscape

Turned sideways a phone is **short rather than narrow** (844×390) but it is still a thumb. Anything
about *touch* rather than *layout* keys off:

```css
@media (max-width: 599.98px), (max-height: 559.98px)
```

This is exactly what went wrong: the `.dialog-sheet` block is width-only, so a landscape phone got
MudBlazor's own 36.5px action buttons — the same geometry that had been reported in the first place.

## Two floors, both measured

- **Size** — every hit-testable element is at least **44×44** CSS px.
- **Clearance** — the gap to its nearest neighbour above or beside it is either **zero** (they meet,
  so there is no hole) or at least **8px**. Anything between is a dead gutter: too narrow to aim
  around, wide enough to swallow a tap, and awarded by the browser to whichever neighbour has the
  larger contact area. **`elementFromPoint` cannot see this** — it reports both neighbours as
  perfectly reachable.

Together they are the column-pitch guarantee: with no dead gutter left, the distance between two
column centres *is* the cell's own width.

`scripts/touch-targets.mjs` enforces both at 320×568, 360×640 and 844×390. Where geometry provably
cannot reach 44px the number is in `RECORDED_FLOORS` with its reason, and **a recorded floor is still
a floor**.

## Touch states come in pairs

The `pointer: coarse` blocks grow tap targets on a touch screen; `@media (hover: hover)` guards the
hover states so they do not stick after a tap. **Keep both halves** — every plain `<button>` in this
app has needed them.

## Dialogs on a phone (`.dialog-sheet`)

Every dialog goes through `DialogPrompts` with `UiFeedback.LockedDialog` (no backdrop-click close). A
**long form** additionally carries `Class="dialog-sheet"` — currently only `GameDialog`.

Below 600px it becomes a full-screen sheet: full width, `overflow: hidden` on the dialog so
`.mud-dialog-content` is the **one** scroller (a tap during momentum scrolling cannot resolve against
a stale layout), a real footer with a top border and the bottom safe-area inset, and the title taking
over the top safe-area inset.

The sheet *layout* is deliberately width-only — a full-screen sheet 844px wide is not an improvement.
The 44px button floor and the stepper rule live in their own `(max-width), (max-height)` query for the
reason above.

**A numeric field's steppers are hidden on a phone.** `.mud-input-numeric-spin` is two 24×16 buttons
stacked flush inside a 32px row: a third of the floor, no gap, and opposite effects, so a tap that
misses one hits the other and steps the wrong way. Two 44px targets cannot fit a 48px field, so the
field becomes what it already was — a number you type behind a numeric keyboard. Match MudBlazor's own
specificity when overriding: its rule is
`.mud-input-control.mud-input-number-control .mud-input-numeric-spin`, so a bare class loses.

## The date picker, which is where most of this was found

- **A MudBlazor dialog is 64px narrower than the phone.** `.mud-dialog-width-full` is
  `calc(100% - 64px)`, so a 360px phone gets 296px — narrower than the picker's 310px `min-width`,
  which is anchored to the input's left edge and hung **49px off a 320px screen, taking the whole
  Saturday column with it**. `.mud-picker-popover` centres the calendar on the viewport instead.
- **Give the popover an explicit width, never just a `max-width`.** `.mud-picker-calendar` is a
  wrapping flex row whose min-content width is one 40px day cell, so a shrink-to-fit popover collapses
  the month into a single column.
- **Day cells were 36px on a 40px column pitch with 4px dead gutters.** `--dp-day` spends the width
  instead (41.7px at 320 wide up to 52px, sized by height too so landscape does not blow past the
  viewport) and drops the side margins so the column pitch *is* the target.
- **The month name is a 23px button** — a real `<button>` that MudBlazor clamps because it doubles as
  the slide transition's viewport, sitting in the 56px row the arrows set, with 17px of dead div above
  and 16px below. 44px fits with no layout change. Its label is taken out of flow by
  `.mud-picker-slide-transition > *`, so growing it also needs `bottom: 0` and flex centring.
- **Sizing the calendar means sizing `.mud-picker-calendar-transition` too.** The grid is out of flow,
  so that container's `min-height` is the *only* thing reserving room for it — grow the cells without
  it and the last weeks draw over whatever follows. Always six rows; the calendar renders 42 cells
  whatever the month.
- **The flow is year → month → day**, so the toolbar's date line is not an escape route — it only
  restates the field behind the popover. `app.css` hides it in landscape to buy a 44px year button.

## Buttons need clear space above them, not just their own size

The game dialog's action row sat 28px under the last field, and `MudSelect`'s hit box reaches ~10px
past its own underline — leaving 18px of dead space. A thumb aimed at "Annuleren" opened the dropdown
instead, because the select is the far bigger target.

Detail: [docs/known_issues.md](../../../docs/known_issues.md) ·
[docs/testing.md](../../../docs/testing.md#touch-targets) ·
[docs/theming.md](../../../docs/theming.md)
