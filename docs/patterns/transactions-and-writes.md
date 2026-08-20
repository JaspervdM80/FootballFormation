# Transactions and Writes

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

