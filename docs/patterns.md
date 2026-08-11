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

## Cancellation: the third outcome
Every public service method takes a trailing `CancellationToken cancellationToken = default` and
hands it to every EF call underneath — `ToListAsync`, `FirstOrDefaultAsync`, `FindAsync`,
`SaveChangesAsync`, `CreateDbContextAsync`, the lot. `ServiceOperation` is where it is threaded, so
the parameter is a property of the shape rather than something each method wires up:

```csharp
public Task<Result<List<Player>>> GetAllAsync(CancellationToken cancellationToken = default) =>
    ServiceOperation.RunAsync(logger, "load players", cancellationToken, async () =>
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var players = await db.Players.AsNoTracking().ToListAsync(cancellationToken);
        return Result.Success(players);
    });
```

**A cancelled call is not a failed one.** This is a Blazor Server app: a visitor navigating away,
closing the tab or losing their circuit is the most ordinary event there is, and on a phone at a
touchline it happens constantly. So `RunAsync` catches `OperationCanceledException` *ahead of* the
general handler and returns `Result.Cancelled()` — no `LogError`, no stack trace, and no
`"Failed to load games"` message. Without that, threading the token would have turned every
navigation-away into a logged error and a red snackbar on the page the visitor moved to.

`Result.Cancelled()` is still an `IsFailure`, deliberately: every existing "did that work?" check
reads it as no, which is the only safe answer. What sets it apart is `IsCancelled` and an
`ErrorKey` of null. Three consequences worth knowing:

- `UiFeedback.Report`/`ReportFailure` show nothing for one and return false. The snackbar belongs
  to the circuit, not to the page that started the call.
- `Result.To<T>()` carries the flag, so a cancellation stays a cancellation through however many
  services hand it up (`GameService.CreateAsync` → `SeasonService.GetOrCreateForDateAsync`).
- The catch filter is `when (cancellationToken.IsCancellationRequested)`. An
  `OperationCanceledException` raised while the caller's token is untouched is *not* the caller
  leaving — it is a bug — and still falls through to the error log.

**Which calls get a token, in the UI.** Reads do; writes do not. Pages take theirs from
`CancellableComponent.Cancellation` (see ui_components.md), which trips on disposal. A write is
deliberately left on `default`: an admin who taps "finish match" and then loses the circuit must
still have finished the match, and abandoning a dispatched write to save a few milliseconds of
SQLite is the wrong trade. The rule is *the token goes on the calls whose only purpose is to show
something*.

`SeasonState.EnsureLoadedAsync()` is the other deliberate omission — the task is memoized and
shared by the layout's picker and whichever page is open, so cancelling it on behalf of one page
would take the app bar down with it.

