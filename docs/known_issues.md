# Known Issues & Past Fixes

Avoid repeating these mistakes:

## EF Core
- **UNIQUE constraint on save**: When re-saving `GamePlayerPosition` entities, always create NEW entities with `Id = 0`. Never re-add tracked entities with existing IDs — EF tries INSERT with the old PK.
- **List value converters need ValueComparer**: Without it, EF won't detect changes to `List<PlayerPosition>` or `List<int>` properties.
- **DB path must be absolute**: Use `%LOCALAPPDATA%\FootballFormation\` not relative paths (relative resolves to working directory, which changes).
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

## Data / domain
- **Deleting a player is destructive across every season**: `PlayerService.DeleteAsync` cascades their `GamePlayerPosition` and `GameGoal` rows, so last season's top scorer disappears from last season's stats. Pre-existing, but more visible now that old seasons are browsable. Prefer **removing them from the current season's squad** on `/players` — that keeps all history. Soft-delete (`IsArchived`) would be the real fix if this ever bites.

## Blazor / MudBlazor 9.x
- **Dialogs not showing**: `MudDialogProvider` must be inside an interactive render mode. Fixed by setting `@rendermode="InteractiveServer"` on both `<Routes>` and `<HeadOutlet>` in App.razor.
- **`Position` enum ambiguity**: Renamed to `PlayerPosition` because `MudBlazor.Position` exists.
- **`MudForm.Validate()` is obsolete**: Use `ValidateAsync()`.
- **`ShowMessageBox` removed**: Use custom `ConfirmDialog` component instead.
- **Multi-select binding**: Use `IReadOnlyCollection<T>` not `IEnumerable<T>`.
- **`RenderFragment` in code-behind**: Use `=> __builder =>` lambda pattern in `@code` block; can't use regular methods.
- **Dropdowns rendered as a full-width band across the page**: `MudPopover` carries `.mud-paper`, and app.css's card rule set `position: relative` on it — same specificity as MudBlazor's `.mud-popover{position:absolute}` but later in source order, so it won. A relatively positioned block fills the popover provider's width and treats the placement JS's `left`/`top` as an offset from its static spot at the top of the page. Fixed twice over: first by patch rules putting `position` back, and now — the current state — by the card rule never claiming a popover in the first place, `.mud-paper:not(.mud-popover):not(.mud-dialog)` in app.css. The patch rules are gone, so MudBlazor's own positioning is never disturbed and there is nothing left to restore. Watch for this whenever a global `.mud-*` rule touches layout: excluding the popover beats overriding it back.
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
  `.mud-day` is `width/height: 36px; margin: 0 2px`, i.e. a 36px circle on a 40px column pitch with
  rows flush — under Apple's 44px *and* Android's 48dp in both axes, and the 4px gutters between
  columns are **dead**: a tap landing there hits `.mud-picker-calendar` and does nothing at all.
  Worse, the popover was still pinned to 310px on a sheet that is now the full width of the phone,
  so 40px of screen sat unused either side of the calendar and left live form controls exposed
  there — with the picker open, `elementFromPoint` 14px outside the calendar returned the season
  `MudSelect` underneath. `--dp-day` in app.css spends that width instead (41.7px at 320 wide up to
  52px, sized by height as well so landscape doesn't blow past the viewport) and drops the side
  margins so the column pitch *is* the target. See [ui_components.md](ui_components.md).
- **The month name between the arrows is a 23px button, and it was the worst target of the lot.**
  Reported as "it's mostly the month selection". `.mud-picker-calendar-header-transition` is a real
  `<button>` — the one that opens the month grid — but MudBlazor gives it `height: 23px` because it
  doubles as the slide transition's viewport. It sits in the 56px row the two 44px arrows set, so
  there is **17px of dead div above it and 16px below**, and a thumb aimed at "augustus 2026" mostly
  lands in one of those and does nothing. 44px fits that row with no layout change at all. Its label
  is taken out of flow by `.mud-picker-slide-transition > *` (`position: absolute`, top/left/right
  pinned), so growing the button also needs `bottom: 0` and flex centring or the text sticks to the
  top edge. The toolbar's year button — the only way into the year list — was 64x40 and got the same
  44px floor.
- **The picker's flow is year → month → day, so the toolbar's date line is not an escape route.**
  Worth knowing before hiding it (which `app.css` does in landscape, to buy a 44px year button in a
  56px toolbar): picking a year lands on the month grid, and picking a month lands on the days. The
  date button only restates what is already in the field behind the popover. Verified end to end at
  every size, landscape included.
- **Sizing a MudBlazor calendar means sizing `.mud-picker-calendar-transition` too.**
  `.mud-picker-slide-transition > *` is `position: absolute`, so the grid is out of flow and that
  container's `min-height` (MudBlazor's 216px = six 36px rows) is the *only* thing reserving room
  for it. Grow the day cells without growing it and the last weeks are drawn straight over whatever
  follows. Always six rows — the calendar renders 42 cells whatever the month.
- **Buttons need clear space above them, not just their own size.** The game dialog's action row
  sat 28px under the last field, and `MudSelect`'s hit box reaches ~10px past its own underline —
  leaving 18px of dead space between "Annuleren" and the unavailable-players dropdown. Mobile
  browsers snap a tap that misses every target to the nearest one *by contact area*, and the
  select is the far bigger target, so a thumb aimed at the button opened the dropdown instead.
  This is invisible to `document.elementFromPoint`, which reports the button as reachable — the
  measurement that finds it is the **gap** to the nearest interactive element above.
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

## Touch / PWA (continued)
- **White page after switching apps**: a suspended PWA loses its SignalR circuit. Two
  causes, both fixed: the page background came only from the MudBlazor theme (now also
  set statically on `html, body` in app.css via `var(--surface-page)`, plus
  `color-scheme: light`), and Blazor's stock reconnect overlay is light (now themed via
  `#components-reconnect-modal`, and `js/pwa.js` reloads the page once reconnection fails
  or on return to a dead tab).
  **The reload needs its own guard, or it is the next bug.** A page that serves while the circuit
  never connects — a blocked WebSocket, a dead network — would reload forever. `pwa.js` stamps
  `sessionStorage` and refuses to reload twice inside ten seconds, leaving the overlay up instead.

## Localization
- **Resource keys are English text, so watch for homographs**: "Home" was already the
  venue label ("Thuis") when the nav needed a home link — the nav uses the key "Start"
  instead. Resx names are also case-insensitive, so no "SUB"/"Sub" pairs.
- **Case-insensitivity bites the service action phrases**: `ServiceOperation`'s actions are
  lowercase verb phrases ("delete game"), and several collided with existing capitalized button
  labels ("Delete Game"). MSBuild warns `MSB3568: Duplicate resource name ... ignored` and the
  first entry silently wins. Reuse the existing key rather than adding a lowercase twin.
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
  is constrained to `class` for that reason; a dialog returning a value type uses
  `PromptValueAsync`, which hands back `TValue?`. A dialog closing with `0` is otherwise
  indistinguishable from the user pressing Cancel.

## Result
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

## General
- **Port already in use**: Kill orphaned process with `taskkill //PID <pid> //F`.
- **File locked during build**: Stop the running app before rebuilding.
