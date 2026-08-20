# Service Registration

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
builder.Services.AddScoped<NavigationTrail>();   // where the visitor came from, for the back arrow
builder.Services.AddScoped<RequestContext>();    // the cookie and referrer this scope's request had
```

The two singletons are the deliberate exceptions. `LiveMatchNotifier` has to be shared across
circuits or a substitution on the sideline would never reach the parents watching; `TimeProvider`
is stateless.

Service-to-service edges are kept few and named: `GameService` injects `SeasonService` so that
"every game has a season" is an invariant no caller can bypass, and `MatchGoalService` injects
`GameService` so goal storage has one implementation. `SeasonSquadService` deliberately takes
none — it queries `db.Seasons` directly. It is separate from `SeasonService` because the two own
different things: the season lifecycle and its `IsCurrent` invariant, versus squad membership.

