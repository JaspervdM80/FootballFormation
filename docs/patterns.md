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
general handler and returns `Result.Cancelled()` — still an `IsFailure` so every existing "did that
work?" check reads it as no, but carrying `IsCancelled` and a null `ErrorKey` so nothing is logged
and nothing is shown.

The three call-site consequences — `UiFeedback` staying quiet, `Result.To<T>()` having to carry the
flag, and the load-bearing catch filter — are in
[known_issues.md](known_issues.md#result), along with the redirect trap.

**Which calls get a token, in the UI.** Reads do; writes do not. Pages take theirs from
`CancellableComponent.Cancellation` (see ui_components.md), which trips on disposal. A write is
deliberately left on `default`: an admin who taps "finish match" and then loses the circuit must
still have finished the match, and abandoning a dispatched write to save a few milliseconds of
SQLite is the wrong trade. The rule is *the token goes on the calls whose only purpose is to show
something*.

`SeasonState.EnsureLoadedAsync()` is the other deliberate omission — the task is memoized and
shared by the layout's picker and whichever page is open, so cancelling it on behalf of one page
would take the app bar down with it.

**`MatchPreferencesService.GetAsync` is the one read that writes**, and the one place the rule
needed spelling out inside a method rather than at the call site. It seeds a preferences row for a
season on first read, and both `/settings` and the game dialog hand it a page-lifetime token. The
lookups above the seeding take that token and give up having written nothing, which is right; the
`SaveChangesAsync` that inserts the row takes `CancellationToken.None`, because by then it is a
write and a write finishes. `SeasonService.CloseSeasonGapsAsync` and `EnsureCurrentSeasonAsync` are
the same shape — a read that repairs — but only ever run from startup with no token, so nothing
there has to decide.

## When two rows have to agree, one context writes both
One `SaveChangesAsync` is a transaction, so almost every mutating method here is atomic without
saying anything: it opens a context, changes what it changes, and saves once. The two that need more
say so with an explicit `BeginTransactionAsync` — `GameService.SavePeriodLineupAsync`, where
delete-then-insert is two saves, and the goal writes below, where the second save has to read what
the first one wrote.

**What no transaction can cover is two `AppDbContext` instances.** Each operation opens its own from
the factory, for the circuit reason above, and each context has its own connection. So a service
method that calls *another service's* write is two transactions with a gap between them, and a
SQLite lock timeout, a failure on the second save, or Fly.io restarting the container mid-deploy all
land in that gap. That is not hypothetical: the app migrates itself on boot, so a deploy is a
restart.

The rule that follows: **when one row is derived from another, write them through one context and
commit them once.** The live scoreline is the worked example. Logging a goal used to insert the row
through `GameService` and then recount the score through `MatchGoalService`'s own context — two
saves in two contexts, and an interruption between them left the goal on file behind a stale
scoreline. `GameService.AddGoalAsync(goal, recountScoreline: true)` now opens a transaction, saves
the goal, recounts from the goals **then** on file, and commits both together. `RemoveGoalAsync`
mirrors it. `MatchGoalServiceTests` counts the commits, because "one write" is the property and it
is invisible from the outside until something interrupts the halves.

**The recount goes after the save, not before it.** Counting the goals in memory and adding the new
one to the total would be one save rather than two, which is tempting — and it would be a
read-modify-write. Two touchline devices logging a goal in the same moment would each read *n* and
each write *n+1*, leaving two goal rows behind a scoreline of one. Counting *after* the insert, with
SQLite's write lock already held, makes the second one wait and then count both. The insert has to
come first, so the two writes need the transaction rather than a shared `SaveChanges`.

Two ways of getting there were considered and rejected, and both are worth not re-proposing:
passing a context or a transaction from one service into another (which breaks the short-lived
context rule that exists for the circuit), and letting `MatchGoalService` store goals itself (a
second implementation of goal storage, which delegating to `GameService` exists to prevent).

`recountScoreline` defaults to false, and that is the result page: there an admin types the score
and records the goals whose scorer somebody remembered, so the list is allowed to be shorter than
the scoreline and recounting would rewrite a 3-1 as 1-0. Both behaviours are pinned by a test.

**Recount, never increment.** `Game.CountScoreFrom(goals)` rewrites the scoreline from the goals
rather than nudging it, so a score that did drift is repaired by the next goal logged and by
`MatchClockService.FinishMatchAsync`, which recounts the same way at the final whistle. A derived
value that is recomputed heals; one that is incremented accumulates.

### The one multi-save write left, on purpose
`GameService.CreateAsync` resolves `SeasonId 0` through `SeasonService.GetOrCreateForDateAsync`,
which may create and save a season in its own context before the game is saved in this one. Stopping
between the two leaves an **empty season** — and that is allowed to stand rather than being made
atomic, because an empty season is a valid gapless window: the next game scheduled on that date
resolves to it and reuses it, so the leftover costs nothing and disappears on its own.
`GameServiceTests` pins that reuse, so the reasoning holds rather than merely being believed. Making
it atomic would need one of the two moves rejected above, which is a poor trade for a leftover with
no consequence.

**Not in scope, deliberately:** none of this gives writes the page-lifetime token from
`CancellableComponent`. Atomicity says all-or-nothing; it does not say which one is wanted, and for
a write an admin explicitly asked for the answer is *all*. A dropped circuit is not someone changing
their mind.

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
| `MatchClockService` | Kick-off, half time, starting the next half, the final whistle — and `BankClock`, the only thing that moves seconds about. No pause: only half time stops the clock |
| `MatchGoalService` | The live minute a goal is stamped with. Storing the goal, and recounting the scoreline in the same save, still delegates to `GameService` |
| `MatchSubstitutionService` | The slot swap and the record of it, in one `SaveChanges`, and undoing the most recent one of a half |

What made the cut worth making was not the line count: the clock arithmetic and the substitution
slot-swapping shared a type, a `UtcNow` and a set of private helpers, so reading either meant paging
past the other. What *not* to do instead — pull the data access out from under it — would have
fought the rule that each operation opens its own short-lived context, which the file already
followed correctly throughout.

Three things fall out of a split like this, and they are the parts worth copying:

- **Pure helpers over an entity move onto the entity.** `CurrentPeriod` and `NextPeriod` were
  private statics over a `Game`; they are `Game.LiveHalf()` and `Game.NextHalf()` now, beside
  `Game.CurrentOrLastHalf()` and `Game.MidHalfPlan()`, and the live page reads its "next half" and
  the plan it offers as a reference from the same ones. A helper that *mutates*, like `BankClock`,
  stays with the service that owns the writing.
- **What every piece still shares gets named once.** `LiveMatchQueries` holds the tracked load they
  all start from (the game with its planned line-ups, shaped by `GameQueries.WithPeriods`) and the single
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
`Game.PeriodCount`, `Game.PeriodDurationSeconds`, `Game.IsInRoster`, `Game.SelectRoster`,
`Game.LiveHalf()`, `Game.NextHalf()`, `Game.MidHalfPlan()`,
`GameSplitTypeExtensions.PeriodCount()/PeriodDurationSeconds()/PeriodLabel()`. `PeriodCount` derives from
`PeriodTypeExtensions.ForSplitType`, so the count can never drift from the periods actually created.

The split-type extensions take the duration as a parameter rather than a `Game`, so the game dialog
can preview the split of a duration that has not been saved onto a game yet and get the same answer
the saved game will give.

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

  Pairs, shallow then deep over one navigation: `WithPeriods` / `WithPeriodLineups` /
  `WithNamedLineups`, `WithGoals` / `WithGoalsAndScorers`, `WithSubstitutions` /
  `WithSubstitutionPlayers`. Compose one of a pair, never both — EF rejects a filtered and an
  unfiltered include of the same navigation in one query, which is also why the deep ones include
  their collection twice.

  **Deliberately not a repository:** they stay `IQueryable`, so tracking, filtering, ordering and
  tagging remain the caller's, each being a decision made for a documented reason
  (`AsNoTrackingWithIdentityResolution` for the Blazor circuit, `QueryTags.ComparesDatesInSql`).
  Only the chain is shared, because that is the part that drifted — spelled out in two files it
  fails *silently* when one copy changes.
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
  `AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`. `AddSeasons`
  and `ConsolidatePlayerPositions` were written that way.
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

### Migrations are one file

`Migrations/` holds a single migration, `20260322100416_InitialCreate`, which creates the whole
schema. The twenty that grew it between March and August 2026 were folded into it once every
database that exists had them all applied; the names still quoted in these docs — `AddSeasons`,
`AddSeasonSquads`, `StoreGoalPeriodAndClock` — are history rather than files you can open.

**The id is the original `InitialCreate`'s, not the timestamp it was scaffolded at, and that is
what makes the fold safe.** The live volume has `20260322100416_InitialCreate` in its
`__EFMigrationsHistory` already, so it boots with nothing pending and never runs this file; a fresh
id would have re-run `CREATE TABLE` over a season of data and failed the deploy. The nineteen rows
below it in that table now name migrations the assembly no longer has, which EF ignores — pending
work is what the assembly holds and the history does not.

So if you ever rescaffold this migration rather than adding one after it, **put the id back by
hand** — in both file names and in the `[Migration]` attribute in the designer file — and check
that the schema still comes out the same:

```bash
APP_DATA_DIR=/tmp/schema-check dotnet ef database update --project src/FootballFormation.Core
```

Migrations from here on are ordinary ones added on top. There is no reason to fold again until the
count is a nuisance, and doing it costs the migration bodies: `GoalClockBackfillTests` covered the
one backfill that rewrote rows, and went with the file it tested.

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

**Minute figures are the exception inside those public reads.** How long someone has played is the
raw material of a rotation argument, so it belongs to whoever has to make one: a visitor gets the
*split* — which positions a player spent their time in, as a share of that time — and an admin gets
the minutes, the totals and the share of *available* time behind them. On `/players/{id}/stats` that
hides the Minutes tile whole (total, per-game average and utilisation alike), the minutes half of
each position label, the per-game `Min` column and the footnote explaining its `~`; on `/stats` it
hides the Playing-time card outright, because that card is minutes from top to bottom and a redacted
version of it would say nothing. Note what this costs: **utilisation — minutes played over minutes
available — is admin-only in full**, so a visitor cannot see how much of their own time a player
got, only how they divided it. That is the intended reading of the rule, not an oversight; a public
utilisation figure is the rotation argument with the units filed off. Games, goals, assists and the
scoreline are counts, not minutes, and are unaffected.

**Goalkeeper minutes on `/stats` stay public, deliberately** — who has kept goal and for how long is
what the squad actually asks about, and it is one figure per keeper rather than a table ranking
everybody. It is the only card that shows minutes to a signed-out visitor, and
`authorization.spec.js` asserts it is still there, so removing it has to be a decision someone takes
rather than a line that rots away.

Nothing in `Core` knows about any of this — the reports return the same numbers either way and the
render is what chooses — so the rule is pinned in `tests/ui/specs/` instead. `match-day.spec.js`
completes a match and then reads the player page twice, once as an admin and once in a visitor
context, because proving an absence is only worth anything next to the presence it is measured
against.

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

## The sign-in cookie has three settings that are easy to get wrong
All three live in `Program.cs`, and each failed in a way that reads as "it logged me out again"
rather than as a bug with a cause. The rules, with the evidence and the symptoms in
[known_issues.md](known_issues.md#authentication):

- **`IsPersistent`, on the sign-in**, not `ExpireTimeSpan`, is what makes the cookie outlive the
  browser session. Both sign-in routes pass `PersistentSession()`, which returns a *fresh*
  `AuthenticationProperties` each call.
- **`SameSite` is `Lax`, and must not go back to `Strict`.**
- **Data protection pins an application name**, or the purpose string follows the content root path.

`tests/ui/specs/session.spec.js` holds all three — they are browser decisions, so no C# test can see
them.

## Revoking authority takes two halves, because a circuit barely makes requests
`OnValidatePrincipal` re-checks the security stamp on every HTTP request — and a Blazor Server tab
makes almost none after the page loads, so it is not what revokes a session here (see
[known_issues.md](known_issues.md#authentication) for what that measured).
`RevalidatingUserAuthenticationStateProvider` (Web/Security) is the other half: it re-asks
`UserService.FindForSessionAsync` on a timer for the life of the circuit and signs it out when the
account is gone or its stamp has moved. Five minutes by default;
`Auth:RevalidationIntervalSeconds` sets it, and `0` leaves the stock provider in place so the UI test
can be run against the old behaviour.

Both halves call the same `FindForSessionAsync(ClaimsPrincipal)` overload on purpose — two places
deciding separately what a valid session looks like is how they drift.

The provider takes an `IServiceScopeFactory` rather than a `UserService`, and **not** for the usual
short-lived-context reason. It *is* the circuit's `AuthenticationStateProvider`; `UserService`
depends on `ICurrentUser`, which depends on the `AuthenticationStateProvider`. Injecting it directly
closes the loop and the container refuses to build.

A failed check makes the circuit anonymous. It cannot clear the cookie — a circuit has no HTTP
response to set a header on — so `[Authorize]` renders `NotAuthorized`, `RedirectToLogin`
force-loads, and *that* request is where `OnValidatePrincipal` finally drops the cookie.

## Blazor Rendering
- Entire app is Interactive Server (set on `<Routes>` and `<HeadOutlet>` in App.razor)
- UI assembly discovered via `AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly)`
- Layout is `FootballFormation.UI.Layout.MainLayout`
