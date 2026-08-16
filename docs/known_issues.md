# Known Issues & Past Fixes

Avoid repeating these mistakes:

## EF Core
- **UNIQUE constraint on save**: When re-saving `GamePlayerPosition` entities, always create NEW entities with `Id = 0`. Never re-add tracked entities with existing IDs — EF tries INSERT with the old PK.
- **List value converters need ValueComparer**: Without it, EF won't detect changes to `List<PlayerPosition>` or `List<int>` properties.
- **DB path must be absolute** — a relative path resolves against the working directory, which
  changes. `APP_DATA_DIR` is the supported override.
- **`ORDER BY` on a date sorts its text, not the date**: SQLite has no date type, so all eight
  `DateTime` columns in this schema (`Game.Date`, `Game.ClockRunningSince`, `Season.StartDate`,
  `Season.EndDate`, `GameComment.CreatedAt`/`EditedAt`, `GameGoal.RecordedAt`,
  `GameSubstitution.RecordedAt`) are TEXT, and an `ORDER BY` or a `<`/`>` in the query compares the
  string the value was written as. That matches date order only while every row carries
  byte-identical formatting — one written with an ISO `T` separator instead of EF's space (a
  restored backup, a value written by anything but this app) sorts as if the `T` were part of the
  time, because `'T'` > `' '`. **Sort and compare dates after materialising the rows**, where the
  parsed `DateTime` is what gets compared: `GameOrdering` (`Models/Game.cs`) and `SeasonOrdering`
  (`Models/Season.cs`) do it with the tie-break spelled out, and `SeasonService` reads the whole
  season table and does its window arithmetic in memory for the same reason — which is also what
  lets `Season.Contains` be the single date-only definition of a window. `GameOrderingTests` and
  `SeasonOrderingTests` pin it. The one deliberate exception is `LiveMatchService`'s
  `Date >= today && Date < tomorrow`, a same-day range kept in SQL so the games table is not
  loaded whole on every home-page hit.
  **The rule is now mechanical, because prose could not hold it.** `DateInSqlInterceptor`, wired
  into `ServiceTestBase`'s context factory, reads the SQL of every query the suite runs and throws
  on a date column in an `ORDER BY` or an inequality — so a new query that reintroduces this fails
  whichever test first executes it, rather than sorting almost-right until a backup is restored.
  Its column list comes from the EF model, so it covers a date property from the moment it is
  mapped. The exception opts out by name with `.TagWith(QueryTags.ComparesDatesInSql)`, which is
  the only way past and is meant to be argued for.
  Two things it does not catch: SQL no test ever executes (nothing watches a path the suite does
  not walk), and equality on a date. `=` compares text just as fragilely, but an `UPDATE ... SET
  "Date" = @p0` is an assignment wearing the same syntax, so flagging the operator would fail every
  write. Inequality and `ORDER BY` are unambiguous, which is why the guard stops there.
- **Sorting backup filenames as text is a different case, and it is fine**: `DatabaseSafety` names
  backups `pre-migration-<last applied migration>.db` and prunes by `OrderByDescending(f => f.Name)`.
  A migration id begins with the fixed-width timestamp it was scaffolded at, so lexicographic *is*
  chronological — the sort is on text by design, not by accident. (The name used to be a timestamp
  of the moment the copy was taken; it is the schema state now, so a crash loop cannot write five
  snapshots of the broken database and prune the only good one. See [deployment.md](deployment.md).)
