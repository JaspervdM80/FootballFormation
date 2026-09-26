# Service Registration

## Service Registration
Core registers itself: `builder.Services.AddFootballFormationCore()`
(`Core/Services/CoreServiceRegistration.cs`) is the one list of Core services, so a second host
calls one method rather than copying `Program.cs`. The host keeps what reads its own request and
storage — the DbContext factories, `ICurrentUser`, `ICurrentTeam`, the UI state services and
`RequestContext`.

Services are scoped. The singletons are the deliberate exceptions: `LiveMatchNotifier` has to be
shared across circuits or a substitution on the sideline would never reach the parents watching;
`TimeProvider`, the stats cache and the push queries hold no per-circuit state.

Service-to-service edges are kept few and named: `GameService` injects `SeasonService` so that
"every game has a season" is an invariant no caller can bypass, and `MatchGoalService` injects
`GameService` so goal storage has one implementation. `SeasonSquadService` deliberately takes
none — it queries `db.Seasons` directly. It is separate from `SeasonService` because the two own
different things: the season lifecycle and its `IsCurrent` invariant, versus squad membership.

