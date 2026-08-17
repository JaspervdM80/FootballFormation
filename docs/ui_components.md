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

## Live match screen (`/games/{id}/live`)
Phone-first single column (`max-width: 560px`), no `[Authorize]`: admin drives it, everyone else
watches the same URL read-only. Every control sits in an `<AuthorizeView Roles="@AppRoles.Admin">`.

- **The heading is the opponent's name alone**, with no `vs`/`@` in front of it: the subtitle under
  it already spells the venue out in words ("Thuis" / "Uit"), and the scoreboard right below puts
  the two sides in the order the ground decides. The prefix said the same thing a third time, in
  punctuation. The other screens that show a fixture (`Home`, `FormationBuilder`,
  `FormationOverview`, `MatchResult`, `PlayerStats`) still carry it.
- **It injects four services, one per thing it does**: `LiveMatchService` to read the match,
  `MatchClockService` for the clock buttons, `MatchGoalService` for the goal dialogs and
  `MatchSubstitutionService` for the pitch taps. That is the intended shape — see
  [patterns.md](patterns.md#when-a-service-gets-long-split-it-by-use-case--not-into-layers).

- **The clock never round-trips.** A per-circuit 1-second `System.Timers.Timer` re-renders
  `Game.ElapsedSecondsAt(DateTime.UtcNow)` from the anchor the server stored, and it repaints only
  while the clock is running. See [models.md](models.md#game).
- **Spectators are pushed to via the singleton `LiveMatchNotifier`.** No service raises it by hand:
  `LiveMatchOperation` — the shape every touchline write runs inside — does it after a successful
  one, naming the game that changed. The page filters on its own `GameId`, reloads and
  `InvokeAsync(StateHasChanged)`, and unsubscribes in `Dispose`. In-process only — fine for the
  single Fly.io instance, but it needs a backplane if the app is ever scaled out.
- **`GetLiveAsync` is `AsNoTrackingWithIdentityResolution`, and it has to be.** A spectator's
  circuit keeps one scoped `AppDbContext` for its whole life, so a tracked `Game` keeps returning
  the score, clock and state from its first load while newly inserted goals appear alongside them —
  a live screen stuck at the old scoreline. Identity resolution keeps shared `Player` rows single.
- **This screen knows halves and nothing else.** A quarters game is two halves with two line-ups
  each, and the second line-up of a half is a plan the coach works through by hand on the pitch —
  nothing rolls it on, the clock never stops for it, and it is never a stage of the match here. So
  the controls are the same for both splits: "Half time" (`EndHalfAsync`) while a half is being
  played and one is still to come, then "Start 2nd half" (`StartNextHalfAsync`), and "Finish match"
  throughout. `Game.NextHalf()` is what skips the quarter left behind inside a half already played,
  so the second half opens at Q3 and Q2 is never kicked off — a line-up with no timings costs
  `GameMinutesReport` nothing, which is what leaves the whole half credited to the line-up that
  actually played it plus the substitutions made during it.
- **There is no pause.** The clock runs from kick-off until the half is whistled off, and only half
  time stops it — `PauseClockAsync`/`ResumeClockAsync` are gone from `MatchClockService` too, not
  just from the screen. A youth match is not paused at the touchline, and a clock a stray tap can
  stop is a clock the season's minutes cannot be trusted from. So a half being played always has a
  running clock, which is why the status chip's third state is half time (`.live-status-break`)
  rather than a paused one.
- **The controls are a two-column grid, laid out in `app.css`.** How many buttons the panel holds
  depends on where the match is, so equal columns keep them the same size whichever set is showing,
  and `:last-child:nth-child(odd)` spans the odd one out across the row. It has to be `app.css`:
  the children are `MudButton`s, and the old scoped `.live-control-row > *` rule never matched
  them — the classic CSS-isolation miss, and why they used not to fill the panel.
- **The clock controls sit at the foot of the page, the scoring buttons near the top.** "Half time",
  "Start 2nd half", "Finish match" and "Edit result" are pressed once each; "Goal" and "Goal against"
  are pressed all match, so the order follows how often a thumb reaches for them rather than the
  order the match runs in. Kick-off is the exception and keeps its place under the scoreboard: before
  it there is nothing else on the page to do. On a phone `.live-controls-foot` needs `order: 3` to
  stay last, because `.live-lineup` and `.live-minutes-card` are already reordered past source order.
- **The scoring buttons show only while the match is in progress.** Nothing is scored before kick-off
  or after the final whistle; a goal missed at the time is added back on `/result`, which is where a
  finished match is corrected and where the "Edit result" button leads.
- The pitch shows the half being played; at half time and after full time the last one played, and
  before kick-off the half the match opens with — so it is never blank when a lineup exists. The
  bench strip under it is always drawn.
- **Tapping a player offers two changes, one dropdown each** (`LiveSubDialog`): someone comes on for
  them (`SubstituteAsync`), or they trade positions with a team-mate who stays on
  (`SwapPositionsAsync`). Choosing in either list clears the other, so the single action button
  always has exactly one change to make and says which — "Make substitution" or "Swap positions".
  A position swap writes no `GameSubstitution`: nobody's minutes changed, and a row there would say
  they did. The price is the *split by position* — `GameMinutesReport` reads the lineup as it finally
  stands, so after a swap the whole half is credited to the position each player moved **into**
  (pinned by `A_position_change_with_no_substitution_credits_the_position_it_ended_in`). Totals are
  unaffected. Undoing a substitution therefore follows the slot rather than the recorded one: a swap
  can have moved it since, and handing the recorded slot back would seat two players in it.
  Each select's `Placeholder` is set **only** when its list is empty — MudSelect shows a
  placeholder whenever nothing is chosen, so a standing "nobody is on the bench" greets a full bench.
- **Every goal on the timeline carries the score it made it** (`ScoreProgressionReport`), in the
  scoreboard's order — home side first. It is counted forwards over the whole match and looked up
  by goal id, because the timeline itself runs newest first and a total accumulated while rendering
  would count down.
- **Events are shown as a `MatchMinute` — 35, or 35+2 in stoppage time — and ordered on the elapsed
  match clock.** The two are different scales and that is the point: the scoreboard reading stops at
  the end of the half, so a goal two minutes into first-half stoppage and one just after the restart
  both read in the thirties, while the elapsed clock runs on across the break and puts them in the
  order they happened without anyone comparing pairs. Neither kind of event stores the minute it
  displays: a goal carries `GamePeriodId` + `AtSeconds` exactly as a substitution does, and
  `MatchClockReport.MinuteOf` derives the reading from the half's own timings. The timeline, the
  result page's goal list and `ScoreProgressionReport` all sort on elapsed seconds
  (`MatchClockReport.ElapsedOf`), then `RecordedAt`, then the id. A goal typed in on `/result` has
  only a scoreboard minute, and `ElapsedOf` converts it back through the half timings rather than
  reading it as elapsed time — the two scales part company by however long a half over-ran, and
  taking one for the other puts a second-half goal under the half-time rule.
- **Half time is a dashed rule across the timeline** (`.live-event-break`), not an event. The list
  runs newest first, so it lands where the second half's entries give way to the first's;
  `MatchClockReport.HalfOf` decides which side an entry is on, from its own line-up's half or —
  for a goal typed in by hand — from which side of the second half's kick-off its clock reading
  falls. `LiveMatch.Timeline` marks the one entry it is drawn above, because the markup renders an
  entry at a time and cannot see its neighbour, and because the substitutions filter decides who
  the neighbours are.
- A **"Show substitutions" checkbox** (`.live-timeline-toggle`) drops the substitutions from the
  timeline and leaves the goals: a rotated squad buries the goals among swaps nobody is scrolling
  back for. The state is per circuit and deliberately not stored. It rides the card's heading row
  (`.live-card-head`) rather than sitting above the list, at the size of a caption — it is a setting
  for the list, not the first entry in it. The label is sized in `app.css` on
  `.mud-typography`: MudBlazor renders it as its own `body1` element, which inherits no font-size
  from the wrapper.
- Finishing asks for confirmation via `DialogPrompts.ConfirmAsync` (not `ConfirmDeleteAsync`,
  whose button says "Delete").
- **The plan for the middle of a half is a pop-up, not part of the screen.** The line-up card's
  heading carries a `Changes (n)` button (`.live-card-head`, `.live-plan-btn` in `app.css`) that
  opens `PlannedChangesDialog`; the dialog renders what `PlannedChangesReport` makes of the
  difference between the two planned line-ups (`PlannedChangesList`, which owns the `.planned-*`
  styling). Each line is carried out by tapping that player on the pitch — the dialog writes
  nothing. Admin only, like the minutes table.
  <br>A pop-up rather than a card because the plan is not the match: standing beside the live
  line-up it reads as the state of play, and a shared screen invites being asked about a change
  before it is made. Behind a button it is looked up, acted on and dismissed, and the count on the
  button says whether opening it is worth the tap. It is there before kick-off too, as something to
  read; nothing left to change means no button.
- **Only viable changes are listed.** The report is handed the substitutions already made in the
  half so it can rewind to the line-up that kicked off. A swap whose outgoing player has since
  been taken off is dropped: the difference between the line-ups still names their slot, but it now
  proposes withdrawing whoever came on for them, which nobody planned. An injury replacement
  therefore stays on for the rest of the half rather than being listed to come straight back off.
- **No line-up means no line-up card and no minutes card.** Both are left out entirely rather than
  headed over an empty pitch or an empty table — a match nobody has been picked for is sent to the
  formation screens by the buttons on `/games`, and two cards repeating "build one first" only push
  the scoreboard and the timeline down the phone screen the coach is actually reading. The heading
  is the first thing either card can say, so there is nothing to keep on screen once the body is
  empty.
- **Minutes played is admin-only** (`LiveMinutesReport`), and shows exact time on the pitch rather
  than the `periodsPlaying × periodDuration` estimate the planning screens use. It is a computed
  property, so the running player's total climbs with the clock tick. Until the first kick-off
  there is no time played at all and the figures are the planned line-up costed at a full period
  each, so the card is headed **"Planned minutes"** rather than "Minutes played"
  (`Game.HasActualTimings`) — the numbers cannot say which they are, so the heading does.
- **Mobile reorders the column with flex `order`**: what just happened matters more at a touchline
  than where everyone stands, so the line-up card (`.live-lineup`, `order: 1`) and the minutes
  table (`.live-minutes-card`, `order: 2`) drop below the timeline under 600px. Both rules live in
  `app.css` — the classes sit on `MudPaper` roots, which scoped CSS cannot reach.
- Goal and assist selects bind `int?`, not `int`: an `int` binds to 0, which is nobody's id but
  still renders as a chosen value, so the scorer field looked pre-filled.
- **`/games` is two lists, not one** (`Games.Sections()`): fixtures still to play, soonest first,
  then results in the order they were played — both ascending. A single list has to put one of
  them at the wrong end, and newest-first put the *most distant* fixture at the top. The split is
  on `HasFinalScore`, not on the date: a game stays a fixture until a result is on file. A match
  that was never played therefore sits in the fixture list after its date has passed, which is
  intended — the only thing to do with one is delete it, and the stale row is the prompt. Because
  `HasFinalScore` tests `MatchState` too, a game being played now stays among the fixtures instead
  of crossing over on its first goal. Either block disappears when empty.
- **The Live button leads the action row and is always the crest red** (`.action-live`), which no
  other action on a card wears — the leading position and the one colour are what a coach hits
  without reading. `.action-live-now` adds the pulse, and only a match actually under way carries
  it: **paint and state are separate classes**, because the first version put the red on the
  in-progress class alone and the button everyone actually sees — on the day, before kick-off —
  rendered grey.
- `/games` routes an `InProgress` game to `/live` for **everyone**, whatever the calendar says,
  since a match kicked off before midnight is still being played. For other games the Live action is
  admin-only **and match-day only** (`Games.IsMatchDay`, i.e. `game.Date.Date == Today`): the live
  screen runs a real clock and writes real substitution timings, so opening it on a fixture weeks
  out would bank minutes against a match nobody is playing. It disappears entirely once
  `Games.HasFinalScore(game)` — a settled game has nothing left to run, so the Result button is the
  way in and a row click opens `/result`.
- **A fixture in the future carries no Result button** (`Games.IsFuture`, i.e.
  `game.Date.Date > Today`). There is no result to read and none to enter, and a score typed onto a
  match nobody has played turns a fixture into a result — `Sections()` splits on the scoreline. The
  page is not the enforcement: `MatchResult` applies the same rule, so an admin who arrives at
  `/games/{id}/result` by URL gets the score read-only, no **Save Score** and no add-goal form
  (a goal is a scoreline by another route — `AddGoalAsync` recounts it), under a line saying the
  match has not been played yet.
- **The action row is a card of its own below 600px.** A game card carries four `.action-btn` icons
  on a fixture and six on match day when Live joins them, and on a touch screen those are 44px each
  — 264px, which no phone has to spare beside an opponent's name. So `Games.razor.css` wraps the
  row onto its own full-width line under the match, at a fixed width and right-aligned, flush
  against each other: a gap between two touch targets has to be nothing or at least 8px, and there
  is nowhere to find five 8px gaps. Fixed rather than split evenly because the row's length varies
  with both the game's state and who is looking — see [known_issues.md](known_issues.md). The
  card's horizontal padding drops to 12px there so the six still clear 44px on a 320px phone.
  `scripts/touch-targets.mjs` measures all of it — see [testing.md](testing.md#touch-targets).
- **The venue is a word, at every width.** A `.badge-venue.badge-venue-inline` badge trails the
  opponent's name and spells out *THUIS*/*UIT* — the same badge the player statistics use, so the
  two screens say it the same way. The card's coloured edge stripe says the same thing and had been
  saying it alone, which is a convention nobody reads off a stripe.
- The page reads "today" from the injected `TimeProvider`, not `DateTime.Today`, the same way the
  services do — that is also what `IsIncomplete` (the missing-lineup flag) compares against.
- `HasFinalScore` checks `MatchState` **as well as** the score fields, and must: `MatchGoalService`
  writes `ScoreHome`/`ScoreAway` on every goal, so a score alone only means the game has started.
  Testing the score by itself would hide the Live button on the very match being played.

## Live banner on the home page
`Home.razor` calls `LiveMatchService.GetTodaysMatchAsync`, which returns a match in progress if
there is one and otherwise today's fixture — so the banner has three forms: `.home-live-banner`
for a match being played (opponent, live score, tap through to `/games/{id}/live`),
`home-banner-upcoming` before kick-off and `home-banner-done` after full time. It is visible to
everyone, since the people most likely to land on the home page on match day are spectators.

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
- Both position badges are sized for the widest abbreviation (`min-width: 3.25rem`, centred),
  so `GK` and `CDM` are the same box and the alternatives line up under the preferred one.
  Scoped to `.players-table`: elsewhere those two badge classes carry words.
- The row actions are the same box as a game card's `.action-btn` — 32px for a pointer, 44px
  and flush for a finger. MudBlazor sizes a small icon button from its padding, which lands
  at 26px, well under the touch floor.
- Sorting is unavailable on mobile — MudBlazor collapses the header to zero height in card
  mode, and its small-devices sort select is hidden (see `known_issues.md`, "the sort select
  that appears on the second render"). Sorting stays a desktop affordance.

## A page stops reading when the visitor leaves (`Components/CancellableComponent.cs`)
Blazor Server gives a component no request lifetime of its own. A page that starts a query in
`OnInitializedAsync` and is then navigated away from leaves that query running against the SQLite
volume with nobody left to render it — and on the phone-on-a-bad-connection this app is built for,
circuits drop constantly.

`CancellableComponent` is the seam: it owns a `CancellationTokenSource` cancelled on disposal and
exposes `Cancellation`. Every page or dialog that reads inherits it (`SeasonAwarePage` does, so its
four pages get it for free), and the token goes on every service **read**:

```csharp
// .razor — the base class goes here, never on the partial class (CS0263)
@inherits CancellableComponent

// .razor.cs
var result = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
_games = Snackbar.ReportFailure(L, result) ? result.Value : [];
```

- **Writes deliberately get no token.** An admin who taps "finish match" and then loses their
  circuit must still have finished the match. See [patterns.md](patterns.md#cancellation-the-third-outcome).
- **The `ReportFailure` line does not change.** A cancelled read comes back as
  `Result.IsCancelled`, and `UiFeedback` keeps quiet about one — otherwise leaving a page would
  raise "Failed to load games" on the page the visitor went to, since the snackbar belongs to the
  circuit rather than to the page that made the call.
- **What a caller must add is a check before anything the visitor would notice — a redirect above
  all.** `if (result.IsCancelled) return;` goes ahead of the not-found branch on `/games/{id}/result`,
  `/games/{id}/formation`, `/games/{id}/overview` and `/players/{id}/stats`. Without it, abandoning
  one of those loads bounces the visitor off whichever page they had just navigated to.
- **Overriding `Dispose` means calling `base.Dispose()`** — `Home` and `LiveMatch` both do, to
  unsubscribe from `LiveMatchNotifier` (and to stop the clock timer) before the base cancels.

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
- **Route allowlist**, now `AppNav.IsSeasonAware` rather than a copy of the route list living here:
  `/games`, `/stats`, `/players` (the squad is per season), `/players/{id}/stats`, and `/` — the
  start page filters nothing, but it is where a visit begins, so the season can be set before
  navigating. Hidden on `/settings`, where it would be misleading while the season list itself is
  edited, and on the single-game routes, where it is inert. The component subscribes to
  `NavigationManager.LocationChanged` so visibility follows navigation.
- Selecting a season **never** writes `Season.IsCurrent` — the picker is reachable by anonymous
  visitors, and `IsCurrent` is shared state owned by the admin on `/settings`. It goes in a cookie
  for eight hours instead, which is per-browser, which is the scope a view choice belongs at — see
  [patterns.md](patterns.md#ui-state-services).

## Navigation: routes, menu, page headers
Everything that knows a URL lives in `UI/Navigation/`. Three rules, and the whole thing holds:

1. **Build URLs from `AppRoutes`** — `AppRoutes.Games`, `AppRoutes.PlayerStats(id)` — in pages,
   in `Href` attributes, everywhere. Not an interpolated literal. The `@page` directives are the
   one exception: Razor needs a compile-time constant, so `AppRoutes` mirrors them by hand.
2. **A page's name lives once**, in `AppNav.PageNameKey` — a localization key, matched by pattern
   on the path segments. It names the menu entries *and* fills in "Back to {0}", so a page is
   called the same thing wherever it is referred to. It returns `null` for anything outside the
   app's routes (`/login`, `/not-found`), which is how the back arrow knows not to offer it.
3. **The menu is `AppNav.Menu`**, rendered by `<NavItems />` in both the app bar and the drawer
   (`ShowIcons="true"` there). Adding an item is one line. There is deliberately **no Start item**:
   the "GJS Meiden" title is already a home link in both places.

### PageHeader (`Components/PageHeader.razor`)
Every page opens with one — heading, optional subtitle, optional back arrow, optional actions.
Do not hand-roll a header row.

- `Title` / `TitleContent` and `Subtitle` / `SubtitleContent`: string for plain text, fragment when
  the page needs its own markup. **A fragment is compiled into the calling page**, so that page's
  scoped CSS reaches inside it. Anything on an element *PageHeader* renders — the wrapper, the
  heading — needs a rule in `app.css`; that is what `Class` and `TitleClass` are for, and why
  `.result-header`, `.result-opponent`, `.result-subtitle` and `.builder-opponent` moved there.
- `Meta` sits beside the heading (the formation builder's venue/date/formation badges); `Actions`
  is pushed right by a `MudSpacer` (the Add button on `/games`, the squad actions on `/players`).
- `TitleTypo` is `h4` on top-level pages, `h5`/`h6` on detail pages. `Class` carries the page's
  bottom margin — the existing `mb-2`/`mb-3`/`mb-4`/`mb-6` were kept as they were, so unifying
  the vertical rhythm is still an open, separate job.
- `<PageTitle>` stays on the page. Several deliberately differ from the heading (`/players` is
  titled "Players" but headed "Squad"), and on the detail pages it sits above the loading guard so
  the browser tab is right before the data arrives.

### Back arrow (`Components/BackButton.razor`)
Rendered by `PageHeader` when you pass `BackFallback`. It returns to the page the visitor **came
from** — `NavigationTrail`, see [patterns.md](patterns.md#ui-state-services) — and names that
destination in `aria-label` + `title` ("Terug naar Seizoen"). Icon-only by design; the tooltip is
the whole affordance, which is why the label and the destination are resolved from one expression.

`BackFallback` is only used when there is nothing behind: a shared link, a bookmark, a refresh.
Pick the page someone landing cold most likely wants — `/players` for player stats, `/games` for
the game screens, and for `/games/{id}/overview` the editor for an admin, `/games` for a visitor.

## Squad page (`/players`)
Season-scoped: it follows the season picker and shows that season's squad, not everyone on file.

- Row actions (admin only): **remove from squad** (person-remove) and an overflow `MudMenu` holding
  "Edit player", "Archive player" / "Restore player", and "Delete player permanently". Removing from
  a squad is the everyday action; the two that act on the *person* are demoted out of the icon row,
  with archive listed above delete because it is what is almost always meant.
- **Guest status is a switch in the Edit Player dialog, not an icon on the row.** The `GUEST` badge
  already states it, and a toggle next to a badge saying the same thing is two controls for one
  fact. `PlayerDialog` therefore returns a `PlayerEdit(Player, IsGuest)` record rather than a
  `Player`: the person and the membership are two writes, and the page only makes the second one
  when the switch moved, so renaming someone never touches the squad. The switch is seeded from the
  row's flag, is separated from the person's fields by a divider, and names the season under it —
  the flag belongs to one season's squad, not to the person.
- **Archive vs delete.** Archiving retires someone who has left the club: nothing they are in
  changes, they simply stop being offered for seasons still to come (see
  [models.md](models.md#archiving-and-why-deleting-is-guarded)). Its confirm says so; the delete
  confirm now spells out what would be lost and points at archiving, instead of a bare "are you
  sure" about a cascade nobody can see. `PlayerService.DeleteAsync` refuses anyway once the player
  has played, so the dialog is the explanation and the service is the guard.
- An archived player keeps their place in the squads of the seasons they played and is badged
  **`ARCHIVED`** (`.badge-archived` — the quietest badge on the row; it states an absence rather
  than warning about one). That badge is also the only place the restore action lives, so switching
  the season picker to a season they played is how you find them again.
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
- Row actions sit in `.row-actions`, which is `display: inline-flex`. `MudMenu` wraps its trigger in
  a plain `div`, and that wrapper's inline-block baseline put the ⋮ about 6px above the icon buttons
  beside it; flex removes baselines from the equation.

## Statistics screens (`/stats`, `/players/{id}/stats`)
Both are public reads, and both hold minute figures back from a visitor — the rule and its one
exception are in [patterns.md](patterns.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).
How each page implements it differs, and the difference is the point:

- **`SeasonStats` uses `<AuthorizeView Roles="@AppRoles.Admin">`** around the whole Playing-time
  card. One gate, markup only, nothing else on the page depends on who is looking — so the default
  mechanism is the right one and the code-behind stays unaware.
- **`PlayerStats` uses an `_isAdmin` flag** read in `OnInitializedCoreAsync` (**not**
  `OnInitializedAsync` — `SeasonAwarePage` owns that). It has to: the per-game table is a CSS grid,
  and hiding its minutes cell means dropping a *track* as well, which is a class on the container
  that no `AuthorizeView` inside the rows could set. Hence `.game-list-no-minutes` and, for the same
  reason one tile up, `.stat-tiles-3` — `.stat-tiles` is a fixed `repeat(4, 1fr)`, so three tiles
  would otherwise leave a dead fourth column. Both variants live in `app.css` beside the base rule,
  and `.stat-tiles-3` is repeated inside the `max-width: 760px` block because a two-class selector
  outranks the one-class phone rule that would otherwise narrow it.

The two mechanisms are not quite equivalent, and the difference shows up exactly once: an
`AuthorizeView` re-renders on `AuthenticationStateChanged`, while `_isAdmin` is read once in
`OnInitializedCoreAsync`. An admin whose account is revoked mid-session loses the `/stats` card
immediately and keeps the player page's minutes until they navigate. That is the same trade
`Games` and `MatchResult` already make, and the circuit's own revalidation ends the session shortly
after regardless — but it is why the flag is not the default choice.

**The Playing-time bar is filled to the player's own available minutes** (`PlayerStats.Utilization`
— on-pitch minutes over the played duration of every match they were in the roster for), and the
list is ordered by that share. It used to be filled relative to the busiest player, which made the
top of the table 100% by definition and read as a ranking of who plays most rather than of who is
being rotated fairly. `AvailableMinutes` already excludes matches a player was unavailable for, so
someone who missed half the season is not punished for it. The inline `width:` needs
`InvariantCulture`, like every other percentage in this app — a Dutch decimal comma kills the bar
silently.

**The per-game rows carry the `.badge-venue` pill**, the same THUIS/UIT pill the formation builder's
header and the games list use, rather than the `vs` / `@` prefix the rest of the app still uses in
running text. The row is a list of fixtures where the venue is a fact about each one, not a sentence
about a single match — a badge reads down it, a prefix does not. It **trails** the opponent, because
the name is what someone scans the column for and a leading badge makes every row start with the
same two words. `.badge-venue-inline` adds the 8px lead and takes the pill under the name's own
height — at header size the box is taller than the text it annotates. `.g-opp-name` is a flex row
so the pill keeps its width and `.g-opp-text` gives way to the ellipsis.

## Dialogs on a phone (`.dialog-sheet`)
Every dialog goes through `DialogPrompts` with `UiFeedback.LockedDialog` (no backdrop-click close).
A **long form** additionally carries `Class="dialog-sheet"` on its `MudDialog` — currently only
`GameDialog`, the new/edit match form, which is the app's longest and is filled in at a touchline
on a phone in portrait.

The class does nothing above 600px; the rules are in `app.css` behind a media query. Below it the
dialog becomes a full-screen sheet:

- **Full width.** MudBlazor's `calc(100% - 64px)` leaves a 360px phone 296px, which is narrower
  than the 310px date picker. See [known_issues.md](known_issues.md).
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
[testing.md](testing.md#touch-targets). Changing one of them means changing what the guard measures,
not just what this page says.

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
