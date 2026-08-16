---
name: razor-pages-and-circuit
description: Building or changing a Razor page, dialog or component in FootballFormation.UI. Covers code-behind and the CS0263 @inherits rule, AppRoutes/AppNav/PageHeader, Trail.Redirect, SeasonAwarePage and CancellableComponent, and the circuit-lifecycle leaks. Use for any .razor or .razor.cs work.
---

# Razor pages and the circuit

## File shape

Pages use `.razor` + `.razor.cs` code-behind partial classes.

**A page's base class goes in the `.razor` as `@inherits SeasonAwarePage`, never on the partial
class.** Putting `: SeasonAwarePage` on the `public partial class` gives *CS0263: Partial declarations
must not specify different base classes*, because the generated Razor partial already declares
`: ComponentBase`.

**`section` is a reserved word in a `.razor` file.** `@foreach (var section in …)` then
`@section.Title` parses as the `@section` *directive* and fails with `RZ2005`. Name the variable
something else, or parenthesise as `@(section.Title)`.

**A `RenderFragment` in code-behind** needs the `=> __builder =>` lambda pattern in an `@code` block;
a regular method will not do.

## Navigation — three rules and the whole thing holds

1. **Build URLs from `AppRoutes`** (`AppRoutes.PlayerStats(id)`), never an interpolated literal. The
   `@page` directives are the one exception — Razor needs a compile-time constant, so `AppRoutes`
   mirrors them by hand.
2. **A page's name lives once**, in `AppNav.PageNameKey` — a localization key matched by pattern on
   the path segments. It names the menu entries *and* fills in "Back to {0}". It returns `null`
   outside the app's routes, which is how the back arrow knows not to offer one.
3. **The menu is `AppNav.Menu`**, rendered by `<NavItems />` in both app bar and drawer.

**Redirect away from a page that failed to load with `Trail.Redirect(...)`, not `NavigateTo`.** It
replaces the failed page in the trail and in browser history, so neither back button walks straight
back into it.

## Every page opens with `<PageHeader>`

Do not hand-roll a header row. `Title`/`TitleContent` and `Subtitle`/`SubtitleContent` take a string
for plain text or a fragment for markup.

**A fragment is compiled into the *calling* page**, so that page's scoped CSS reaches inside it.
Anything on an element `PageHeader` itself renders — the wrapper, the heading — needs a rule in
`app.css`; that is what `Class` and `TitleClass` are for.

`<PageTitle>` stays on the page: several deliberately differ from the heading (`/players` is titled
"Players" but headed "Squad").

## Reads stop when the visitor leaves

Blazor Server gives a component no request lifetime of its own, so a page navigated away from leaves
its query running against the volume with nobody to render it. `CancellableComponent` owns a
`CancellationTokenSource` cancelled on disposal and exposes `Cancellation`; `SeasonAwarePage` inherits
it.

```csharp
var result = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
_games = Snackbar.ReportFailure(L, result) ? result.Value : [];
```

- The token goes on **reads**. Writes deliberately get `default`.
- **Check `IsCancelled` before anything the visitor would notice — a redirect above all.**
  `if (result.IsCancelled) return;` goes *ahead of* the not-found branch. Without it, abandoning a
  load bounces the visitor off the page they just navigated to. `/games/{id}/result`,
  `/games/{id}/formation`, `/games/{id}/overview` and `/players/{id}/stats` carry the check.
- **Overriding `Dispose` means calling `base.Dispose()`**, or the reads outlive the component again.

## Circuit-lifecycle leaks are cross-user, not per-page

A circuit outlives a request and a singleton outlives the circuit.

- **Every `+=` needs its `-=` in `Dispose`**, and the component must actually implement `IDisposable`.
  `LiveMatchNotifier` is a **singleton** — a handler never removed keeps a dead circuit's component
  alive and re-entered for every future match. Copy `SeasonPicker`, `MainLayout`, `SeasonAwarePage`.
- **A callback arriving from outside the circuit** (`LiveMatchNotifier`, `SeasonState.OnChanged`) must
  re-enter through `InvokeAsync` before touching component state or calling `StateHasChanged`.

## UI state services

`SeasonState` holds the selected season; `NavigationTrail` holds where the visitor has been. Both are
`Scoped`, so on Blazor Server they live for the circuit — a choice survives navigation within a tab
but resets on refresh.

- Loading is a **memoized task** (`EnsureLoadedAsync() => _loading ??= LoadAsync()`), because a scoped
  service cannot load in its constructor and the layout and page both need the data during their own
  `OnInitializedAsync`.
- Season-aware pages **do not wire up `OnChanged` themselves** — they inherit `SeasonAwarePage`, which
  awaits the load, subscribes, re-runs `LoadAsync()` inside `InvokeAsync` on change, and unsubscribes
  on dispose. Override `LoadAsync()`; use `OnInitializedCoreAsync()` for one-time setup.
- The state holds a **view** choice and never writes shared data — the picker is reachable by
  anonymous visitors, so it must not touch `Season.IsCurrent`.
- `NavigationTrail.Start()` is called by `MainLayout.OnInitialized`, before any page renders: a scoped
  service is not constructed until something injects it, and if the first injector were a detail
  page's back button the navigation that led there would already have been missed.

## A generic dialog result cannot tell `default` from "cancelled"

`PromptAsync<TDialog, TResult>` is constrained to `class` for that reason. A dialog closing with `0`
is otherwise indistinguishable from Cancel; a value-typed dialog needs its own helper handing back
`TValue?`.

Detail: [docs/ui_components.md](../../../docs/ui_components.md) ·
[docs/patterns.md](../../../docs/patterns.md#ui-state-services)
