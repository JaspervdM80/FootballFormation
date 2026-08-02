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
  when the viewer is an admin *and* a period is actually being played.
- The five fit colors are tokens in `theme.css` (`--fit-*`), shared with the builder's legend and
  its playing-time dots — one definition, three consumers.

## Live match screen (`/games/{id}/live`)
Phone-first single column (`max-width: 560px`), no `[Authorize]`: admin drives it, everyone else
watches the same URL read-only. Every control sits in an `<AuthorizeView>`.

- **The clock never round-trips.** A per-circuit 1-second `System.Timers.Timer` re-renders
  `Game.ElapsedSecondsAt(DateTime.UtcNow)` from the anchor the server stored, and it repaints only
  while the clock is running. See [models.md](models.md#game).
- **Spectators are pushed to via the singleton `LiveMatchNotifier`.** `LiveMatchService` raises it
  after every successful mutation; the page filters on its own `GameId`, reloads and
  `InvokeAsync(StateHasChanged)`, and unsubscribes in `Dispose`. In-process only — fine for the
  single Fly.io instance, but it needs a backplane if the app is ever scaled out.
- **`GetLiveAsync` is `AsNoTrackingWithIdentityResolution`, and it has to be.** A spectator's
  circuit keeps one scoped `AppDbContext` for its whole life, so a tracked `Game` keeps returning
  the score, clock and state from its first load while newly inserted goals appear alongside them —
  a live screen stuck at the old scoreline. Identity resolution keeps shared `Player` rows single.
- Controls are context-sensitive, and **only half time is a break**. A quarters game is still two
  halves: Q1→Q2 and Q3→Q4 offer "Start Q2"/"Start Q4", which call `AdvancePeriodAsync` and roll
  the lineup over *without stopping the clock*. Only after the first half or Q2 does the screen
  offer "Half time" (`EndPeriodAsync`, which does stop it) followed by "Start Q3". The rule lives
  in `PeriodTypeExtensions.IsFollowedByBreak`, not in the page.
- The pitch shows the live period; at the break and after full time the last one played, and before
  kick-off the first — so it is never blank when a lineup exists.
- Finishing asks for confirmation via `DialogPrompts.ConfirmAsync` (not `ConfirmDeleteAsync`,
  whose button says "Delete").
- **Minutes played is admin-only** (`LiveMinutesReport`), and shows exact time on the pitch rather
  than the `periodsPlaying × periodDuration` estimate the planning screens use. It is a computed
  property, so the running player's total climbs with the clock tick.
- **Mobile reorders the column with flex `order`**: what just happened matters more at a touchline
  than where everyone stands, so the line-up card (`.live-lineup`, `order: 1`) and the minutes
  table (`.live-minutes-card`, `order: 2`) drop below the timeline under 600px. Both rules live in
  `app.css` — the classes sit on `MudPaper` roots, which scoped CSS cannot reach.
- Goal and assist selects bind `int?`, not `int`: an `int` binds to 0, which is nobody's id but
  still renders as a chosen value, so the scorer field looked pre-filled.
- `/games` routes an `InProgress` game to `/live` for **everyone**, and shows a pulsing green
  `.action-live` button on its card. For other games the Live action is admin-only, and it
  disappears entirely once `Games.HasFinalScore(game)` — a settled game has nothing left to run,
  so the Result button beside it is the way in and a row click opens `/result`.
- `HasFinalScore` checks `MatchState` **as well as** the score fields, and must: `LiveMatchService`
  writes `ScoreHome`/`ScoreAway` on every goal, so a score alone only means the game has started.
  Testing the score by itself would hide the Live button on the very match being played.

## Live banner on the home page
`Home.razor` shows `.home-live-banner` whenever `LiveMatchService.GetInProgressAsync` finds a match
being played — opponent, live score, and a tap through to `/games/{id}/live`. It is visible to
everyone, since the people most likely to land on the home page mid-match are spectators.

It subscribes to **every** `LiveMatchNotifier.Changed` event rather than filtering by game id, the
way the live screen does: the banner has no game of its own until it loads one, so a match being
started is exactly the event it must not miss. That is what makes it appear on an already-open home
page without a refresh.

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
  mobile drawer above the nav menu. The `Compact` parameter shortens the app-bar label to "25/26"
  (`Season.ShortName`) while the drawer shows the full "2025/26".
- **Below 700px only the drawer copy shows** — `.mud-appbar .season-picker` is hidden, because the
  app bar is already carrying the hamburger, title, language menu and login on a phone. The rule
  targets `.mud-appbar` specifically; the drawer instance lives in `.drawer-season-picker`.
- It loads the season list itself (`SeasonState.EnsureLoadedAsync()` in `OnInitializedAsync`) —
  otherwise it would render nothing on the start page, where no page loads seasons. The call is
  memoized, so on the season-aware pages it shares the page's own query rather than adding one.
- Its CSS lives in `Web/wwwroot/app.css`, **not** scoped CSS, precisely because it renders from two
  places — isolation would force a duplicate stylesheet, the problem `.stat-tiles` already has
  between `SeasonStats.razor.css` and `PlayerStats.razor.css`.
- **Route allowlist**: `/games`, `/stats`, `/players` (the squad is per season),
  `/players/{id}/stats`, and `/` — the start page filters nothing, but it is where a visit begins,
  so the season can be set before navigating. Hidden on `/settings`, where it would be misleading
  while the season list itself is edited, and on the single-game routes, where it is inert. The
  component subscribes to `NavigationManager.LocationChanged` so visibility follows navigation.
- Selecting a season **never** writes `Season.IsCurrent` — the picker is reachable by anonymous
  visitors, and `IsCurrent` is shared state owned by the admin on `/settings`.

## Squad page (`/players`)
Season-scoped: it follows the season picker and shows that season's squad, not everyone on file.

- Row actions (admin only): **guest toggle**, **remove from squad** (person-remove), and an
  overflow `MudMenu` holding "Edit player" and "Delete player permanently". Removing from a squad is
  the everyday action; deleting a *person* cascades their lineup and goal rows in every season, so
  it is demoted out of the icon row.
- "Add Player" is a `MudMenu` with two items: **New player** (creates the person *and* adds them to
  this squad in one action) and **Existing player** (`SquadMemberDialog`, picking from
  `GetNonMembersAsync` — someone from an earlier season, or a guest being promoted).
- **Copy squad from {previous season}** shows only while the squad is **empty** and a previous
  season exists — copying into a populated squad would silently merge two rosters. It lives in the
  header next to "Add Player" (not in the empty-state card, which would duplicate it), preserves
  guest flags and is idempotent.
- **Mobile header actions**: `.squad-actions` becomes `column-reverse` below 700px so "Add Player"
  sits on top and copy-forward below it; the markup order is the reverse so desktop reads
  left-to-right. Both buttons, and `/games`' "Add", carry `btn-compact`, which drops the label
  below 700px and leaves the icon alone (`font-size: 0` on `.mud-button-label` — MudBlazor icons
  set their own font-size, so they don't shrink).
- Three edge states: "All seasons" selected (a card asking you to pick one — a squad belongs to
  exactly one season); an empty squad with a previous season (copy-forward offer); an empty squad
  with no previous season (plain "add your first player").
- The `GUEST` badge is driven by the **member's** flag, so the existing `.badge-guest` CSS is
  untouched. It stays visible at every width here — the `display: none` hide is in
  `Games.razor.css`, scoped to the game cards, and does not reach this table.
- The guest toggle shows **`HowToReg`** in club red (`--club-primary-bright`) for a squad player and
  **`PersonOutline`** in `--color-guest-bright` for a guest. Not a star — that reads as "favourite",
  and outline-vs-filled star is nearly indistinguishable at 20px. The colour ties the toggle to the
  `GUEST` badge beside it.
- Row actions sit in `.row-actions`, which is `display: inline-flex`. `MudMenu` wraps its trigger in
  a plain `div`, and that wrapper's inline-block baseline put the ⋮ about 6px above the icon buttons
  beside it; flex removes baselines from the equation.

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
- **Scoped CSS does not reach a MudBlazor component's root element.** A `Class` you put on a
  `MudPaper`/`MudButton` lands on markup the child component renders, which carries *its* scope
  attribute (or none), never the page's — so the rule silently does nothing and you get MudBlazor's
  default. Style plain elements in the scoped `.razor.css`; put anything targeting a MudBlazor
  component in `app.css` (see the `.live-scoreboard` / `.live-action-btn` block there).
- **A Razor comment inside a component's attribute list is parsed as an attribute name**, and
  throws `does not have a property matching the name '@* … *@'` at render time — not at build time,
  so it survives `dotnet build`. Put the comment on the line *above* the tag.
- **Never set `position` in a global `.mud-paper` rule.** `MudPopover` is a `.mud-paper`, and
  overriding its `position: absolute` turns every dropdown into a full-width band. See
  [known_issues.md](known_issues.md).
- `MudDialogProvider`, `MudSnackbarProvider`, `MudPopoverProvider` all in MainLayout
- Theme: **light mode**, club red/green from the crest. Colors are centralized as CSS
  variables — see [theming.md](theming.md). The MudBlazor palette (built by `ClubTheme`)
  is a separate C# copy that must be kept in sync with `theme.css`.
