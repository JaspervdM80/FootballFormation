# Formation Builder and Pitch

## Formation Builder (`/games/{id}/formation`)
3-panel layout: Player List | Pitch | Substitutes
- Tabs for each period (2 halves or 4 quarters)
- Drag state lives in `LineupDragState` (`Drag.PlayerId` / `Drag.FromSlotIndex` / `Drag.FromSub`), cleared via `Drag.Clear()`
- Pitch slots are index-based: `GamePlayerPosition.SlotIndex` is the source of truth,
  position matching is the fallback for legacy rows (see `BuildSlotAssignments`)
- Page requires the Admin role (`[Authorize(Roles = AppRoles.Admin)]`); anonymous visitors get the
  read-only overview
- Actions: Save All, Copy to Next Period
- Playing time table is built by `PlayingTimeReport.Build(...)`, not by the page; it renders
  whenever there are players (it does not wait for every period to be filled)
- Its totals read the match clock once the game has been run live, and are the planned
  `periods × period length` estimate before that. An estimate is written `~30 min` in the muted
  ink and the table carries a footnote — the same `~` the player report uses, so the mark means
  one thing across the app. The choice is per game, so every row of one table is marked alike


## Drag & Drop (HTML5 API)
- **Player list → Pitch**: Assigns player to position slot
- **Player list → Sub bench**: Adds as substitute
- **Pitch → Pitch**: Swaps two players' slots (`Drag.FromSlotIndex` is set ⇒ the drop is a swap)
- **Pitch → Sub bench**: Drop on empty bench area moves player to bench; drop **on a sub** swaps the two (`OnSwapFieldPlayerWithSub`)
- **Sub bench → Pitch**: Sub takes the slot; the displaced starter goes to the bench
- Click on assigned player = remove from position (only while `Draggable`; elsewhere a tap raises
  `OnPlayerClicked`)
- `@ondragstart`/`@ondrop` sit on the **inner** element — the occupied chip `.pitch-player` and the
  empty slot `.pitch-empty` — never on the `.pitch-slot` wrapper, which carries only the
  coordinates. Relevant when scripting or testing a drag: a synthetic event aimed at the wrapper
  reaches no handler
- **Touch devices**: `wwwroot/js/drag-drop-touch.js` (Web project) converts touch gestures into
  synthetic `DragEvent`s with a real `DataTransfer` — Blazor ignores drag events without one.
  A floating ghost follows the finger; an 8px threshold separates taps from drags. Draggable
  chips have `touch-action: none` (app.css), so a scroll gesture cannot start on a chip.


## Position Fit Colors (5 tiers)
| Tier | CSS class | Color | Example |
|---|---|---|---|
| Preferred | fit-preferred | Dark green (#1b5e20) | CB in CB |
| NaturalFit | fit-natural | Light green (#388e3c) | W in LW, DEF in CB |
| Alternative | fit-alternative | Blue (#1565c0) | Listed CAM alt, placed in CAM |
| Compatible | fit-compatible | Orange (#e65100) | Alt is CM, placed in CM |
| OutOfPosition | fit-out-of-position | Red (#b71c1c) | ST in CB |

The classes are applied by `Pitch.razor.cs` and defined in `Pitch.razor.css`; the hex values come
from the `--fit-*` tokens in `theme.css`.

Logic in `Core/Reporting/PositionFitHelper.cs`. Broad positions (W, DEF, MID, ATT) naturally cover all their specific variants.


## Pitch
One component for all three pitches — the drag-drop builder, the shareable overview and the live
screen. It used to be two near-identical components plus a third copy of the slot logic inside
`FormationBuilder`; the assignment rule now lives in `Core/Models/FormationSlots.cs` and is tested
there, so a lineup can never be laid out one way on one screen and another way on the next.

- `aspect-ratio: 3/4`; position coordinates from `PitchPositionHelper.cs` (left%, top%).
- `Size` picks the chip scale: `Regular` (52px, the overview and live screens) or `Compact` (44px,
  the builder, where the pitch shares the screen with the bench). Everything that differs between
  the two — chip size, fonts, line alphas — is a CSS custom property set by the size class, so the
  variants cannot drift apart again.
- `ConstrainHeight` caps the pitch at 65vh; the builder uses it.
- `HidePositionFit` flattens every chip to `fit-preferred`; anonymous visitors get that.
- `Draggable` turns on the builder behaviour: chips are draggable, empty slots are drop targets
  with the pulsing white `drop-ready` highlight, and tapping a chip removes the player.
- `OnPlayerClicked` is **optional**. Unset (and not draggable), the pitch is inert — which is what
  the overview and every spectator wants. Set, occupied slots gain `.pitch-clickable` (pointer
  cursor, press feedback) and tapping one raises the player id. The live match screen wires it only
  when the viewer is an admin *and* a half is actually being played.
- The five fit colors are tokens in `theme.css` (`--fit-*`), shared with the builder's legend and
  its playing-time dots — one definition, three consumers.


