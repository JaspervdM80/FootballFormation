---
name: ef-core-and-queries
description: Writing or changing an EF Core query against AppDbContext — the never-sort-a-DateTime-in-SQL rule, GameQueries include chains, DbContext-per-operation, the two-contexts write rule, and the UNIQUE-constraint trap on lineup saves. Use whenever a LINQ query, an Include, an ordering or a SaveChanges is involved.
---

# EF Core and queries

## Never order or compare a `DateTime` inside a query

SQLite has no date type. All eight `DateTime` columns in this schema are TEXT, so `ORDER BY Date`
sorts the string the value happened to be written as. **Materialise first, then order in memory** with
`GameOrdering` / `SeasonOrdering` (`Models/Game.cs`, `Models/Season.cs`).

```csharp
var games = (await db.Games.AsNoTracking().WithPeriods().ToListAsync(ct)).NewestFirst();
```

`DateInSqlInterceptor` fails any test whose query breaks this, so a violation goes red rather than
sorting almost-right. The one deliberate exception is `LiveMatchService`'s same-day
`Date >= today && Date < tomorrow`, kept in SQL so the home page does not load the games table whole;
it opts out by name with `.TagWith(QueryTags.ComparesDatesInSql)`. Adding that tag to a new query is a
decision to argue for, not a way to quiet a failing test.

## Each operation opens its own short-lived context

Take `IDbContextFactory<AppDbContext>`, never an injected `AppDbContext`. A Blazor Server circuit
outlives a request, so a scoped context is shared by every component on the page and two concurrent
queries throw *"A second operation was started on this context."*

```csharp
await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
```

## When two rows have to agree, one context writes both

One `SaveChangesAsync` is a transaction, so most mutations are atomic without saying anything. What no
transaction can cover is **two `AppDbContext` instances** — a service method calling another service's
write is two transactions with a gap, and a SQLite lock timeout or Fly restarting the container
mid-deploy lands in it. The app migrates on boot, so a deploy *is* a restart.

`GameService.AddGoalAsync(goal, recountScoreline: true)` is the worked example: one transaction saves
the goal, recounts from the goals **then** on file, and commits both. The recount goes *after* the
save — counting in memory and adding one would be a read-modify-write, and two touchline devices
logging a goal in the same moment would each write *n+1* behind two goal rows.

**Recount, never increment.** `Game.CountScoreFrom(goals)` rewrites the scoreline rather than nudging
it. A derived value that is recomputed heals; one that is incremented accumulates.

Two rejected alternatives, worth not re-proposing: passing a context or transaction between services
(breaks the short-lived context rule), and letting `MatchGoalService` store goals itself (a second
implementation of goal storage).

## Include chains are named, not respelled

`Core/Data/GameQueries.cs` holds every shape a `Game` is loaded in, as composable `IQueryable`
extensions:

```csharp
var game = await db.Games
    .AsNoTrackingWithIdentityResolution()
    .WithNamedLineups()
    .WithGoalsAndScorers()
    .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
```

They come in pairs, shallow then deep over one navigation: `WithPeriods`/`WithPeriodLineups`/
`WithNamedLineups`, `WithGoals`/`WithGoalsAndScorers`, `WithSubstitutions`/`WithSubstitutionPlayers`.
**Compose one of a pair, never both** — EF rejects a filtered and an unfiltered include of the same
navigation in one query.

Deliberately not a repository: they stay `IQueryable`, so tracking, filtering and tagging remain the
caller's decision.

## Traps that have already cost time

- **`DbSet.Update` on an entity loaded with its graph** walks the whole graph and marks every row
  `Modified` — renaming an opponent would rewrite the lineup history.
- **Re-saving `GamePlayerPosition` needs fresh entities with `Id = 0`.** Re-adding tracked entities
  with existing IDs makes EF attempt an INSERT with the old PK and hit the UNIQUE constraint.
  `SavePeriodLineupAsync` deletes then inserts fresh, both inside one `BeginTransactionAsync` — a
  failed insert must not leave the period with no lineup at all.
- **List value converters need a `ValueComparer`.** Without one EF never detects a change to
  `List<PlayerPosition>` or `List<int>`.
- `AsNoTracking` on reads; the `CancellationToken` threaded to *every* EF call underneath.

Incident detail: [docs/known_issues/](../../../docs/known_issues/ef-core.md) ·
conventions: [docs/patterns/](../../../docs/patterns/ef-core.md)
