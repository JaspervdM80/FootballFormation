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

