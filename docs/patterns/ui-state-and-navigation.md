# UI State and Navigation

## UI state services
Two of these: `SeasonState` (`UI/State/SeasonState.cs`) holds the selected season, shared by
`MainLayout`'s picker and the season-aware pages; `NavigationTrail` (`UI/Navigation/`) holds where
the visitor has been. The pattern, taking `SeasonState` as the worked example:

- Registered `Scoped`, which since the render-mode split means **one per scope, and a page has
  two**: the static render of the chrome, and the circuit behind an interactive page's island. Both
  read the same cookie off their own request, so they cannot disagree.
- **The choice is kept in a cookie for eight hours**, through `SeasonPreference`
  (`UI/State/SeasonPreference.cs`). A circuit does not survive a deploy, and a merge to `main` *is*
  a deploy onto the one Fly machine, so before this everyone watching a match came back to
  whichever season the database calls current. Eight hours is a match day rather than a
  subscription — a cookie carries its own expiry, which is why it is a cookie and not
  `localStorage`.
- **Both halves go through a request now.** Writing is `/season/set`, a GET beside `/culture/set`
  that appends the cookie and redirects back — a response to put a `Set-Cookie` on, which a live
  circuit never had. The picker used to be a handler that asked the browser to write the cookie over
  JS interop, for exactly that reason; it is a link. Reading: **every scope a page renders in was
  created by a request that is already carrying the cookie**, so `RequestContext`
  (`UI/State/RequestContext.cs`, filled in by `Program.cs` from `IHttpContextAccessor`) hands it to
  `SeasonState`'s constructor and nobody asks the browser for anything.
  - **A static render and a circuit are different DI scopes, and that is fine here** — the static
    render has the page request, and a circuit is created *during* the `/_blazor` request, which
    carries the same cookies. Both scopes therefore resolve the same season independently. This
    replaced a root-component parameter on `Routes` that serialised the value into the component
    marker; that worked, but only while `Routes` was itself an interactive root.
  - **Do not reach for the browser instead.** It puts a round trip in front of the first
    interactive render *and* leaves the static pass painting a season the cookie had already
    overruled, so the page visibly changes season after connecting.
  - **A stored season that no longer exists is ignored**, falling back to the current one.
    Restoring it would filter every page down to nothing behind a picker that cannot name what it
    is showing.
  - **"All seasons" is stored as the literal `all`**, because that choice is `null` in C# and
    `null` is also how "nothing stored" has to read.
- **An unparseable `season` is simply not stored**, the same way an absent cookie and a hand-edited
  one already read alike — a bad query string lands the visitor back where they were, on the season
  they already had.
- `tests/ui/specs/season.spec.js` covers all of it, including a fetch of the prerendered HTML —
  the flash and the round trip are invisible to an assertion made on the settled page.
- Loading is a **memoized task** (`EnsureLoadedAsync() => _loading ??= LoadAsync()`). A scoped
  service can't load in its constructor, and the layout and the page both need the data during
  their own `OnInitializedAsync`, where they interleave at the first `await`. This was once
  load-bearing — the services shared one scoped `AppDbContext` and a second concurrent query threw
  *"A second operation was started on this context."* They take a short-lived context per operation
  now (`AddDbContextFactory`), so it is purely an optimisation: forgetting it costs a duplicate
  query, not a crash.
- **There is no change notification, because a season change is a navigation.** The picker is a
  link to `/season/set`, which stores the cookie and redirects back; the page that comes back is a
  fresh render that read the new choice off its own request. Season-aware pages inherit
  `SeasonAwarePage` (`UI/Components/SeasonAwarePage.cs`) only to await the load before their first
  query. Override `LoadAsync()`; use `OnInitializedCoreAsync()` for one-time setup such as reading
  the auth state. Declare the base with `@inherits SeasonAwarePage` in the `.razor` — Razor owns the
  base class, so putting it on the code-behind is a CS0263 error.
- **The picker's links opt out of enhanced navigation** (`data-enhance-nav="false"`), as the
  language switcher's do. Enhanced navigation patches the DOM without making a new circuit, so an
  island already up would keep the old season while the chrome around it showed the new one.
- The state holds a **view** choice and never writes shared data. The picker is reachable by
  anonymous visitors, so it must not touch `Season.IsCurrent`, which is admin-owned on `/settings`.

`NavigationTrail` answers the same question from the other end, and it is a cookie for the same
reason: **`ff.trail`, the last two pages served, newest first**, written by a middleware in
`Program.cs` on every 200 `text/html` GET and read back off `RequestContext`.

**The browser's `Referer` cannot answer this, which is what the trail used to read.** Blazor's
enhanced navigation pushes the destination into history *before* it fetches the page, so the header
on that fetch names the page being loaded rather than the one being left — and every in-app link is
an enhanced navigation. `NavigationTrail` saw the current path, took its "nothing behind us" branch,
and every back arrow in the app silently fell through to its `Fallback`. See
[known_issues](../known_issues/blazor-components.md).

Two entries rather than one, for two cases that both put a useless page at the front: a **refresh**,
whose request carries the cookie the page itself wrote, and a **`/login` or `/not-found`** the route
table cannot name. `Previous` skips any entry that is the current path or that `AppNav.PageNameKey`
cannot name, and takes the first that survives.

**A circuit never asks.** Its scope is created once and outlives every enhanced navigation made
through it, so the `RequestContext` it holds is the one the circuit *started* on — right for the
first page and stale from the second onwards (`/players` → `/games` → a formation offers `/players`).
So `BackButton` consults the trail only where `AssignedRenderMode is null`, and an island takes its
`Fallback`. That is checked on the component rather than in `NavigationTrail` because only a
component knows which of the two it is rendering in, and `AssignedRenderMode` reads the same in the
prerender as in the circuit — so the arrow does not change destination under a thumb when the
circuit connects. The three pages in that position — the builder, the live screen, the match result
— are reached from `/games`, which is what their fallback already says.

One thing a cookie cannot do that the header could: **it is one per browser, not one per tab.** Open
a link in a second tab and the page it lands on rewrites the trail the first tab reads on its next
navigation. Two entries and a single-page depth keep the damage to a back arrow offering a sibling
page; nothing server-side can be per-tab, so this is accepted rather than solved.

Two related rules for anything that navigates:

- Build URLs from `AppRoutes` (`AppRoutes.PlayerStats(id)`), never an interpolated literal.
- Redirect away from a page that failed to load with `Trail.Redirect(...)`, not `NavigateTo`. It
  replaces the failed page in browser history, so the browser's own back button does not walk
  straight back into it. The failed page has already written itself to `ff.trail` and stays there as
  the second entry — harmless, because the page redirected *to* is then the first, and it is the one
  a back arrow offers.

