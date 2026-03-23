# UI Components & Interactions

## Formation Builder (`/games/{id}/formation`)
3-panel layout: Player List | Pitch | Substitutes
- Tabs for each period (2 halves or 4 quarters)
- Drag state tracked via `DraggedPlayerId` and `DraggedFromPosition`
- Actions: Save All, Copy to All Periods
- Playing time overview table appears when all periods are fully filled

## Drag & Drop (HTML5 API)
- **Player list → Pitch**: Assigns player to position slot
- **Player list → Sub bench**: Adds as substitute
- **Pitch → Pitch**: Swaps two players' positions (uses `DraggedFromPosition`)
- **Pitch → Sub bench**: Moves player from pitch to bench
- Click on assigned player = remove from position

## Position Fit Colors (5 tiers)
| Tier | CSS class | Color | Example |
|---|---|---|---|
| Preferred | chip-preferred | Dark green (#1b5e20) | CB in CB |
| NaturalFit | chip-natural | Light green (#388e3c) | W in LW, DEF in CB |
| Alternative | chip-alternative | Blue (#1565c0) | Listed CAM alt, placed in CAM |
| Compatible | chip-compatible | Orange (#e65100) | Alt is CM, placed in LCM |
| OutOfPosition | chip-out-of-position | Red (#b71c1c) | ST in CB |

Logic in `PositionFitHelper.cs`. Broad positions (W, DEF, MID, ATT) naturally cover all their specific variants.

## PitchView
- Pitch is `aspect-ratio: 3/4`, `max-height: 65vh`
- Position coordinates from `PitchPositionHelper.cs` (left%, top%)
- Empty slots show position label, pulsing green border when drag active
- Assigned slots show colored circle with shirt number + short name
- Circles are both draggable (for swap) and drop targets

## MudBlazor 9.x Notes
- `ValidateAsync()` not `Validate()`
- `IReadOnlyCollection<T>` for multi-select `@bind-SelectedValues`
- `IMudDialogInstance` (cascading parameter in dialogs)
- `MudDialogProvider`, `MudSnackbarProvider`, `MudPopoverProvider` all in MainLayout
- Theme: dark mode, Primary=#66BB6A, Secondary=blue, Surface=#1E1E2E
