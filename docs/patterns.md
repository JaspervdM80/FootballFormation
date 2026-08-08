# Patterns & Conventions

## Result Pattern
All service methods return `Result` or `Result<T>` (defined in `Core/Result.cs`).

Services do **not** write their own try/catch. `ServiceOperation.RunAsync` (in `Core/Services/`)
owns the exception handling, the error log and the user-facing message, which is always
`"Failed to {action}"` built from the action phrase passed in:

```csharp
// Service — expression-bodied, returns Task<...> directly (RunAsync awaits the lambda).
// RunAdminAsync rather than RunAsync because this one writes: it refuses a non-admin caller
// before the lambda runs. Reads use plain RunAsync and stay open to everyone.
public Task<Result<Player>> CreateAsync(Player player) =>
    ServiceOperation.RunAdminAsync(currentUser, logger, "create player", async () =>
    {
        // Its own context, every time — the factory, never an injected AppDbContext. See below.
        await using var db = await dbFactory.CreateDbContextAsync();

        db.Players.Add(player);
        await db.SaveChangesAsync();

        logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
        return Result.Success(player);
    });
```

Expected misses (not found) still return `Result.Failure(...)` explicitly from inside the lambda,
after a `LogWarning`. Only unexpected exceptions fall through to the wrapper.

```csharp
// UI consumer — via the UiFeedback extensions, never a hand-rolled if/else.
// The localizer is the first argument: the service states its error in English, and that English
// text is the resource key, so Report needs L to translate it.
var result = await PlayerService.CreateAsync(player);
Snackbar.Report(L, result, L["{0} added to the squad", player.DisplayName]);

// Loads: report only failures
var players = await PlayerService.GetAllAsync();
_players = Snackbar.ReportFailure(L, players) ? players.Value : [];
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

### Pass a value object, don't eager-load a navigation
When a model rule needs data the entity doesn't own, hand it in as a parameter rather than relying
on a navigation property being loaded. `Game.IsInRoster(player, squad)` takes a `SeasonSquad`
(`Models/SeasonSquad.cs`) instead of reading `Game.Season.SquadMembers`, because `Game.Season` is
nullable: a query that forgot the `.Include` would silently answer "everyone is a guest" and empty
the roster, with no compile-time signal, on any of `GameService`'s four read paths. A parameter
makes the dependency visible, gives the pure report helpers a scope they can be handed, and lets
`SeasonSquad.Empty` be an honest degraded value instead of a null nav.

The plural `SeasonSquads` exists for the same reason one level up: reports walk games across
seasons, so each game resolves *its own* season's squad.

## EF Core Conventions
- **DbContext**: `AppDbContext` with primary constructor
- **Value converters**: `List<PlayerPosition>` → comma-separated ints; `List<int>` → comma-separated values. Both need `ValueComparer` for change tracking.
- **SavePeriodLineupAsync**: Deletes all existing positions, then inserts fresh entities with `Id = 0` to avoid UNIQUE constraint errors (never reuse tracked entity IDs).
- **Auto-migration**: `db.Database.MigrateAsync()` in Program.cs startup
- **Never order or compare a date in the query.** SQLite keeps every `DateTime` in a TEXT column,
  so `ORDER BY Date` sorts the text a date was written as. Materialise first, then use
  `GameOrdering` / `SeasonOrdering` (`Models/Game.cs`, `Models/Season.cs`). See
  [known_issues.md](known_issues.md) for what goes wrong and the one deliberate exception.
- **Data backfills** belong in the migration's `Up()` via `migrationBuilder.Sql(...)`, not in
  startup code: `__EFMigrationsHistory` runs them exactly once, and it is the only place you can
  populate a new required FK column *before* the constraint is added. Order the operations
  `AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`. See
  `AddSeasons` and `ConsolidatePlayerPositions`.
  **Do not assume atomicity:** when EF rebuilds a SQLite table it emits `PRAGMA foreign_keys = 0`,
  which cannot run inside a transaction, so the migration is *not* all-or-nothing — and EF never
  re-runs `foreign_key_check`, so a partial backfill boots silently clean. Verify with
  `SELECT COUNT(*) FROM <table> WHERE <fk> = 0` and `PRAGMA foreign_key_check` after applying.
- **Check the scaffolder's ordering before trusting it.** `AddSeasonSquads` needed `Players.IsGuest`
  dropped *and* its values copied into a new table; EF emitted the `DropColumn` **first**, which
  would have destroyed the source data before the backfill could read it. Always read the generated
  `Up()` and reorder so reads happen before drops.
- **`DropColumn` rebuilds the whole table.** When that table is a *parent* (other tables hold FKs
  into it — `Players` has three), verify afterwards that its row count is unchanged and that no
  `ef_temp_*` table survived: `SELECT name FROM sqlite_master WHERE name LIKE 'ef_temp%'`.
- **Rehearse destructive migrations on a copy.** Copy the DB to a scratch folder, point
  `APP_DATA_DIR` at it, run `dotnet ef database update`, and check the data before touching the
  real file. Fly.io auto-migrates on boot, so production gets no second chance.

## UI state services
Two of these: `SeasonState` (`UI/State/SeasonState.cs`) holds the selected season, shared by
`MainLayout`'s picker and the season-aware pages; `NavigationTrail` (`UI/Navigation/`) holds where
the visitor has been. The pattern, taking `SeasonState` as the worked example:

- Registered `Scoped`, so on Blazor Server it lives for the SignalR circuit — the choice survives
  navigation within a tab but resets on a browser refresh.
- Loading is a **memoized task** (`EnsureLoadedAsync() => _loading ??= LoadAsync()`). A scoped
  service can't load in its constructor, and the layout and the page both need the data during
  their own `OnInitializedAsync`, where they interleave at the first `await`. This was once
  load-bearing — the services shared one scoped `AppDbContext` and a second concurrent query threw
  *"A second operation was started on this context."* They take a short-lived context per operation
  now (`AddDbContextFactory`), so it is purely an optimisation: forgetting it costs a duplicate
  query, not a crash.
- Change notification is `event Action? OnChanged`. Season-aware **pages don't wire this up
  themselves** — they inherit `SeasonAwarePage` (`UI/Components/SeasonAwarePage.cs`), which awaits
  the load, subscribes, re-runs `LoadAsync()` inside `InvokeAsync` on change, and unsubscribes on
  dispose. Override `LoadAsync()`; use `OnInitializedCoreAsync()` for one-time setup such as
  reading the auth state. Declare the base with `@inherits SeasonAwarePage` in the `.razor` —
  Razor owns the base class, so putting it on the code-behind is a CS0263 error.
- The state holds a **view** choice and never writes shared data. The picker is reachable by
  anonymous visitors, so it must not touch `Season.IsCurrent`, which is admin-owned on `/settings`.

`NavigationTrail` follows the same shape with one twist worth knowing. It records the circuit's
navigations by subscribing to `NavigationManager.LocationChanged` — but a scoped service is not
constructed until something injects it, and if the first injector were a detail page's back button
the navigation that led there would already have been missed. So subscribing lives in `Start()`,
which **`MainLayout.OnInitialized` calls**, before any page renders. `Previous` also records the
current URL before answering: the `Router` subscribed to `LocationChanged` first and re-renders the
page inside its own handler, so a component can read the trail before our handler has run.

Two related rules for anything that navigates:

- Build URLs from `AppRoutes` (`AppRoutes.PlayerStats(id)`), never an interpolated literal.
- Redirect away from a page that failed to load with `Trail.Redirect(...)`, not `NavigateTo`. It
  replaces the failed page in both the trail and browser history, so neither back button walks
  straight back into it.

## Service Registration
Scoped, except the two that must outlive a circuit:
```csharp
builder.Services.AddSingleton(TimeProvider.System);        // the clock, injected so tests can drive it
builder.Services.AddSingleton<LiveMatchNotifier>();        // fans live changes to every open circuit

