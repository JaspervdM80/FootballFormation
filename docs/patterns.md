# Patterns & Conventions

## Result Pattern
All service methods return `Result` or `Result<T>` (defined in `Core/Result.cs`).

Services do **not** write their own try/catch. `ServiceOperation.RunAsync` (in `Core/Services/`)
owns the exception handling, the error log and the user-facing message, which is always
`"Failed to {action}"` built from the action phrase passed in:

```csharp
// Service — expression-bodied, returns Task<...> directly (RunAsync awaits the lambda)
public Task<Result<Player>> CreateAsync(Player player) =>
    ServiceOperation.RunAsync(logger, "create player", async () =>
    {
        db.Players.Add(player);
        await db.SaveChangesAsync();

        logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
        return Result.Success(player);
    });
```

Expected misses (not found) still return `Result.Failure(...)` explicitly from inside the lambda,
after a `LogWarning`. Only unexpected exceptions fall through to the wrapper.

```csharp
// UI consumer — via the UiFeedback extensions, never a hand-rolled if/else
var result = await PlayerService.CreateAsync(player);
Snackbar.Report(result, $"Player {player.DisplayName} added");

// Loads: report only failures
var players = await PlayerService.GetAllAsync();
_players = Snackbar.ReportFailure(players) ? players.Value : [];
```

**Trade-off:** the error log for an exception records the action phrase (`"Failed to {Action}"`)
rather than a per-entity id. Entity ids are still structured-logged on the `Information` and
`Warning` lines around it, and the exception carries the stack trace.

## Logging
- **Framework**: Microsoft.Extensions.Logging via Serilog
- **Sink**: Console + rolling file at `%LOCALAPPDATA%\FootballFormation\logs\`
- **Injection**: `ILogger<T>` via primary constructor (services) or `[Inject]` (Blazor pages)
- **Levels**: Debug (reads), Information (mutations), Warning (not found), Error (exceptions with stacktrace)
- **Noise suppression**: Microsoft.* and EF Core set to Warning minimum

## No interfaces for services
Services are injected as concrete types. Don't add `IPlayerService` etc. unless a second
implementation actually exists.

## Domain logic on the model
Anything computable without the database lives on the entity, not in a service or a page:
`Game.PeriodCount`, `Game.PeriodDurationMinutes`, `Game.IsInRoster`, `Game.SelectRoster`,
`GameSplitTypeExtensions.PeriodCount()/PeriodLabel()`. `PeriodCount` derives from
`PeriodTypeExtensions.ForSplitType`, so the count can never drift from the periods actually created.

## EF Core Conventions
- **DbContext**: `AppDbContext` with primary constructor
- **Value converters**: `List<PlayerPosition>` → comma-separated ints; `List<int>` → comma-separated values. Both need `ValueComparer` for change tracking.
- **SavePeriodLineupAsync**: Deletes all existing positions, then inserts fresh entities with `Id = 0` to avoid UNIQUE constraint errors (never reuse tracked entity IDs).
- **Auto-migration**: `db.Database.MigrateAsync()` in Program.cs startup

## Service Registration
All services registered as `Scoped` in Program.cs:
```csharp
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<MatchPreferencesService>();
```

## Blazor Rendering
- Entire app is Interactive Server (set on `<Routes>` and `<HeadOutlet>` in App.razor)
- UI assembly discovered via `AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly)`
- Layout is `FootballFormation.UI.Layout.MainLayout`
