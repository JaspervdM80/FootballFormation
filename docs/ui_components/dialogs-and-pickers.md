# Dialogs and Date Pickers on a Phone

## Dialogs on a phone (`.dialog-sheet`)
Every dialog goes through `DialogPrompts` with `UiFeedback.LockedDialog` (no backdrop-click close).
A **long form** additionally carries `Class="dialog-sheet"` on its `MudDialog` — currently only
`GameDialog`, the new/edit match form, which is the app's longest and is filled in at a touchline
on a phone in portrait.

The class does nothing above 600px; the rules are in `app.css` behind a media query. Below it the
dialog becomes a full-screen sheet:

- **Full width.** MudBlazor's `calc(100% - 64px)` leaves a 360px phone 296px, which is narrower
  than the 310px date picker. See [known_issues](../known_issues/index.md).
- **One scroll container.** `overflow: hidden` on the dialog leaves `.mud-dialog-content` as the
  only scroller, so a tap during momentum scrolling can't resolve against a stale layout.
- **A real footer.** The action row gets a top border, 12px padding plus the bottom safe-area
  inset, and 44px-tall buttons. The point is the *gap*: the buttons need clear space above them,
  or a thumb aimed at "Annuleren" gets snapped to the unavailable-players select instead.
- The sheet covers the app bar, so `.mud-dialog-title` takes over the top safe-area inset.

The sheet *layout* is deliberately width-only, so a **landscape** phone keeps an ordinary centred
dialog — a full-screen sheet 844px wide is not an improvement.

Two rules are not part of that layout and so sit in their own query, `max-width: 599.98px` **or**
`max-height: 559.98px` — the same one the date picker uses, because turned sideways a phone is
short rather than narrow but it is still a thumb:

- **The action buttons' 44px floor.** Left inside the sheet block it did not apply in landscape, so
  a landscape phone got MudBlazor's own 36.5px buttons — the exact geometry that was reported. The
  touch-target guard measures this at 844x390.
- **A numeric field's steppers are hidden.** `.mud-input-numeric-spin` is two 24x16 buttons stacked
  flush inside a 32px input row: a third of the 44px floor, no gap between them, and opposite
  effects, so a tap that misses one hits the other and counts down instead of up. Two 44px targets
  cannot fit a 48px field, so on a phone they go and the field is what it already was — a number
  you type, behind a numeric keyboard. It affects all three `MudNumericField`s (match duration,
  the default on `/settings`, shirt number); above 600px the arrows are back. Match MudBlazor's own
  specificity when overriding it — its rule is
  `.mud-input-control.mud-input-number-control .mud-input-numeric-spin`, so a bare class loses.

### Date pickers below 600px (or 560px tall)
`MudDatePicker`'s popover is centred on the viewport instead of anchored to its input, and capped
at `100dvh - 16px` with `overflow-y: auto` so landscape scrolls rather than clips. It applies to
both dialogs that carry one — `GameDialog` and `SeasonDialog` on `/settings`. MudBlazor writes
`left`/`top` inline from JS, so every positioning declaration needs `!important`.

The day cells are **resized**, not left at MudBlazor's 36px — that line used to say the 36px was
"already the smallest a finger reliably hits" and it was wrong, which is why picking a day still
misfired after the popover was centred. `--dp-day` on the popover sizes all seven columns from the
viewport, bounded by what the height allows as well as the width, and every element that has to
line up with a column reads it:

| viewport | day cell | note |
| --- | --- | --- |
| 320x568 | 41.7px | the one phone that cannot reach 44px — seven of those need 308px and it has 308px |
| 360x640 | 47.4px | |
| 390x844 | 51.7px | |
| 412x915 | 52px | the `clamp()` ceiling |
| 844x390 | 36px | landscape is height-bound; the 100px banner halves to 56px so six rows still fit |

The 2px side margins go with it, so the **column pitch is the target** — there are no dead gutters
left to miss into, which the 40px pitch around a 36px cell used to have four of. Two knock-on rules
are load-bearing: `.mud-picker-calendar-transition`'s `min-height` is the only thing reserving room
for the grid (`.mud-picker-slide-transition` takes its children `position: absolute`), so it has to
scale with the cell or taller rows draw over what follows; and the weekday letters carry the same
width or the header stops lining up with the days.

Days were not the only undersized target, and by the second report not even the main one — "it's
mostly the month selection". Everything else a finger has to hit is raised to 44px too:

| control | MudBlazor | here |
| --- | --- | --- |
| month name (opens the month grid) | 23px tall | 44px |
| ‹ › month arrows | 40px | 44px |
| toolbar year (opens the year list) | 40px | 44px |
| year row in the list | 40px | 48px |

The month name is the one worth remembering: it is a real `<button>` clamped to `height: 23px`
because it doubles as the slide transition's viewport, and it sits in the 56px row the arrows
already set — so 17px of dead div above and 16px below, with no layout change needed to fix it.
Growing it also needs `bottom: 0` and flex centring on its label, which
`.mud-picker-slide-transition > *` has pinned `position: absolute` to the top.

That header row also had the same dead gutters the day grid did: MudBlazor gives the arrows
`margin: 6px`, leaving 6px of nothing between each arrow and the month name. The margin stays on
the vertical axis, where it sets the row's height, and goes sideways; the month button takes
`flex: 1` and grows to meet the arrows, so the row is three targets edge to edge.

In landscape the 56px toolbar has room for one 44px button, not two, so the date line is hidden and
the year button keeps its target. That strands nothing: the picker's flow is **year → month → day**,
and the date line only restates what is already in the field behind the popover.

Every number in this section is now measured on each `scripts/visual-check.sh` run — see
[testing](../testing/visual-and-touch-checks.md#touch-targets). Changing one of them means changing what the guard measures,
not just what this page says.


