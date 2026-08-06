# Known Issues & Past Fixes

Avoid repeating these mistakes:

## EF Core
- **UNIQUE constraint on save**: When re-saving `GamePlayerPosition` entities, always create NEW entities with `Id = 0`. Never re-add tracked entities with existing IDs — EF tries INSERT with the old PK.
- **List value converters need ValueComparer**: Without it, EF won't detect changes to `List<PlayerPosition>` or `List<int>` properties.
- **DB path must be absolute**: Use `%LOCALAPPDATA%\FootballFormation\` not relative paths (relative resolves to working directory, which changes).
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
- **Dropdowns rendered as a full-width band across the page**: `MudPopover` carries `.mud-paper`, and app.css's card rule set `position: relative` on it — same specificity as MudBlazor's `.mud-popover{position:absolute}` but later in source order, so it won. A relatively positioned block fills the popover provider's width and treats the placement JS's `left`/`top` as an offset from its static spot at the top of the page. Fixed with `.mud-popover.mud-paper{position:absolute}` (+ the `.mud-popover-fixed` variant). Watch for this whenever a global `.mud-*` rule touches layout.
- **`MudMenu`'s `Class` lands on the root wrapper, not the activator button**: `Class="btn-gold"` painted an invisible `div` while the button kept MudBlazor's default filled colours. There is no `ActivatorClass` parameter in 9.7 — style `.<your-class>.mud-menu .mud-button-root` instead (see `.btn-gold.mud-menu` in app.css, and `SeasonPicker`'s `.season-picker .mud-button-root`).

## Touch / PWA
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

## Localization
- **Resource keys are English text, so watch for homographs**: "Home" was already the
  venue label ("Thuis") when the nav needed a home link — the nav uses the key "Start"
  instead. Resx names are also case-insensitive, so no "SUB"/"Sub" pairs.
- **Case-insensitivity bites the service action phrases**: `ServiceOperation`'s actions are
  lowercase verb phrases ("delete game"), and several collided with existing capitalized button
  labels ("Delete Game"). MSBuild warns `MSB3568: Duplicate resource name ... ignored` and the
  first entry silently wins. Reuse the existing key rather than adding a lowercase twin.

## Blazor components
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