builder.Services.AddScoped<ICurrentUser, CircuitCurrentUser>();  // who is asking; the write guard
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<SeasonService>();
builder.Services.AddScoped<SeasonSquadService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<LiveMatchService>();
builder.Services.AddScoped<MatchPreferencesService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SeasonState>();       // UI state, see "UI state services"
builder.Services.AddScoped<NavigationTrail>();   // where this tab has been, for the back arrow
```

The two singletons are the deliberate exceptions. `LiveMatchNotifier` has to be shared across
circuits or a substitution on the sideline would never reach the parents watching; `TimeProvider`
is stateless.

Service-to-service edges are kept few and named: `GameService` injects `SeasonService` so that
"every game has a season" is an invariant no caller can bypass, and `LiveMatchService` injects
`GameService` so goal storage has one implementation. `SeasonSquadService` deliberately takes
none — it queries `db.Seasons` directly. It is separate from `SeasonService` because the two own
different things: the season lifecycle and its `IsCurrent` invariant, versus squad membership.

## Authorization is at the service boundary, not only in the markup
Every mutating service method goes through `ServiceOperation.RunAdminAsync`, which asks
`ICurrentUser` and refuses before running. The UI already hides those controls behind
`<AuthorizeView Roles="@AppRoles.Admin">` and an unrendered handler has no id to dispatch to — but
that is enforcement in the render tree only, and it stops holding the moment a service is reached
some other way. Reads stay open: the squad, fixtures and statistics are public.

`CircuitCurrentUser` answers false for an account still on its seeded password, so the first-login
gate is a real restriction rather than a redirect that could be navigated around.

## Blazor Rendering
- Entire app is Interactive Server (set on `<Routes>` and `<HeadOutlet>` in App.razor)
- UI assembly discovered via `AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly)`
- Layout is `FootballFormation.UI.Layout.MainLayout`
