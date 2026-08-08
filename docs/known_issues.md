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

## Blazor components
- **`section` is a reserved word in a `.razor` file.** `@foreach (var section in Sections())` then
  `@section.Title` is parsed as the `@section` *directive*, not as a member access, and the build
  fails with `RZ2005: The 'section' directive must appear at the start of the line`. Name the
  variable anything else (`gameList` on `/games`), or parenthesise as `@(section.Title)`.
  **This one is SDK-dependent, which is the real trap:** it fails on the 10.0.110 SDK from Ubuntu's
  archive — the one `.claude/hooks/session-start.sh` installs — and compiled clean on the SDK
  `actions/setup-dotnet` resolved for `10.0.x` in CI, so a green check is not proof it builds in a
  web session. A `global.json` pinning one SDK would close that gap.
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
