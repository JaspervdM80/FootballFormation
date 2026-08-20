# Patterns & Conventions

- [Result and Cancellation](result-and-cancellation.md) — the `Result`/`Result<T>` pattern, `ServiceOperation.RunAsync`, and cancellation as the third outcome.
- [Transactions and Writes](transactions-and-writes.md) — when two rows have to agree, one context writes both; the one multi-save write left on purpose.
- [Service Structure and Domain Logic](service-structure.md) — logging, no interfaces for services, splitting a long service by use case, and what belongs on the model.
- [EF Core Conventions](ef-core.md) — include chains, value converters, migration ordering, and why migrations are one file.
- [UI State and Navigation](ui-state-and-navigation.md) — `SeasonState`, `NavigationTrail`, the season cookie, and URL/redirect conventions.
- [Service Registration](service-registration.md) — the DI lifetime table and why two services are singletons.
- [Authorization and Authentication](authorization-and-auth.md) — the service-boundary auth rule, minute-figure visibility, the sign-in cookie settings, and revoking authority mid-circuit.
- [Blazor Rendering](blazor-rendering.md) — render mode and layout, in brief.
