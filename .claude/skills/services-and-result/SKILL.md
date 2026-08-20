---
name: services-and-result
description: Writing or changing a service in Core/Services — the Result type, ServiceOperation.RunAsync/RunAdminAsync, the admin write guard, cancellation, and how a page consumes a Result. Use when adding a service method, handling a failure message, or wiring a call site that reads Result.Value.
---

# Services and Result

Every service method returns `Result` or `Result<T>` (`Core/Result.cs`). Services **never** throw and
**never** write their own try/catch.

## The shape

Wrap the body in `ServiceOperation.RunAsync` for a read, `RunAdminAsync` for a write. The admin check
is a property of the shape, not something each method remembers.

```csharp
public Task<Result<Player>> CreateAsync(Player player, CancellationToken cancellationToken = default) =>
    ServiceOperation.RunAdminAsync(currentUser, logger, "create player", cancellationToken, async () =>
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Players.Add(player);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
        return Result.Success(player);
    });
```

Expected misses return `Result.Failure(...)` explicitly from inside the lambda, after a `LogWarning`.
Only unexpected exceptions fall through to the wrapper.

## Failure messages are templates, and the English template is the resource key

```csharp
Result.Failure("Season {0} still has {1} games", name, count)   // yes
Result.Failure($"Season {name} still has {count} games")        // no — cannot be translated
```

## Authorization is at the service boundary

Every mutation goes through `RunAdminAsync`. `<AuthorizeView Roles="@AppRoles.Admin">` is enforcement
in the render tree only and stops holding the moment a service is reached another way. Reads stay
open — squad, fixtures and statistics are public.

The one exception: `GameService.GetCommentsAsync(gameId, includePrivate)` re-confirms its own flag
against `ICurrentUser` rather than trusting the caller. A read with something to hide is not where a
boolean argument gets believed.

`ICurrentUser` answers false for an account still on its seeded password, so the first-login gate is a
real restriction rather than a navigable redirect.

## Cancellation is the third outcome

Every public method takes a trailing `CancellationToken cancellationToken = default` and hands it to
every EF call underneath — not just the outermost. `RunAsync` catches `OperationCanceledException`
*ahead of* the general handler and returns `Result.Cancelled()`: no log, no stack trace, no
"Failed to load games" on the page the visitor just moved to.

`Result.Cancelled()` is still `IsFailure` — every "did that work?" check reads it as no. What sets it
apart is `IsCancelled` and a null `ErrorKey`. The catch filter
`when (cancellationToken.IsCancellationRequested)` is load-bearing: an `OperationCanceledException`
raised while the caller's token is untouched is a bug, not someone leaving, and still logs.

**Reads get a token, writes do not.** An admin who taps "finish match" and then loses the circuit must
still have finished the match. Pages take theirs from `CancellableComponent.Cancellation`.
`SeasonState.EnsureLoadedAsync()` is the other deliberate omission — the task is memoized and shared,
so cancelling it for one page takes the app bar down with it.

## At the call site

- Never read `Result<T>.Value` without an `IsSuccess` check — or use `Snackbar.ReportFailure`, which
  returns the bool for exactly this. Reading a failed value throws by design.
- **Check `IsCancelled` before `Trail.Redirect(...)`.** A cancelled load that redirects throws the
  visitor off the page they just navigated to.
- `Result.To<T>()` carries the cancellation flag; dropping it delivers a messageless failure that
  renders as an empty red snackbar.
- Report through the `UiFeedback` extensions with `L` first, never a hand-rolled if/else:
  `Snackbar.Report(L, result, L["{0} added to the squad", player.DisplayName])`.

## No interfaces for services

Services are injected as concrete types. Do not add `IPlayerService` unless a second implementation
exists. `ICurrentUser` is the deliberate exception — it is the seam the write guard needs.

## When a service gets long, split by use case, not into layers

The live match is the worked example: one 514-line service became four, cut along what happens at the
touchline (the clock, the goals, the substitutions), never into a data-access layer. Pure helpers over
an entity move **onto the entity**; shared setup gets **named once** (`LiveMatchQueries`); anything
every method had to remember becomes **part of the operation shape** (`LiveMatchOperation.RunAdminAsync`
makes the notify call itself). A page injecting all four is expected. A *facade* over them is the
signal the split was cut along the wrong line.

Full detail, including the rejected alternatives: [docs/patterns/](../../../docs/patterns/index.md)
