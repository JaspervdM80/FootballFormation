# Result and Cancellation

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
[known_issues](../known_issues/result.md), along with the redirect trap.

**Which calls get a token, in the UI.** Reads do; writes do not. Pages take theirs from
`CancellableComponent.Cancellation` (see ../ui_components/shared-components.md), which trips on disposal. A write is
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

