# Known Issues & Past Fixes

Avoid repeating these mistakes:

## EF Core
- **UNIQUE constraint on save**: When re-saving `GamePlayerPosition` entities, always create NEW entities with `Id = 0`. Never re-add tracked entities with existing IDs — EF tries INSERT with the old PK.
- **List value converters need ValueComparer**: Without it, EF won't detect changes to `List<PlayerPosition>` or `List<int>` properties.
- **DB path must be absolute**: Use `%LOCALAPPDATA%\FootballFormation\` not relative paths (relative resolves to working directory, which changes).

## Blazor / MudBlazor 9.x
- **Dialogs not showing**: `MudDialogProvider` must be inside an interactive render mode. Fixed by setting `@rendermode="InteractiveServer"` on both `<Routes>` and `<HeadOutlet>` in App.razor.
- **`Position` enum ambiguity**: Renamed to `PlayerPosition` because `MudBlazor.Position` exists.
- **`MudForm.Validate()` is obsolete**: Use `ValidateAsync()`.
- **`ShowMessageBox` removed**: Use custom `ConfirmDialog` component instead.
- **Multi-select binding**: Use `IReadOnlyCollection<T>` not `IEnumerable<T>`.
- **`RenderFragment` in code-behind**: Use `=> __builder =>` lambda pattern in `@code` block; can't use regular methods.

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
  set statically on `html, body` in app.css, plus `color-scheme: dark`), and Blazor's
  stock reconnect overlay is light (now themed via `#components-reconnect-modal`, and
  `js/pwa.js` reloads the page once reconnection fails or on return to a dead tab).

## Localization
- **Resource keys are English text, so watch for homographs**: "Home" was already the
  venue label ("Thuis") when the nav needed a home link — the nav uses the key "Start"
  instead. Resx names are also case-insensitive, so no "SUB"/"Sub" pairs.

## Formation/Pitch
- **Duplicate enum positions**: Formations with 2 CDMs or 2 strikers need distinct enum values (LCDM/RCDM, LST/RST) — can't have duplicate values in an array.
- **Pitch too large**: Use `max-height: 65vh` with `aspect-ratio: 3/4` and `max-width: calc(65vh * 3/4)`.

## General
- **Port already in use**: Kill orphaned process with `taskkill //PID <pid> //F`.
- **File locked during build**: Stop the running app before rebuilding.