## Logging
- **Framework**: Microsoft.Extensions.Logging via Serilog
- **Sink**: Console + rolling file at `%LOCALAPPDATA%\FootballFormation\logs\`
- **Injection**: `ILogger<T>` via primary constructor (services) or `[Inject]` (Blazor pages)
- **Levels**: Debug (reads), Information (mutations), Warning (not found), Error (exceptions with stacktrace)
- **Noise suppression**: Microsoft.* and EF Core set to Warning minimum

## No interfaces for services
Services are injected as concrete types. Don't add `IPlayerService` etc. unless a second
implementation actually exists.

## When a service gets long, split it by use case — not into layers
The live match is the worked example. It was one 514-line service and is now four, cut along what
is actually happening at the touchline rather than along a data-access seam:

| Service | Owns |
| --- | --- |
| `LiveMatchService` | Reading: `GetLiveAsync` for the live screen, `GetTodaysMatchAsync` for the home banner. Both public, like every other read |
| `MatchClockService` | Kick-off, pause/resume, ending a period, starting or rolling into the next, the final whistle — and `BankClock`, the only thing that moves seconds about |
| `MatchGoalService` | The live minute a goal is stamped with and the scoreline recomputed from the goals on file. Storage itself still delegates to `GameService` |
| `MatchSubstitutionService` | The slot swap and the record of it, in one `SaveChanges`, and undoing the most recent one of a period |

What made the cut worth making was not the line count: the clock arithmetic and the substitution
slot-swapping shared a type, a `UtcNow` and a set of private helpers, so reading either meant paging
past the other. What *not* to do instead — pull the data access out from under it — would have
fought the rule that each operation opens its own short-lived context, which the file already
followed correctly throughout.

Three things fall out of a split like this, and they are the parts worth copying:

- **Pure helpers over an entity move onto the entity.** `CurrentPeriod` and `NextPeriod` were
  private statics over a `Game`; they are `Game.LivePeriod()` and `Game.NextPeriod()` now, beside
  `Game.CurrentOrLastPeriod()`, and the live page reads its "next period" from the same one. A
  helper that *mutates*, like `BankClock`, stays with the service that owns the writing.
- **What every piece still shares gets named once.** `LiveMatchQueries` holds the tracked load they
  all start from (the game with its periods, shaped by `GameQueries.WithPeriods`) and the single
  "game not found" message.
- **Anything every method had to remember becomes part of the operation shape.** Each write used to
  end with `notifier.Notify(gameId)`; three services each remembering that is worse than one, so
  `LiveMatchOperation.RunAdminAsync` wraps `ServiceOperation.RunAdminAsync` and makes the call
  itself on success — the same move the admin check already is. Its second overload is for the one
  write named by something other than a game (undoing a substitution, which is found by its own id):
  the operation answers with the id of the game it changed, and the caller gets a plain `Result`.

A page injecting all four is fine and expected. A *facade* over them would be the signal that the
split was cut along the wrong line.

## Domain logic on the model
Anything computable without the database lives on the entity, not in a service or a page:
`Game.PeriodCount`, `Game.PeriodDurationMinutes`, `Game.IsInRoster`, `Game.SelectRoster`,
`Game.LivePeriod()`, `Game.NextPeriod()`, `GameSplitTypeExtensions.PeriodCount()/PeriodLabel()`. `PeriodCount` derives from
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
- **Include chains are named, not respelled.** `Core/Data/GameQueries.cs` holds every shape a
  `Game` is loaded in, as `IQueryable<Game>` extension methods composed at the call site:

  ```csharp
  var game = await db.Games
      .AsNoTrackingWithIdentityResolution()
      .WithNamedLineups()
      .WithGoalsAndScorers()
      .WithSubstitutionPlayers()
      .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
  ```

  Three pairs, shallow then deep over the same navigation — `WithPeriods` /
  `WithPeriodLineups` / `WithNamedLineups`, `WithGoals` / `WithGoalsAndScorers`,
  `WithSubstitutions` / `WithSubstitutionPlayers`. Compose one of each pair, never both: EF
  rejects a filtered and an unfiltered include of one navigation in a single query.
  `WithGoalsAndScorers` and `WithSubstitutionPlayers` include their collection **twice**, because
  EF needs a fresh `Include` to hang a second `ThenInclude` off the same navigation, and the two
  spellings must match.

  This is **deliberately not a repository.** The methods stay `IQueryable`, so tracking, filtering,
  ordering and tagging remain the caller's — each of those is a decision somebody made for a
  documented reason (`GetLiveAsync`'s `AsNoTrackingWithIdentityResolution` for the Blazor circuit,
  `GetTodaysMatchAsync`'s `QueryTags.ComparesDatesInSql`), and none of them belongs in a shared
  helper. There is no interface and nothing to register. Only the include chain is shared, because
  that is the part that drifted: a six-level chain spelled out in two files fails *silently* when
  one copy changes — the page just renders with a navigation quietly unpopulated.
- **Value converters**: `List<PlayerPosition>` → comma-separated ints; `List<int>` → comma-separated values. Both need `ValueComparer` for change tracking.
- **SavePeriodLineupAsync**: Deletes all existing positions, then inserts fresh entities with `Id = 0` to avoid UNIQUE constraint errors (never reuse tracked entity IDs). Both halves run inside one `BeginTransactionAsync` — delete-then-insert needs all of it or none, or a failed insert leaves the period with no lineup at all rather than the one it had.
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
builder.Services.AddScoped<LiveMatchService>();          // reading a live match
builder.Services.AddScoped<MatchClockService>();         // writing to one, split by what happens
builder.Services.AddScoped<MatchGoalService>();          // on the touchline: the clock, the goals,
builder.Services.AddScoped<MatchSubstitutionService>();  // the substitutions
builder.Services.AddScoped<MatchPreferencesService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SeasonState>();       // UI state, see "UI state services"
builder.Services.AddScoped<NavigationTrail>();   // where this tab has been, for the back arrow
```

The two singletons are the deliberate exceptions. `LiveMatchNotifier` has to be shared across
circuits or a substitution on the sideline would never reach the parents watching; `TimeProvider`
is stateless.

Service-to-service edges are kept few and named: `GameService` injects `SeasonService` so that
"every game has a season" is an invariant no caller can bypass, and `MatchGoalService` injects
`GameService` so goal storage has one implementation. `SeasonSquadService` deliberately takes
none — it queries `db.Seasons` directly. It is separate from `SeasonService` because the two own
different things: the season lifecycle and its `IsCurrent` invariant, versus squad membership.

## Authorization is at the service boundary, not only in the markup
Every mutating service method goes through `ServiceOperation.RunAdminAsync`, which asks
`ICurrentUser` and refuses before running. The UI already hides those controls behind
`<AuthorizeView Roles="@AppRoles.Admin">` and an unrendered handler has no id to dispatch to — but
that is enforcement in the render tree only, and it stops holding the moment a service is reached
some other way. Reads stay open: the squad, fixtures and statistics are public.

**One read is not open, and it is the exception worth knowing.**
`GameService.GetCommentsAsync(gameId, includePrivate)` takes a flag saying whether to include
admin-only comments — and then confirms it against `ICurrentUser` rather than believing it, so a
caller passing `true` without the role gets the public ones. A read with something to hide should
not be the one place a boolean argument is trusted. `AuthorizationTests` pins both halves —
`An_anonymous_caller_asking_for_private_comments_gets_only_the_public_ones` alongside
`Reads_stay_open_to_everyone`. Everything else public stays genuinely public; if a second such read
appears, it belongs in this paragraph.

`CircuitCurrentUser` answers false for an account still on its seeded password, so the first-login
gate is a real restriction rather than a redirect that could be navigated around.

## Blazor Rendering
- Entire app is Interactive Server (set on `<Routes>` and `<HeadOutlet>` in App.razor)
- UI assembly discovered via `AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly)`
- Layout is `FootballFormation.UI.Layout.MainLayout`
