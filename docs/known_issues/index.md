# Known Issues & Past Fixes

Avoid repeating these mistakes:

- [EF Core](ef-core.md) — UNIQUE constraints on save, value converters, the DateInSqlInterceptor, migration ordering and the folded `InitialCreate`, a migration history that claims more than the file holds, connection pooling holding a file handle open, cross-context transactions.
- [Data / domain](domain.md) — archiving instead of deleting, the substitution tie-break, period length in seconds, why `PlayerService.GetAllAsync` still returns archived players.
- [Blazor / MudBlazor 9.x](blazor-mudblazor.md) — the per-page render mode and static layout, `InteractiveShell`, dialogs/popovers/snackbar providers, and a run of MudBlazor-specific traps.
- [Touch / PWA](touch-pwa.md) — touch-target sizing, the date picker on a phone, dialog sheets, a card's action row swallowing taps, drag-and-drop shims, and circuit reconnection after backgrounding.
- [Localization](localization.md) — resource-key homographs and the case-insensitivity trap in `ServiceOperation` action phrases.
- [Blazor components](blazor-components.md) — `section` as a reserved word, the SDK-dependent build gap, `@inherits` in the `.razor` file, enhanced navigation sending the destination as the `Referer`, generic dialog results.
- [Result](result.md) — cancellation as a messageless failure, the load-bearing catch filter, redirect-on-cancel, failure message templates.
- [Formation/Pitch](formation-pitch.md) — duplicate enum positions are intentional, `dvh` vs `vh`, chips that scale with the pitch.
- [CSS scoping](css-scoping.md) — a class with no owning `.razor.css` silently does nothing, including on a MudBlazor component's root, and a name shared with a global `app.css` rule cuts the same way round.
- [Live match](live-match.md) — swaps that write no row, the halves-only clock, and how a goal's minute is derived rather than stored.
- [Authentication](authentication.md) — `IsPersistent` vs `ExpireTimeSpan`, `SameSite`, data-protection application name, and why `OnValidatePrincipal` doesn't revoke a circuit.
- [General](general.md) — the published-app working-directory trap, `.count()` failing open in Playwright, dirty state after a CI retry, path separators in the coverage report on Windows, the CRLF the Windows clipboard hands back, `color-mix` breaking the html2canvas export, and the September-only disagreement between JavaScript's `en-GB` and .NET's `en`.
