---
name: services-and-result
description: Writing or changing a service in Core/Services or Core/Security — the Result type, ServiceOperation.RunAsync/RunAdminAsync/RunApplicationAdminAsync, the per-team admin guard and its deliberate exceptions, cancellation, registration, and how a page consumes a Result. Use when adding a service method, handling a failure message, or wiring a call site that reads Result.Value.
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

**Admin means admin of the team in scope.** `ICurrentUser.IsAdminAsync()` asks about `ICurrentTeam`
(the `ff.team` cookie — a view choice anyone can make, never authority), and
`TeamAuthority.GrantsAdminOf` is the whole rule. Where a write's subject is another team than the one
in scope, ask about that team: `IsAdminOfAsync(teamId)`, which `UserService` uses for an account on
another team. **`RunApplicationAdminAsync`** is the rung above, for `TeamService`'s writes; the
`ApplicationAdmin` role itself, granted *or* revoked, is checked by `UserService.MayChangeAsync`
asking `IsApplicationAdminAsync()`, because an ordinary admin passes the `RunAdminAsync` around it.

The exceptions, each deliberate:

- **Reads with something to hide are guarded at the service.** `GameService.GetCommentsAsync`
  re-confirms its `includePrivate` flag against `ICurrentUser` — a boolean argument is not believed.
  `TrainingService.GetAllAsync` runs under `RunAdminAsync`: the training register is not public, and
  `StatsService`'s attendance reads lean on it as their guard, which is why they are not cached.
- **`PushSubscriptionService` is the one anonymous write.** Nobody signs in to follow a match, so it
  uses `RunAsync`, validates every field itself and sits behind the `"push"` rate limiter. It is not
  a precedent for a second one.

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

## UserService opts out of Result on purpose

`ValidateCredentialsAsync`, `FindForSessionAsync` and `ChangePasswordAsync` return raw values rather
than a `Result`: the login endpoint needs "wrong password" and "no such user" to be
indistinguishable, and a `Result` carrying a message hands an attacker the difference. Do not "fix"
those three to match the convention.

## Logging levels

Serilog writes to the console and to `%LOCALAPPDATA%\FootballFormation\logs\`. Follow the levels
already in use: `LogDebug` for a read, `LogInformation` for a mutation including the entity id,
`LogWarning` for an expected miss. `LogError` belongs to `ServiceOperation` — do not raise one
yourself. Always structured placeholders (`{PlayerId}`), never interpolation, or the properties stop
being queryable in the log files.

## No interfaces for services

Services are injected as concrete types. Do not add `IPlayerService` unless a second implementation
exists. `ICurrentUser` and `ICurrentTeam` are the deliberate exceptions — the seams the write guard
and the team scope need, supplied by the host from its own request.

A new service is registered in `CoreServiceRegistration.AddFootballFormationCore`, not `Program.cs`;
`CoreServiceRegistrationTests` resolves every one with the host's lifetimes. Anything shared across
circuits (`LiveMatchNotifier`, `StatsCache`) is a singleton and must not take a scoped service.

## When a service gets long, split by use case, not into layers

The live match is the worked example: one 514-line service became four, cut along what happens at the
touchline (the clock, the goals, the substitutions), never into a data-access layer. Pure helpers over
an entity move **onto the entity**; shared setup gets **named once** (`LiveMatchQueries`,
`ScopeQueries`); anything
every method had to remember becomes **part of the operation shape** (`LiveMatchOperation.RunAdminAsync`
makes the notify call itself). A page injecting all four is expected. A *facade* over them is the
signal the split was cut along the wrong line.

Full detail, including the rejected alternatives: [docs/patterns/](../../../docs/patterns/index.md)
