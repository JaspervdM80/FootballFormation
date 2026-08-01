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

## Squad table (`/players`) — responsive layout
- One `MudTable` (`.players-table`) serves both breakpoints; **desktop is a normal table
  and must stay untouched** when changing mobile.
- The four data cells carry classes `cell-name` / `cell-pref` / `cell-alt` /
  `cell-actions` so mobile CSS can place them; desktop ignores the classes.
- Below MudBlazor's `599.98px` xs breakpoint (its stacked-card mode), `app.css` overrides
  the card into a **CSS grid** per row: name + preferred position on line 1, alternative
  positions on line 2, edit/delete on line 3, all data right-aligned except the name. The
  per-cell `::before` labels are hidden — the grid replaces "label: value" stacking.
- To make the row a grid container, `.mud-table-root`/`.mud-table-body` are flipped to
  `display: block` on mobile (they are normal table boxes on desktop).
- Gotcha: MudBlazor's dense-table rule outspecifies a plain `.cell-name` selector, so the
  mobile name font-size is set on the inner `.player-name-cell` wrapper, not the cell.
- A row with no alternatives collapses its line via `.cell-alt:not(:has(.badge-gold))`
  (alternatives render as `.badge-gold`, preferred as `.badge-teal`).
- Sorting is unavailable on mobile — MudBlazor collapses the header to zero height in card
  mode. Pre-existing, not caused by the grid override.

## Season picker (`Components/SeasonPicker.razor`)
The global season filter, backed by the scoped `SeasonState` (see
[patterns.md](patterns.md#ui-state-services)).

- **Rendered twice from one component**: in the app bar right after `<MudSpacer />`, and inside the
  mobile drawer above the nav menu. `.topbar-nav` is hidden below 700px while the drawer takes over,
  so both placements are needed. The `Compact` parameter shortens the app-bar label to "25/26"
  (`Season.ShortName`) while the drawer shows the full "2025/26".
- Its CSS lives in `Web/wwwroot/app.css`, **not** scoped CSS, precisely because it renders from two
  places — isolation would force a duplicate stylesheet, the problem `.stat-tiles` already has
  between `SeasonStats.razor.css` and `PlayerStats.razor.css`.
- **Route allowlist**: only shown where a season filter changes what is on screen — `/games`,
  `/stats`, `/players` (the squad is per season) and `/players/{id}/stats`. Hidden on `/`,
  `/settings` and the single-game routes, where it would be inert or (on `/settings`) misleading
  while the season list is edited. The component subscribes to `NavigationManager.LocationChanged`
  so visibility follows navigation.
- Selecting a season **never** writes `Season.IsCurrent` — the picker is reachable by anonymous
  visitors, and `IsCurrent` is shared state owned by the admin on `/settings`.

## Squad page (`/players`)
Season-scoped: it follows the season picker and shows that season's squad, not everyone on file.

- Row actions (admin only): **guest toggle** (star), **remove from squad** (person-remove), and an
  overflow `MudMenu` holding "Edit player" and "Delete player permanently". Removing from a squad is
  the everyday action; deleting a *person* cascades their lineup and goal rows in every season, so
  it is demoted out of the icon row.
- "Add Player" is a `MudMenu` with two items: **New player** (creates the person *and* adds them to
  this squad in one action) and **Existing player** (`SquadMemberDialog`, picking from
  `GetNonMembersAsync` — someone from an earlier season, or a guest being promoted).
- **Copy squad from {previous season}** appears whenever a previous season exists, and is the
  primary action in the empty state. It preserves guest flags and is idempotent.
- Three edge states: "All seasons" selected (a card asking you to pick one — a squad belongs to
  exactly one season); an empty squad with a previous season (copy-forward offer); an empty squad
  with no previous season (plain "add your first player").
- The `GUEST` badge is driven by the **member's** flag, so the existing `.badge-guest` CSS and its
  mobile hide rule are untouched.

## MudBlazor 9.x Notes
- `ValidateAsync()` not `Validate()`
- `IReadOnlyCollection<T>` for multi-select `@bind-SelectedValues`
- `IMudDialogInstance` (cascading parameter in dialogs)
- `MudIconButton` takes lowercase `title`, not `Title` (the MUD0002 analyzer flags it). The
  `.action-btn` row buttons are plain `<button>` elements with `title` anyway.
- **`MudMenu` + `ActivatorContent` does not wire itself up.** The custom activator receives a
  `MenuContext` and *you* must call `context.ToggleAsync` — MudBlazor attaches no click handler to
  the `.mud-menu-activator` wrapper, though it does give it `role="button"` and `tabindex="0"`,
  leaving it focusable but inert. Prefer `Label` + `StartIcon`/`EndIcon` and style the generated
  button (as `SeasonPicker` does via `.season-picker .mud-button-root`), which arrives
  keyboard-accessible for free.
- **`MudMenu.Class` styles the root wrapper, not the activator.** There is no `ActivatorClass`
  parameter, so a button style has to be pushed down a level — `.btn-gold.mud-menu .mud-button-root`
  in app.css does that for the squad page's "Add Player" menu.
- **Never set `position` in a global `.mud-paper` rule.** `MudPopover` is a `.mud-paper`, and
  overriding its `position: absolute` turns every dropdown into a full-width band. See
  [known_issues.md](known_issues.md).
- `MudDialogProvider`, `MudSnackbarProvider`, `MudPopoverProvider` all in MainLayout
- Theme: **light mode**, club red/green from the crest. Colors are centralized as CSS
  variables — see [theming.md](theming.md). The MudBlazor palette (`MainLayout.razor.cs`)
  is a separate C# copy that must be kept in sync with `theme.css`.
