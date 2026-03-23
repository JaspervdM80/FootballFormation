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

## Formation/Pitch
- **Duplicate enum positions**: Formations with 2 CDMs or 2 strikers need distinct enum values (LCDM/RCDM, LST/RST) — can't have duplicate values in an array.
- **Pitch too large**: Use `max-height: 65vh` with `aspect-ratio: 3/4` and `max-width: calc(65vh * 3/4)`.

## General
- **Port already in use**: Kill orphaned process with `taskkill //PID <pid> //F`.
- **File locked during build**: Stop the running app before rebuilding.
