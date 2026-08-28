---
name: razor-pages-and-circuit
description: Building or changing a Razor page, dialog or component in FootballFormation.UI. Covers code-behind and the CS0263 @inherits rule, AppRoutes/AppNav/PageHeader, Trail.Redirect, SeasonAwarePage and CancellableComponent, and the circuit-lifecycle leaks. Use for any .razor or .razor.cs work.
---

# Razor pages and the circuit

## Decide the render mode before anything else

**Most pages have no circuit.** `@rendermode InteractiveServer` is declared per page and never on
`<Routes>` or `<HeadOutlet>`. A new page gets one **only if it needs a server-side event handler** —
a dialog, a snackbar, `@bind`, JS interop, a timer, a `LiveMatchNotifier` subscription. Navigation
is not a reason: an anchor does that with no circuit at all.

Today's split: `/`, `/games`, `/players`, `/games/{id}/formation`, `/games/{id}/live`,
`/games/{id}/result`, `/settings` and `/users` are interactive. `/stats`, `/stats/positions`,
`/players/{id}/stats`, `/games/{id}/overview`, `/login`, `/Error` and `/not-found` are not, and
`rendermode.spec.js` asserts they open no WebSocket. That is the whole point: a page with no circuit
cannot show "Reconnecting…", cannot force a reload, and survives a phone suspending the app.

**A page that declares a render mode opens with `<InteractiveShell />`** (or
`<InteractiveShell AdminOnly="true" />` where it also has `[Authorize(Roles = AppRoles.Admin)]`).
That carries the MudBlazor providers and the revocation gate, which the layout can no longer supply:
`MainLayout` renders **statically for every page**, because `RouteView` applies it outside the
island and a layout cannot carry a render mode at all.

**On a static page:**
- `ISnackbar` reports into nothing. Use `PageNotice` + `<InlineNotice Notice="_notice" />`.
- `IJSRuntime` and `OnAfterRenderAsync` never run. Give the work to a plain `onclick` and a script
  that owns its own failure message — `js/screenshot.js` is the worked example.
- `CancellableComponent` and `SeasonAwarePage` still compile and are largely inert. Harmless.
- A `<div @onclick>` standing in for a link is just a link. Write the anchor.

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
  alive and re-entered for every future match. Copy `Home` and `LiveMatch`, the two that subscribe.
- **A callback arriving from outside the circuit** (`LiveMatchNotifier`) must re-enter through
  `InvokeAsync` before touching component state or calling `StateHasChanged`.
- The chrome no longer subscribes to anything, which is most of this hazard gone: it renders
  statically, so there is no circuit for it to leak into.

## UI state services

`SeasonState` holds the selected season; `NavigationTrail` answers where the visitor came from. Both
are `Scoped`, and since the render-mode split **a page has two scopes**: the static render of the
chrome, and the circuit behind an interactive page's island.

- Both read `RequestContext`, which the host fills in from the **cookies** on the request that
  created the scope. That works in either scope because a circuit is created *during* the
  `/_blazor` request, which carries the same ones. **Never reach for the `Referer` header instead**:
  enhanced navigation pushes the destination into history before it fetches, so the referrer names
  the page being loaded. The trail is the `ff.trail` cookie, written by a middleware in `Program.cs`.
- Loading is a **memoized task** (`EnsureLoadedAsync() => _loading ??= LoadAsync()`), because a scoped
  service cannot load in its constructor and the layout and page both need the data during their own
  `OnInitializedAsync`.
- **Choosing a season is a navigation, not an event** — a link to `/season/set`, which stores the
  cookie and redirects back. There is no `OnChanged` to subscribe to; `SeasonAwarePage` exists only
  to await the load before the page's first query.
- Any link that changes per-request state needs `data-enhance-nav="false"`, or an island that is
  already up keeps the old value while the chrome around it shows the new one.
- The state holds a **view** choice and never writes shared data — the picker is reachable by
  anonymous visitors, so it must not touch `Season.IsCurrent`.

## A generic dialog result cannot tell `default` from "cancelled"

`PromptAsync<TDialog, TResult>` is constrained to `class` for that reason. A dialog closing with `0`
is otherwise indistinguishable from Cancel; a value-typed dialog needs its own helper handing back
`TValue?`.

Detail: [docs/ui_components/](../../../docs/ui_components/index.md) ·
[docs/patterns/](../../../docs/patterns/ui-state-and-navigation.md#ui-state-services)
