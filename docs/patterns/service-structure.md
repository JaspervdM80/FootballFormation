# Service Structure and Domain Logic

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

## The cached statistics, and where invalidation hangs from
`StatsService` is the one service that composes two others (`GameService` and `SeasonSquadService`),
which the rest of Core avoids. It earns it: the three statistics pages had the same four lines of
loading copied into each of them, and a cache cannot skip a load the page has already started. The
split is still by use case — "what the statistics pages need" — and it computes rather than
delegates, which is what keeps it from being the facade the section above warns about.

**One entry serves all three pages.** `SeasonStatsReport.Build` produces its per-player figures by
calling `PlayerStatsReport.Build` unchanged, so a player's entry in `SeasonStats.Players` is the
identical object `/players/{id}/stats` would have built for itself — `StatsServiceTests` asserts
`Assert.Same`, not merely equal figures. `/stats/positions` filters the same list to the regulars
rather than reporting on them separately. A squad of twenty costs one cache entry, not twenty-one.

**Nothing is ever invalidated; the key changes.** A write bumps `StatsCache.Generation`, which is
part of every key, so earlier entries are not stale but unreachable, and expire on their own after
fifteen idle minutes. There is no key registry, no tag index and no eviction pass — and a report
built while a write lands is orphaned rather than served, because `KeyFor` captures the generation
*before* the load and `Set` stores under that captured key. Cancelling a shared eviction token
instead would let that in-flight rebuild write its stale result back under the live key.

**The bump hangs off `SaveChanges`, not off `ServiceOperation.RunAdminAsync`.** The service shape is
the other single choke point and the more obvious candidate — `LiveMatchOperation.RunAdminAsync`
delegates to it, so every write in the app really does pass through — but the interceptor
(`StatsCacheInvalidator`, registered on the context factory in `Program.cs`) sits lower and needs no
argument threaded through forty call sites. The difference that matters is that there is nothing
left to remember: a new write method invalidates *by writing*. The one way around it is a write that
never reaches `SaveChanges` — `ExecuteUpdate`, `ExecuteDelete` or raw SQL, none of which this app
uses outside the migrations. Adding one would go behind the interceptor's back.

**Cache the report, never `GetAllWithDetailsAsync`.** `Games.razor` and `FormationBuilder` hand a
loaded `Game` straight back to `GameService.UpdateAsync`, which attaches it with
`db.Entry(game).State = EntityState.Modified` — so caching at the service level would put a shared
mutable graph into a `DbContext` and let a rename corrupt what another reader sees. The statistics
pages never write, so caching their output is safe.

**The report is auth- and culture-independent, which is why one copy serves everyone.**
`PlayerStatsReport.Build` knows nothing about admin: it always computes the minutes, and the page
hides them from a visitor with `_isAdmin`. Gating happens downstream of the cache, so there is no
per-viewer keying to get wrong and nothing of what #98 holds back can leak through it. That is also
the argument against output caching, which looks like a better fit until you count what the *markup*
varies by: the `ff.auth`, culture and season cookies, three chances to serve an admin's minutes to a
visitor.

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

