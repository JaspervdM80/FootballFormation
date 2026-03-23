# Patterns & Conventions

## Result Pattern
All service methods return `Result` or `Result<T>` (defined in `Core/Result.cs`).

```csharp
// Service
public async Task<Result<Player>> CreateAsync(Player player)
{
    try { ... return Result.Success(player); }
    catch (Exception ex) { logger.LogError(ex, "..."); return Result.Failure<Player>("message"); }
}

// UI consumer
var result = await PlayerService.CreateAsync(player);
if (result.IsSuccess) { Snackbar.Add("Done", Severity.Success); }
else { Snackbar.Add(result.Error!, Severity.Error); }
```

## Logging
- **Framework**: Microsoft.Extensions.Logging via Serilog
- **Sink**: Console + rolling file at `%LOCALAPPDATA%\FootballFormation\logs\`
- **Injection**: `ILogger<T>` via primary constructor (services) or `[Inject]` (Blazor pages)
- **Levels**: Debug (reads), Information (mutations), Warning (not found), Error (exceptions with stacktrace)
- **Noise suppression**: Microsoft.* and EF Core set to Warning minimum

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