- **The scaffolder ordered a destructive migration wrongly**: `AddSeasonSquads` had to copy `Players.IsGuest` into a new table *and* drop the column; EF emitted the `DropColumn` first, which would have wiped the source before the backfill ran. Always read and reorder the generated `Up()`.
- **The one migration on file carries an id older than the file**: `Migrations/` holds a single
  `20260322100416_InitialCreate` that the twenty real migrations were folded into, and it keeps
  the original `InitialCreate`'s id rather than the timestamp of the scaffold that wrote it. That is
  load-bearing, not untidiness. The live volume already has that row in `__EFMigrationsHistory`, so
  it boots with nothing pending; a rescaffold that let EF assign a fresh id would make the whole
  schema pending against a database that has it all, and the boot would `CREATE TABLE` over a season
  of results and fail. Restore the id by hand in both file names and the `[Migration]` attribute —
  see [patterns.md](patterns.md#migrations-are-one-file).
- **A transaction cannot span two `AppDbContext` instances, and nothing warns you**: each operation
  opens its own context from the factory, so calling another *service's* write from inside your own
  gives two transactions with a gap between them — even though the code reads like one operation and
  every `Result` check passes. Logging a goal was shaped that way and an interruption left the goal
  on file behind a stale score. **When one row is derived from another, write both through the same
  context and commit them once.** The rule, the worked example and the two rejected alternatives are
  in [patterns.md](patterns.md#when-two-rows-have-to-agree-one-context-writes-both).
- **Collapsing that into a single `SaveChanges` looks tidier and reintroduces a lost update**:
  counting the goals in memory and adding the new one is a read-modify-write on a row with no
  concurrency token, so two admins on the same live match each write a scoreline of *n+1*.
  **Recount after the write, inside the transaction**, where SQLite's write lock has already
  serialised the two.

## Data / domain
- **Deleting a player used to be destructive across every season**: `PlayerService.DeleteAsync` cascades their `GamePlayerPosition` rows and nulls their `GameGoal` scorer, so last season's top scorer disappeared from last season's stats — from a confirm that said nothing about it. Fixed by `ArchivePlayersInsteadOfDeleting`: delete now **refuses** for anyone with a lineup or goal row anywhere, and `Player.IsArchived` is the way to retire someone. Worth knowing when the refusal surprises you: the counts are deliberately **not** scoped to a season, unlike `SeasonSquadService.RemoveMemberAsync`'s, because the cascade is not either.
- **A whole-second match clock ties, and a strict `>` calls both rows the newest**:
  `GameSubstitution.AtSeconds` counts whole seconds, and a double substitution is two taps in a row
  on the touchline, so two rows sharing a second is the normal case, not the edge case.
  `RemoveSubstitutionAsync` guarded "only the most recent one can be undone" with
  `s.AtSeconds > sub.AtSeconds` alone, which both rows of such a pair passed — undoing the earlier
  one restored the player it took off into a slot the later one had already given away, leaving two
  players on the same slot and a timeline naming someone who was not on the pitch. The guard now
  tie-breaks on `s.Id`, which is monotonic (the column is `AUTOINCREMENT`, so a rowid is never
  reused), and `GameMinutesReport` orders the substitutions it rewinds the same way. So does
  `LiveMatch.Timeline` — all three have to agree, or the entry the admin sees on top is not the one
  whose Undo is allowed. **`RecordedAt` is not the tie-break to reach for**: two changes entered in
  one instant share it, and rows written before the column existed all default to `0001-01-01`.
  Every test that predated this advanced `FakeTimeProvider` between substitutions, which is why it
  went unseen — the two that pin it now deliberately do not.
- **A period length in minutes silently loses the remainder**: `Game.PeriodDurationMinutes` used to
  be `GameDurationMinutes / PeriodCount` in `int`, and every planned-minutes figure multiplied that
  truncated number back by 60. A 50 minute match in quarters became 4 × 12, so the dialog offered
  48 minutes of a 50 minute match, the playing-time table planned everyone 4 minutes short, and the
  builder's caption disagreed with the duration printed next to it. The fix is the rule to keep:
  **period length is carried in seconds** (`Game.PeriodDurationSeconds`), which is always exact
  because 60 divides by every period count there is, and the minutes form is a `decimal` for
  display only. `MatchClockReport` already worked in seconds for exactly this reason — it just did
  the division itself instead of asking the model. Reach for `PeriodDurationSeconds` in any new
  arithmetic; `PeriodDurationMinutes` only ever goes on screen.
- **Archiving is a filter on the future, not on the past**: only the "add existing player" picker and copy-forward look at `IsArchived`. `PlayerService.GetAllAsync` deliberately still returns archived players — it is the id → name lookup the match report and live screen resolve against, so filtering it would blank a scorer out of a game they scored in, which is the very thing archiving exists to prevent. Same reasoning for `Game.IsInRoster`: a past game has to be judged the way it was played. If a picker ever *should* hide them, filter at that call site, not in the lookup.

## Blazor / MudBlazor 9.x
- **Dialogs not showing**: `MudDialogProvider` must be inside an interactive render mode. Fixed by setting `@rendermode="InteractiveServer"` on both `<Routes>` and `<HeadOutlet>` in App.razor.
- **`Position` enum ambiguity**: Renamed to `PlayerPosition` because `MudBlazor.Position` exists.
- **`MudForm.Validate()` is obsolete**: Use `ValidateAsync()`.
- **`ShowMessageBox` removed**: Use custom `ConfirmDialog` component instead.
- **Multi-select binding**: Use `IReadOnlyCollection<T>` not `IEnumerable<T>`.
- **`RenderFragment` in code-behind**: Use `=> __builder =>` lambda pattern in `@code` block; can't use regular methods.
- **Dropdowns rendered as a full-width band across the page**: `MudPopover` carries `.mud-paper`, and app.css's card rule set `position: relative` on it — same specificity as MudBlazor's `.mud-popover{position:absolute}` but later in source order, so it won. A relatively positioned block fills the popover provider's width and treats the placement JS's `left`/`top` as an offset from its static spot at the top of the page. Fixed twice over: first by patch rules putting `position` back, and now — the current state — by the card rule never claiming a popover in the first place, `.mud-paper:not(.mud-popover):not(.mud-dialog)` in app.css. The patch rules are gone, so MudBlazor's own positioning is never disturbed and there is nothing left to restore. Watch for this whenever a global `.mud-*` rule touches layout: excluding the popover beats overriding it back.
- **An open dialog locks the page by shrinking `<body>`, which breaks any geometry you measure from
  the DOM.** MudBlazor adds `scroll-locked-no-padding` — `overflow: hidden` on a `<body>` whose box
  is then *shorter than the viewport*. Walk an element's ancestors intersecting every clipping box,
  the obvious way to work out what is actually on screen, and the answer is that every dialog and
  popover in the app is half scrolled out of view. It is not: an ancestor's overflow only clips a
  descendant it is a containing block for, and MudBlazor's dialog container is `position: fixed`, so
  a plain `overflow: hidden` on `<body>` cannot clip it. `scripts/touch-targets.mjs` carries that
  rule (`containsFor`); it was worth about an hour of believing the buttons were off screen.
- **The date picker's month header slides, so it reads the month you just left.** The element is a
  `.mud-picker-slide-transition`, and for a moment after *Previous month* it still reports the old
  text. A loop that clicks and then immediately re-reads therefore spends a second click on a month
  it had already stepped past — `pickEarlierThisMonth` walked to July while asserting August, on a
  loaded CI runner only. Wait for the header text to *change* after each click before reading it
  again, and choose the direction from that settled value so an overshoot walks back instead of
  spiralling. Anything that steps a MudBlazor picker has this shape.
- **A table's small-devices sort select only appears on the second render, so a phone gets an empty
  dropdown out of nowhere.** `MudTable` renders `.mud-table-smalldevices-sortselect` from the sort
  labels its header registered, and they register *during* the first render — so the select is
  absent on load and present after anything re-renders the table (on `/players`, marking a player a
  guest is enough). It arrives unlabelled, because nothing on this table sets `SortLabel`, which is
  what made it look like a stray control rather than a sort box. Two consequences: `app.css` hides
  it on `/players`, and **a first-load screenshot is not evidence that it is gone** — re-render the
  table before you look, or read `getComputedStyle` on the element rather than trusting the picture.
- **`MudMenu`'s `Class` lands on the root wrapper, not the activator button**: `Class="btn-gold"` painted an invisible `div` while the button kept MudBlazor's default filled colours. There is no `ActivatorClass` parameter in 9.7 — style `.<your-class>.mud-menu .mud-button-root` instead (see `.btn-gold.mud-menu` in app.css, and `SeasonPicker`'s `.season-picker .mud-button-root`).

## Touch / PWA
- **A MudBlazor dialog is 64px narrower than the phone, and that breaks the date picker.**
  `.mud-dialog-width-full` is `calc(100% - 64px)`, so a 360px phone gets 296px of dialog and a
  320px one gets 256px. `MudDatePicker`'s popover has `min-width: 310px` and is anchored to the
  input's left edge, so it hung 9px off a 360px screen and **49px off a 320px one — the whole
  Saturday column was untappable**. Two rules in app.css fix it: `.dialog-sheet` makes a long form
  dialog full-screen below 600px, and the `.mud-picker-popover` block centres the calendar on the
  viewport instead of anchoring it. Give the popover an **explicit** width, never just a
  `max-width`: `.mud-picker-calendar` is a wrapping flex row whose min-content width is one 40px
  day cell, so a shrink-to-fit popover collapses the month into a single column.
- **Centring the calendar was only half of it — the day cells are 36px, and that is too small.**
  Reported twice: after the popover was centred, picking a day on a phone still misfired. MudBlazor's
  `.mud-day` is a 36px circle on a 40px column pitch, under Apple's 44px *and* Android's 48dp, and
  the 4px gutters between columns are **dead** — a tap landing there hits `.mud-picker-calendar` and
  does nothing. Worse, the popover stayed pinned to 310px on a sheet that is now the phone's full
  width, leaving live form controls exposed either side: with the picker open, `elementFromPoint`
  14px outside the calendar returned the season `MudSelect` underneath. `--dp-day` spends that width
  instead and drops the side margins so the column pitch *is* the target. The resulting sizes per
  viewport, and the elements that have to scale with them, are tabulated in
  [ui_components.md](ui_components.md#date-pickers-below-600px-or-560px-tall).
- **The month name between the arrows is a 23px button, and it was the worst target of the lot.**
  Reported as "it's mostly the month selection". It is a real `<button>` — the one that opens the
  month grid — that MudBlazor clamps to `height: 23px` because it doubles as the slide transition's
  viewport, sitting in the 56px row the two 44px arrows set. So there is **17px of dead div above it
  and 16px below**, and a thumb aimed at "augustus 2026" mostly lands in one of those. 44px fits that
  row with no layout change at all.
- **Buttons need clear space above them, not just their own size.** The game dialog's action row
  sat 28px under the last field, and `MudSelect`'s hit box reaches ~10px past its own underline —
  leaving 18px of dead space between "Annuleren" and the unavailable-players dropdown. Mobile
  browsers snap a tap that misses every target to the nearest one *by contact area*, and the
  select is the far bigger target, so a thumb aimed at the button opened the dropdown instead.
  This is invisible to `document.elementFromPoint`, which reports the button as reachable — the
  measurement that finds it is the **gap** to the nearest interactive element above.
- **All of the above is now measured, and the measuring found three more.** Everything in this
  section was CSS that nothing verified — a MudBlazor upgrade or one more global `.mud-*` rule would
  have undone any of it silently, which is how the day cells shipped twice. `scripts/visual-check.sh`
  now reopens the match dialog and its date picker at 320x568, 360x640 and 844x390 and fails on a
  target under 44x44 or a gap that is neither zero nor at least 8px (see
  [testing.md](testing.md#touch-targets)). Its first run reported: a **landscape phone got 36.5px
  action buttons**, because the `.dialog-sheet` block is width-only and 844px is not below 600px —
  the same geometry that was reported for "Annuleren" in the first place; **6px of dead space** on
  either side of the picker's month name, from MudBlazor's `margin: 6px` on the arrows, in the row
  this file already calls the worst of the picker; and a numeric field's **24x16 steppers**, stacked
  flush so a tap that misses one hits the other and steps the wrong way. All three are fixed in
  `app.css`. The lesson is the ordering: three fixes had been argued for in prose here for months,
  and the first thing that measured them found three more in an afternoon.
- **Widening that guard to `/games` found a card that had already run out of room.** The action
  buttons on a game card were 40px under `(pointer: coarse)`, set deliberately and with a comment
  saying a finger needs more than the 32px a mouse gets — four short of the floor, and separated by
  2px gutters. Size and clearance are the two rules, so both were flagged the moment the page came
  into scope. What the numbers did not say and the screenshot did is that the row had **already
  overflowed**: five 40px buttons and their gaps left 42px of a 320px card for the opponent's name,
  which wrapped underneath them and was drawn straight through them. And five is not the count —
  the Live button joins on the day of the match, and six 44px buttons are 264px, more than any
  phone can put beside anything at all. So below 600px the card is two lines: the match, then the
  actions on a line of their own, flush, at a fixed width and pushed to the card's right-hand edge
  (`Games.razor.css`). 44px each, which is why the card's horizontal padding drops from 16px to 12px
  there — 16px leaves 260px and six buttons need 264, while 12px leaves 268px.
- **Splitting that row evenly made a button's width a function of the row's length.** It was
  `flex: 1` at first, so nothing was narrower than it had to be. But the row is four buttons on a
  fixture, five on a played match, six on match day and *one* on a card an anonymous visitor is
  looking at — so the same action sat somewhere different on every card in the list, and the
  visitor's lone Overview button rendered as a 312px bar that reads as a call to action rather than
  an icon. Fixed width and right-aligned instead: Delete is under the same thumb position on every
  card, and every other button counts in from the same edge.
  **The audit only ever sees six because `visual-check.mjs` seeds its game dated *today*.** Move
  that date and the widest row the app has quietly stops being measured, with nothing failing to
  say so.
- **A width-only media query does not cover a phone.** Turned sideways, a 390px-tall phone is 844px
  wide and every `max-width: 599.98px` rule stops applying — while the thumb does not change size.
  The picker block keys off `(max-width: 599.98px), (max-height: 559.98px)` for exactly this reason,
  and anything about touch rather than layout belongs in that query, not the sheet's.
- **Do not leave two nested scroll containers in a dialog.** MudBlazor makes both `.mud-dialog`
  and `.mud-dialog-content` scrollable. A flick can move either one, and on iOS a tap landing
  during momentum scrolling resolves against wherever the other has since moved to. `.dialog-sheet`
  sets `overflow: hidden` on the dialog so the content is the only scroller.
- **`DialogOptions` has no `CssClass` in MudBlazor 9.7** — but `MudDialog`'s own `Class` lands
  straight on the `.mud-dialog` element, which is how `.dialog-sheet` gets there. No plumbing
  through `DialogPrompts` is needed.
- **Blazor silently drops drag events with null `dataTransfer`**: dispatching
  `new DragEvent('dragstart', {bubbles: true})` reaches DOM listeners but never the Blazor
  handler — its DragEventArgs serializer reads `dataTransfer.files/items/types` and gives up
  on null. Always attach `new DataTransfer()` (or a stub with those fields). Plain `Event`
  objects with a drag type name are ignored entirely. Cost hours; see `js/drag-drop-touch.js`.
- **HTML5 drag events never fire from touch input**: iOS Safari and Android Chrome require the
  shim in `wwwroot/js/drag-drop-touch.js`, plus `touch-action: none` on `[draggable="true"]`
  (in app.css) so the browser doesn't claim the gesture for scrolling.

- **White page after switching apps**: a suspended PWA loses its SignalR circuit. Two
  causes, both fixed: the page background came only from the MudBlazor theme (now also
  set statically on `html, body` in app.css via `var(--surface-page)`, plus
  `color-scheme: light`), and Blazor's stock reconnect overlay is light (now themed via
  `#components-reconnect-modal`, and `js/pwa.js` reloads the page once reconnection fails
  or on return to a dead tab).
  **The reload needs its own guard, or it is the next bug.** A page that serves while the circuit
  never connects — a blocked WebSocket, a dead network — would reload forever. `pwa.js` stamps
  `sessionStorage` and refuses to reload twice inside ten seconds, leaving the overlay up instead.
- **"Reconnecting..." for ten seconds on a circuit that was never gone.** Reported from the
  touchline: start a match, switch to another app, come back, and the overlay sits there. Nothing in
  this app was waiting — it is Blazor's default retry schedule meeting a phone that has just woken.
  The defaults (read them in `blazor.web.js`, they are not in the docs) are `maxRetries: 30` and
  `attempt < 10 ? 0 : attempt < 20 ? 5000 : 30000`: **the first ten attempts have no delay between
  them at all**. A phone coming out of suspension has no network for the first moment, so all ten
  are spent and failed in a fraction of a second, and every real reconnect therefore starts from
  the five-second bucket. One or two of those is the ten seconds. Blazor does cut a pending wait
  short on `visibilitychange`, which does not help here — the retries only start once you are
  already looking at the page. Fixed by the `Blazor.start` call in `App.razor` — one immediate
  attempt, then one a second — which is why that file loads `blazor.web.js` with
  `autostart="false"`: the schedule cannot be set any other way. **Keep that call inline.** In a
  file under `js/` it becomes a second thing the app's whole interactivity depends on arriving,
  and one that fails silently, exactly like the empty `blazor.web.js` two entries down.
  While you are in there: the retry callback is documented as receiving `maxRetries` as a second
  argument and is called with one, so a guard reading it is dead code — the retry loop applies the
  cap. And whatever the schedule is, keep its total near the retention period below. Blazor giving
  up is what marks the dialog failed, and a failed dialog is `pwa.js` reloading a page whose
  network may still be down — onto the browser's error page, where nothing retries at all.
- **Past the retention period there is no reconnect at all — it is a reload.** `ConnectCircuit`
  returns false for a circuit the server has already evicted, and with no persisted circuit state
  configured the client goes straight to the rejected state, i.e. a fresh page. The window is
  `CircuitOptions.DisconnectedCircuitRetentionPeriod`, three minutes by default and ten here, which
  is the difference between rejoining a running match screen and reloading it. Two things fall
  outside it whatever the setting, and both always cost a reload: a machine Fly has scaled to zero
  (a restarted process has no circuits), and a deploy. Retention is bounded by
  `DisconnectedCircuitMaxRetained` as well as by time, and **lowering that to buy back the memory a
  longer window costs is a trap**: a slot given up is a circuit evicted early, and the coach's is as
  likely as anyone's. It stays at the stock 100. Little occupies it in practice — a tab closed
  properly sends a disconnect beacon and gives its circuit up immediately, so only unclean
  disconnects park at all.
- **Chromium's emulated `(pointer: coarse)` is easy to lose, and then a "phone" capture is the
  pointer layout at phone width.** Two ways it goes, both measured while screenshotting `/players`:
  opening the mobile context in a browser that already holds a desktop one, and taking a `fullPage`
  screenshot — after which `matchMedia('(pointer: coarse)')` reads false and every touch-sized rule
  is off, so 44px targets measure 32. Give the phone pass its own `chromium.launch`, and give it a
  viewport tall enough that the capture needs no `fullPage`. `scripts/touch-targets.mjs` is safe on
  both counts (its own contexts, no screenshots), which is why this only shows up in ad-hoc scripts.

## Localization
- **Resource keys are English text, so watch for homographs**: "Home" was already the
  venue label ("Thuis") when the nav needed a home link — the nav uses the key "Start"
  instead. Resx names are also case-insensitive, so no "SUB"/"Sub" pairs.
- **Case-insensitivity bites the service action phrases**: `ServiceOperation`'s actions are
  lowercase verb phrases ("delete game"), and several collided with existing capitalized button
  labels ("Delete Game"). MSBuild warns `MSB3568: Duplicate resource name ... ignored` and the
  first entry silently wins. Reuse the existing key rather than adding a lowercase twin — or, when
  the phrase has to differ because it is substituted into a sentence ("archive the player" beside
  the menu item "Archive player"), word it so the two are not the same key.
  **This one now fails the build** — `Directory.Build.props` promotes MSB3568 to an error. The trap
  worth remembering is *why it took a second property*: `TreatWarningsAsErrors` is a compiler
  property and does not touch `MSB####` codes, so a duplicate key warned and built green even in
  Release, where every other warning is fatal. Promoting an MSBuild-engine warning needs
  `MSBuildWarningsAsErrors`. It is set unconditionally, so Debug catches it too.
  One limit: `GenerateResource` is incremental, so the check only runs when a resx has actually
  changed — which is when a duplicate arrives, and CI builds cold regardless.

## Blazor components
- **`section` is a reserved word in a `.razor` file.** `@foreach (var section in Sections())` then
  `@section.Title` is parsed as the `@section` *directive*, not as a member access, and the build
  fails with `RZ2005: The 'section' directive must appear at the start of the line`. Name the
  variable anything else (`gameList` on `/games`), or parenthesise as `@(section.Title)`.
  **This one is SDK-dependent, which is the real trap:** it fails on the 10.0.110 SDK from Ubuntu's
  archive — the one `.claude/hooks/session-start.sh` installs — and compiled clean on the SDK
  `actions/setup-dotnet` resolved for `10.0.x` in CI, so a green check was not proof it built in a
  web session. `global.json` now pins 10.0.110 and `ci.yml` installs from that file, so the two
  agree. The gap was wider than a patch: `10.0.x` resolved to **10.0.302**, a different feature
  band. `rollForward` is `disable` on purpose — anything looser picks the *highest* installed 10.x,
  so a runner that preinstalls a newer SDK would quietly ignore the pin.
- **The SDK the pin cannot reach.** The pinned 10.0.110 exists only as a package: Microsoft
  publishes no container image for it (the newest 1xx tag on MCR is `10.0.103`), and
  `packages.microsoft.com` carries no .NET 10 for noble at all — on Ubuntu 24.04 it defers to
  Ubuntu's archive. So the three environments cannot all be locked to one build, and
  `.dockerignore` keeps `global.json` out of the image: copying it in would leave `sdk:10.0` unable
  to satisfy the pin and **every deploy would stop**. The deploy image therefore still publishes on
  whatever `sdk:10.0` currently is, as it always has — CI, not the image build, is the gate that
  ran the tests. Moving the pin means checking that MCR has an image for the new band first.
  A container build cannot be rehearsed from a web session: the Docker CLI is installed but no
  daemon runs.
- **A base class for a page goes in the `.razor`, not the code-behind**: putting
  `: SeasonAwarePage` on the `public partial class` gives *CS0263: Partial declarations must not
  specify different base classes*, because the generated Razor partial already declares
  `: ComponentBase`. Use `@inherits SeasonAwarePage` in the markup file.
- **A generic dialog result can't tell `default` from "cancelled"**: `PromptAsync<TDialog, TResult>`
  is constrained to `class` for that reason. A dialog closing with `0` is otherwise
  indistinguishable from the user pressing Cancel, so one returning a value type needs its own
  helper handing back `TValue?` — there was a `PromptValueAsync` doing exactly that until its last
  caller went, and adding another value-typed dialog means writing it again.

## Result
- **A cancelled call is a failure with no message, and both halves matter.** Threading a
  `CancellationToken` into services makes an ordinary navigation-away throw
  `OperationCanceledException` from inside EF. `ServiceOperation.RunAsync` catches it *ahead of*
  its general handler, or every visitor leaving a page would log an error with a stack trace and
  raise "Failed to load games" — on the page they moved to, because the snackbar lives on the
  circuit and not on the page that made the call. `Result.Cancelled()` therefore keeps `IsFailure`
  true (so every existing success check still reads "no") while carrying a null `ErrorKey`, and
  `UiFeedback` shows nothing for one. **`Result.To<T>()` must carry `IsCancelled` too** — drop it
  when handing a result up between services and the cancellation arrives at the page as a
  messageless failure, which is an empty red snackbar.
- **The catch filter is load-bearing**: `when (cancellationToken.IsCancellationRequested)`. An
  `OperationCanceledException` raised while the caller's own token is untouched is a bug, not
  someone leaving, and must keep falling through to the error log.
- **A cancelled load must not redirect.** The pages that treat "not found" as a reason to
  `Trail.Redirect(...)` have to check `result.IsCancelled` first, or abandoning the load throws the
  visitor off whatever page they actually navigated to — a navigation that fights the one they
  just made. `MatchResult`, `FormationBuilder`, `FormationOverview` and `PlayerStats` all carry
  that check.
- **Reading `Result<T>.Value` on a failure throws**: it used to return `default`, so a caller that
  skipped the success check got a null three frames away instead of an error where the mistake was.
  Check `IsSuccess` (or let `Snackbar.ReportFailure` do it — it returns a bool for exactly this).
- **Failure messages are templates, not interpolated strings**: `Result.Failure("Season {0} still
  has {1} games", name, count)`, never `$"..."`. The template is the resource key, so an
  interpolated message can't be translated.

## Formation/Pitch
- **Duplicate enum positions are fine — do not "fix" them.** A formation with two CBs or two
  strikers returns the same `PlayerPosition` twice from `DefaultPositions()`, and that is the
  design: which slot a player occupies comes from `GamePlayerPosition.SlotIndex`, ordered by
  `FormationSlots.OrdinalOf`. This entry used to say the opposite — that side-specific members
  (LCDM/RCDM, LST/RST) were needed — and they were, until `ConsolidatePlayerPositions` and
  `ConsolidatePositionsRound2` deleted them. Reintroducing them would undo those migrations.
- **Pitch too large**: `max-height: 65dvh` with `aspect-ratio: 3/4` and
  `max-width: calc(65dvh * 3/4)`. `dvh`, not `vh` — on iOS `vh` is the *large* viewport, so with
  the URL bar showing a `vh`-sized pitch is taller than the visible area.
- **Chips must scale with the pitch.** `.pitch` is `container-type: inline-size` and `--chip-size`
  is a `clamp(..., cqw, ...)`. Fixed-pixel chips looked right on a full-width pitch and collided on
  a narrow one — at ~225px wide, a 52px chip is a quarter of the pitch and the wide positions
  (LM at `left: 8%`) hung off the grass, since `.pitch` has no `overflow: hidden`.

## CSS scoping
- **A class used on a page that doesn't own its `.razor.css` silently does nothing.** Scoped CSS
  compiles to `.foo[b-<ownerHash>]`, so `.action-btn` defined in `Games.razor.css` never matched
  the identical markup on `/settings` — those buttons rendered as native browser chrome for as
  long as nobody looked. There is no warning. Anything more than one page uses goes in `app.css`;
  `.action-btn`, `.badge-*`, `.stat-tile*` and `.stacked-table` are there for this reason.
- **The same trap catches a rule that never leaves its own page: a child component's root element
  has no scope attribute either.** `.live-control-row > *` sat in `LiveMatch.razor.css` and matched
  nothing, because every child of that row is a `MudButton` and the `<button>` MudBlazor renders
  carries no `b-<hash>`. The row looked deliberate and read as flex — the buttons simply never took
  the width or the height it asked for, which is what "the buttons don't fill the box" turned out
  to be. The tell: the *container* is styled and the *children* are not. Anything selecting past a
  MudBlazor component's root goes in `app.css`, next to `.live-scoreboard` and `.live-action-btn`,
  which are there for the same reason.

## Live match
- **A change to the pitch that writes no row is invisible to everything that reads the rows.**
  `SwapPositionsAsync` moves two players between slots without a `GameSubstitution`, which is right
  — nobody left the pitch — but two readers assumed the rows were the whole story.
  `RemoveSubstitutionAsync` handed back the slot the *substitution* recorded, so subbing into slot
  5, swapping that player to slot 0 and then undoing seated two players in slot 5 and emptied
  slot 0; it now reads the slot off the player coming off instead. And `GameMinutesReport` seeds
  from the lineup as it finally stands, so a swap credits **the position moved into** for the whole
  half, earlier minutes included — the opposite of what its comment used to claim. Totals are
  right either way; only the split by position is affected, and a test pins it.
- **A quarters match only ever kicks off two of its four periods.** The live match knows halves
  and nothing else: `Game.NextHalf()` skips a line-up whose half has already been played, so the
  second half opens at Q3. Q2 and Q4 keep their planned line-ups and never get `StartedAtSeconds`,
  which is exactly what `GameMinutesReport` needs — a line-up that was never kicked off contributes
  nothing, so the half is credited to the line-up that played it plus the substitutions made during
  it. Q2 and Q4 reach the touchline only as `Game.MidHalfPlan()`, behind the live screen's
  `Changes (n)` pop-up. Do not "fix" a Q2 with no timings, and do not read `PeriodCount` as a count
  of stages the clock stops for.
- **A goal's minute is derived, not stored — and two goals in the same table are placed by
  different columns.** A goal logged from `/live` carries `GamePeriodId` and `AtSeconds`, the same
  pair a substitution carries, and the minute anyone sees comes out of `MatchClockReport.MinuteOf`.
  A goal typed in on `/result` has neither and falls back to `Minute`. So do all the goals logged
  before `StoreGoalPeriodAndClock` that were not scored in stoppage time: that migration backfilled
  only what an old row states outright, and a plain minute does not say which half it belonged to.
  The trap is reading `Minute` directly and finding it null on a live match, or assuming a row that
  has one was typed in by hand. Never reinstate the previous shape — a minute frozen on the row
  moved under stored data whenever `GameDurationMinutes` changed, and could not be corrected when a
  half's timings were.
  The same migration dropped `AdditionalMinute`, but **backfilled the rows that carried one first**:
  an overrun on a row says outright that it was stoppage time, so the half follows from the minute
  and the clock reading from that half's kick-off, and those goals still read `30+2` afterwards.
  `32` would be the 32nd minute — two minutes into a second half — which is a different moment.
  Rows with `AdditionalMinute = 0` were left alone, because a stored `37` could equally be a minute
  typed in by hand. That backfill has run everywhere it was ever going to, and the migration was
  folded into `InitialCreate` along with the test that drove it across the boundary — so what is
  written here is now the only record of why an old row looks the way it does.
- **A stored `Minute` is a scoreboard reading, and the timeline is ordered on elapsed seconds — do
  not mix the two.** They agree only while the halves run to length. On a match whose first half
  was whistled off three minutes long, the scoreboard's 31' is 33 minutes of elapsed play, so
  taking `(Minute - 1) * 60` as an ordering key files a second-half goal *before* one scored in
  first-half stoppage time — wrong running score out of `ScoreProgressionReport`, and the goal
  drawn on the wrong side of the half-time rule. `MatchClockReport.ElapsedOf` is the conversion,
  and it is the only thing that should produce an ordering key for a goal. It cost a review round
  on the change that introduced it.

## Authentication
- **`ExpireTimeSpan` does not keep anyone signed in — `IsPersistent` does.** `SignInAsync` without
  `AuthenticationProperties` sets a *session* cookie: no `Expires` on the header, so the browser is
  free to drop it whenever it decides the session ended. An eight-hour `ExpireTimeSpan` sat right
  above it and looked like the answer, but it bounds the ticket *inside* the cookie and has no say
  in whether the browser keeps the container. The symptom is phone-shaped and so reads as flaky
  rather than broken: a desktop tab holds the cookie for days, while iOS Safari and an installed PWA
  drop it every time the OS reclaims the backgrounded tab — which is a coach putting their phone
  away at half time. Both sign-in routes now pass `PersistentSession()`, and it returns a fresh
  instance per call because the cookie handler writes `IssuedUtc`/`ExpiresUtc` onto the object it is
  handed; one shared static would pin every later sign-in to the first one's expiry.
- **`SameSite=Strict` makes an ordinary link look like a logged-out session.** Strict withholds the
  cookie on *every* cross-site navigation, a plain top-level link click included — so opening the
  site from WhatsApp, an email or a search result arrives anonymous and bounces to `/login`, and
  then a reload puts it right because that navigation is same-site. Coming back on its own is what
  makes it hard to report and easy to dismiss. `Lax` is the setting; it still withholds the cookie
  on the cross-site POST that CSRF actually needs, and nothing here is reached by one.
- **Persisting data-protection keys is only half of surviving a deploy.** The keys are on the
  volume, but the purpose they are derived for defaults to the content root path — `/app` only
  because the Dockerfile says `WORKDIR /app`. Keys present on disk and derived for a different
  string open nothing, and the failure is silent: no exception, no log line, just every cookie
  rejected at once after a deploy that changed nothing about authentication.
  `SetApplicationName("FootballFormation")` is what stops it.
- **These three are browser decisions, so no C# test can see them.** All three are pinned in
  `tests/ui/specs/session.spec.js`, which reads the cookie's attributes after a real form sign-in
  and follows a link into the app from another site.
- **`OnValidatePrincipal` is not what revokes a Blazor Server session.** It runs per HTTP request,
  and a circuit makes almost none after its first page load — the rest of the session is SignalR.
  The stock `ServerAuthenticationStateProvider` reads the principal once when the circuit is created
  and never asks again, so deleting an account left the owner's open tab fully working. Measured,
  not assumed: with revalidation off, an account deleted while its owner sat idle on `/users` still
  rendered the Add User button. And this is not only a markup problem — `CircuitCurrentUser` reads
  that same provider, so `RunAdminAsync` was consulting the stale principal too.
  `RevalidatingUserAuthenticationStateProvider` closes it on a timer.
- **A rejoin does not carry stale authority through, so the retained-circuit window does not widen
  the gap.** Worth knowing before reasoning about `DisconnectedCircuitRetentionPeriod` as if it did.
  With a revoked cookie, a dropped circuit does not come back: the reconnect fails and Blazor's
  client falls back to a full page reload, which is an HTTP request, which is
  `OnValidatePrincipal` — landing on `/login`. Probed both ways round, blocking `_blazor/negotiate`
  to force the give-up path and leaving it open for a clean rejoin; both reloaded, while
  `reconnect.spec.js` shows a *valid* cookie rejoining cleanly and staying live. So the stale window
  is the revalidation interval on a connected idle circuit, and nothing more.

## General
- **A published app started from the wrong directory serves every static file as 200 with an empty
  body.** The content root of a published app is the *working directory*, which is why the
  Dockerfile sets `WORKDIR /app` before its entry point. Run
  `dotnet path/to/publish/FootballFormation.Web.dll` from anywhere else and it boots, `/health`
  answers healthy, the page renders complete and correct — and `blazor.web.js` comes back
  `Content-Length: 0`, so `window.Blazor` is never defined, no circuit connects, and nothing is
  interactive. There is no error anywhere: not in the app log, not in the browser console, not in
  the network panel, where every request is a green 200. It surfaces only as every `_bl_*` wait in
  the UI harnesses timing out. Both places that start a published app (`ci.yml`'s browser jobs, via
  `UI_TEST_APP_DLL` and `VISUAL_APP_DLL`) `cd` into the artifact first.
- **`.count()` is the one Playwright query that does not wait, and it fails open.** Every other
  locator call in `tests/ui` retries until its timeout; `count()` answers from the DOM as it stands
  right now. `if (await dialog.count()) await confirmDialog(...)` therefore read zero before a
  MudBlazor dialog had rendered, skipped the confirmation entirely, and let the test carry on
  against a player who was never archived — green locally for months, red on a loaded runner. The
  guard was also unnecessary: `ToggleArchived` only skips the confirm when *restoring*. Prefer
  `openDialog()`, which asserts visibility and waits; reach for `count()` only to assert that
  something is *absent*, and even then `toHaveCount(0)` is the waiting version.
- **Waiting for a consequence is not waiting for the navigation it causes.** Changing the seeded
  admin's password rotates the security stamp, `OnValidatePrincipal` rejects the cookie issued
  before it, and the circuit navigates to `/login`. `visual-check.mjs` waited for the *notice* to
  clear, which happens when the component re-renders — earlier than the drop. Signing in on that
  signal starts a navigation while the circuit's own is still in flight, and Playwright abandons the
  new one: `Navigation to "/dev/login" is interrupted by another navigation to "/login"`, killing the
  run before its first screenshot. Wait on `page.waitForURL` for the landing. Any Blazor flow that
  ends in a server-driven redirect has this shape — the redirect is the thing to wait for, not the
  re-render that precedes it.
- **A retry does not get a clean database, so one flake can look like a hard failure.** `run.mjs`
  builds a single throwaway database per run, and Playwright's CI retry re-runs the test against
  whatever the failed attempt left behind. A test that creates a player and then fails will, on its
  retry, be adding that player a second time — `playerRow(...).first()` may match the wrong row, and
  the report says `1 failed` rather than `1 flaky`. Read a two-attempt failure as "flaked, then hit
  dirty state", not as proof the behaviour is genuinely broken.
