# UI Components & Interactions

## Localization
- Dutch is the default culture; English is the fallback (and the switcher's second option)
- All user-facing strings go through `IStringLocalizer<Strings>` (`L`); **the English text
  is the resource key**, so only `Strings.nl.resx` exists — missing keys render as English
- Language switcher: globe menu in `MainLayout` → `/culture/set` endpoint → culture cookie
  → full page reload (circuit culture is fixed at startup)
- Known limitation: `Result.Error` messages from Core services are English

## Formation Builder (`/games/{id}/formation`)
3-panel layout: Player List | Pitch | Substitutes
- Tabs for each period (2 halves or 4 quarters)
- Drag state lives in `LineupDragState` (`Drag.PlayerId` / `Drag.FromSlotIndex` / `Drag.FromSub`), cleared via `Drag.Clear()`
- Pitch slots are index-based: `GamePlayerPosition.SlotIndex` is the source of truth,
  position matching is the fallback for legacy rows (see `BuildSlotAssignments`)
- Page requires admin login (`[Authorize]`); anonymous visitors get the read-only overview
- Actions: Save All, Copy to Next Period
- Playing time table is built by `PlayingTimeReport.Build(...)`, not by the page; it renders
  whenever there are players (it does not wait for every period to be filled)

## Drag & Drop (HTML5 API)
- **Player list → Pitch**: Assigns player to position slot
- **Player list → Sub bench**: Adds as substitute
- **Pitch → Pitch**: Swaps two players' slots (`Drag.FromSlotIndex` is set ⇒ the drop is a swap)
- **Pitch → Sub bench**: Drop on empty bench area moves player to bench; drop **on a sub** swaps the two (`OnSwapFieldPlayerWithSub`)
- **Sub bench → Pitch**: Sub takes the slot; the displaced starter goes to the bench
- Click on assigned player = remove from position
- `@ondragstart`/`@ondrop` sit on the **inner** circle (`.player-circle` / `.empty-circle`),
  not on the `.position-slot` wrapper — relevant when scripting or testing a drag
- **Touch devices**: `wwwroot/js/drag-drop-touch.js` (Web project) converts touch gestures into
  synthetic `DragEvent`s with a real `DataTransfer` — Blazor ignores drag events without one.
  A floating ghost follows the finger; an 8px threshold separates taps from drags. Draggable
  chips have `touch-action: none` (app.css), so a scroll gesture cannot start on a chip.

## InstallBanner (PWA install prompt)
- `Components/InstallBanner.razor(.cs)`, rendered once in `MainLayout`
- Shows a fixed bottom banner on mobile browsers when the app is not installed
  (standalone) and not previously dismissed (localStorage `pwa-install-dismissed`)
- Android: button triggers the native install prompt via `window.pwaInstall` (js/pwa.js,
  which captures `beforeinstallprompt`); falls back to ⋮-menu instructions if unavailable
- iOS: no install API exists — shows "Tap Share, then Add to Home Screen" text instead

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
