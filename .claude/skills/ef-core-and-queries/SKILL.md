---
name: ef-core-and-queries
description: Writing or changing an EF Core query against AppDbContext — the never-sort-a-DateTime-in-SQL rule, the team query filter and the child-row gate, GameQueries include chains, DbContext-per-operation, the two-contexts write rule, the stats cache a bulk write skips, and the UNIQUE-constraint trap on lineup saves. Use whenever a LINQ query, an Include, an ordering or a SaveChanges is involved.
---

# EF Core and queries

## Never order or compare a `DateTime` inside a query

SQLite has no date type. Every `DateTime` column in this schema is TEXT, so `ORDER BY Date` sorts
the string the value happened to be written as. **Materialise first, then order in memory** with
`GameOrdering` / `SeasonOrdering` / `TrainingOrdering` (at the foot of each model's file).

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

Always `CreateDbContextAsync`: the factory every service receives is `TeamScopedDbContextFactory`,
and its synchronous `CreateDbContext` throws by design.

## Every read is scoped to a team, and you do not write the `Where`

`TeamScopedDbContextFactory` stamps `CurrentTeamId`/`CurrentClubId` from `ICurrentTeam` onto each
context, and `AppDbContext` carries a `HasQueryFilter` on `Season`, `Game`, `Training`,
`MatchPreferences`, `SeasonSquadMember` and `PushSubscription` (by team) and `Player` (by club). A
query that never mentions the team still returns only the team in scope; an unstamped context
returns *nothing*, never another team's rows.

- **Child rows carry no filter.** Goals, substitutions, injuries, swaps, comments and periods are
  reached through a filtered `Game`. A write that loads one by its own id — `FindAsync(goalId)` or a
  `Where` alike — gates on `db.GameInScopeAsync(child.GameId, ct)` first (`Data/ScopeQueries.cs`),
  which turns another team's id into "not found".
- **`FindAsync` on a filtered root is filtered** when it goes to the database; it skips the filter
  only for an entity that context already tracks. Prefer `FirstOrDefaultAsync(x => x.Id == id, ct)`
  anyway, so the query reads the way it behaves.
- **`IgnoreQueryFilters()` is a decision to argue for**, like the date tag. The legitimate ones are
  cross-team on purpose: a push endpoint unique across every team, a test arranging two teams.
- **`IRawDbContextFactory` is not for services.** It makes an unstamped context, and only
  `CurrentTeam` (which must not ask a context which team it is), the per-team boot steps and the
  scope-less push and calendar queries take it — each stamping the team it was given by hand.

`TeamDataScopingTests` seeds two teams and asserts every public read returns only the one in scope;
a new read belongs there.

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
(breaks the short-lived context rule), and letting `MatchGoalService` log or remove a goal itself (a
second implementation of goal storage). `EditGoalAsync` is the one goal write it owns, and it does
the recount the same way: in its own transaction, through `GameService.RecountScorelineAsync`.

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
- **A write that skips `SaveChanges` skips the statistics cache.** `StatsCacheInvalidator` is a
  `SaveChanges` interceptor; `ExecuteUpdate`, `ExecuteDelete` and raw SQL go behind its back, and the
  expiry is sliding, so a page people keep reading keeps serving the old figures. Only the push
  subscriptions use `ExecuteDelete`, and no statistic reads them.
- `AsNoTracking` on reads; the `CancellationToken` threaded to *every* EF call underneath.

Incident detail: [docs/known_issues/](../../../docs/known_issues/ef-core.md) ·
conventions: [docs/patterns/](../../../docs/patterns/ef-core.md)
