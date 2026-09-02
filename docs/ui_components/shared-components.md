# Shared Components

## Localization
- Dutch is the default culture; English is the fallback (and the switcher's second option)
- All user-facing strings go through `IStringLocalizer<Strings>` (`L`); **the English text
  is the resource key**, so only `Strings.nl.resx` exists — missing keys render as English
- Language switcher: globe menu in `MainLayout` → `/culture/set` endpoint → culture cookie
  → full page reload (circuit culture is fixed at startup)
- Known limitation: `Result.Error` messages from Core services are English


## InstallBanner (PWA install prompt)
- `Components/InstallBanner.razor`, rendered once in `MainLayout`. **Markup only** — the server
  renders it `hidden`, with the localized strings for every branch already in it, and `js/pwa.js`
  decides whether to show it and wires the two buttons.
- That split is not a preference: every input to the decision (`display-mode`, the user agent, a
  dismissal in `localStorage`, whether Chrome ever fired `beforeinstallprompt`) is known only to the
  browser, and the banner renders in the layout, which is statically rendered on every page and so
  has no circuit to ask over. There is no `.razor.cs` and no JS interop left.
- Shows on mobile browsers when the app is not installed (standalone) and not previously dismissed
  (localStorage `pwa-install-dismissed`)
- Android: the button triggers the native install prompt from the captured `beforeinstallprompt`
  event; falls back to ⋮-menu instructions if there is none
- iOS: no install API exists — shows "Tap Share, then Add to Home Screen" text instead
- The strings for the branches the server cannot pick between ride along as `data-instruction-*`
  attributes, so the resx stays the one place a translation lives.


## A page stops reading when the visitor leaves (`Components/CancellableComponent.cs`)
Blazor Server gives a component no request lifetime of its own. A page that starts a query in
`OnInitializedAsync` and is then navigated away from leaves that query running against the SQLite
volume with nobody left to render it — and on the phone-on-a-bad-connection this app is built for,
circuits drop constantly.

`CancellableComponent` is the seam: it owns a `CancellationTokenSource` cancelled on disposal and
exposes `Cancellation`. Every page or dialog that reads inherits it (`SeasonAwarePage` does, so its
four pages get it for free), and the token goes on every service **read**:

```csharp
// .razor — the base class goes here, never on the partial class (CS0263)
@inherits CancellableComponent

// .razor.cs
var result = await GameService.GetAllWithDetailsAsync(SeasonId, Cancellation);
_games = Snackbar.ReportFailure(L, result) ? result.Value : [];
```

- **Writes deliberately get no token.** An admin who taps "finish match" and then loses their
  circuit must still have finished the match. See [patterns](../patterns/result-and-cancellation.md#cancellation-the-third-outcome).
- **The `ReportFailure` line does not change.** A cancelled read comes back as
  `Result.IsCancelled`, and `UiFeedback` keeps quiet about one — otherwise leaving a page would
  raise "Failed to load games" on the page the visitor went to, since the snackbar belongs to the
  circuit rather than to the page that made the call.
- **What a caller must add is a check before anything the visitor would notice — a redirect above
  all.** `if (result.IsCancelled) return;` goes ahead of the not-found branch on `/games/{id}/result`,
  `/games/{id}/formation`, `/games/{id}/overview` and `/players/{id}/stats`. Without it, abandoning
  one of those loads bounces the visitor off whichever page they had just navigated to.
- **Overriding `Dispose` means calling `base.Dispose()`** — `Home` and `LiveMatch` both do, to
  unsubscribe from `LiveMatchNotifier` (and to stop the clock timer) before the base cancels.


## Venue badge (`Components/VenueBadge.razor`)
THUIS/UIT, shown on the games list, the player statistics and both of the formation builder's
headers. A component rather than four copies of `(IsHomeGame ? L["Home"] : L["Away"]).ToUpper()`,
because the wording, the casing and the colour have to agree across all four and they had already
drifted once.

- **The colours are the match overview's own**: `--club-accent` for home, `--color-away` for away,
  tinted the way every other badge in `app.css` is — 12% fill, 20% border. Those are the two
  colours a match card already stripes its edges with, so the stripe and the word are one
  convention instead of two.
- **The text takes the deep end of each ramp**, `--club-accent-deep` and `--color-away-bright`, and
  this is the part that is easy to get wrong: contrast is against the badge's own 12% fill, not the
  card underneath, and that fill lightens the background more than it looks. The colours the larger
  badges use measure **4.31:1** (home) and **4.02:1** (away) over it on `--surface-card`, and worse
  on `--surface-card-alt` — both under AA for text this small. The deep pair clears it on every
  surface the badge lands on: 5.27 / 4.88 home, 5.22 / 4.82 away.
- `.badge-venue` is **shape only** — the colour lives in `.badge-venue-home` / `.badge-venue-away`,
  and the component always emits one of them. Nothing renders the base class alone.
- **`Inline="true"`** where the badge trails a name (the games list, the player statistics) rather
  than standing in a page header: `.badge-venue-inline` drops it to 0.6rem and adds the 8px lead.
  At header size the box is taller than the 0.82rem name it annotates and reads as the louder of
  the two.
- The class string is built in `@code`, not in the attribute: a double-quoted string inside an
  `@()` inside a double-quoted attribute value is a Razor parse error, not a style preference.


## The drawer is a checkbox (`Layout/MainLayout.razor`)
The app-bar sections are also a drawer — the only navigation below 700px, and the overflow above it
— and its open state is an `<input type="checkbox" id="nav-drawer">` at the top of `MudLayout`, not
a bool on a component.

- **No circuit and no script.** The layout is statically rendered on every page, so there is nothing
  to hold a `bool` or handle an `OnClick`; and a drawer that depended on a script would fail exactly
  the way `../known_issues/touch-pwa.md` records for an empty `blazor.web.js` — silently, and completely. `.app-drawer`
  and `.drawer-scrim` slide in from `.nav-drawer-toggle:checked ~ …` rules in app.css.
- The checkbox is **visually hidden but still focusable** (`clip-path`, not `display: none`): it is
  the control a keyboard or screen reader gets, and it carries the "Menu" label. The hamburger and
  the scrim are `<label for="nav-drawer">` — affordances with no semantics of their own.
- **The closed drawer is `visibility: hidden`**, delayed to the end of the slide so the transform
  still animates out. Parked off-screen hides it from the eye and from nothing else: without this it
  is a run of tab stops nobody can see, and a second reading of the whole menu to a screen reader —
  on every width, now that the drawer is on every width.
- **The hamburger and the drawer are on every width**, not only below 700px: above the breakpoint the
  bar shows the nav links that fit and clips the rest, so the drawer is the one place the whole menu
  is always reachable. See [../theming.md](../theming.md), "The app bar sizes its own nav".
- **The one thing JS still does**: enhanced navigation patches the DOM rather than replacing it, so
  the checkbox survives a navigation with `checked` still set and the drawer would stay open over
  the new page. `js/pwa.js` unchecks it on Blazor's `enhancedload`. If that never runs the only cost
  is a drawer that stays open across a navigation.


## Season picker (`Components/SeasonPicker.razor`)
The global season filter, backed by the scoped `SeasonState` (see
[patterns](../patterns/ui-state-and-navigation.md#ui-state-services)).

- **Rendered twice from one component**: in the app bar right after `<MudSpacer />`, and inside the
  mobile drawer above the nav menu. The `Compact` parameter shortens the app-bar label to "25/26"
  (`Season.ShortName`) while the drawer shows the full "2025/26".
- **Below 700px only the drawer copy shows** — `.mud-appbar .season-picker` is hidden, because the
  app bar is already carrying the hamburger, title, language menu and login on a phone. The rule
  targets `.mud-appbar` specifically; the drawer instance lives in `.drawer-season-picker`.
- **A `<details>` disclosure of plain links, not a `MudMenu`.** It renders in the layout, which is
  statically rendered on every page, so there is no circuit to open a popover from and no handler to
  dispatch a click to — and a disclosure arrives keyboard- and screen-reader-correct for free.
  Choosing a season is a navigation to `/season/set` (`AppRoutes.SetSeason`), which stores the
  cookie and redirects back; the language switcher next to it works the same way.
- It loads the season list itself (`SeasonState.EnsureLoadedAsync()` in `OnInitializedAsync`) —
  otherwise it would render nothing on the start page, where no page loads seasons. The call is
  memoized, so on the season-aware pages it shares the page's own query rather than adding one.
- Its CSS lives in `Web/wwwroot/app.css`, **not** scoped CSS, precisely because it renders from two
  places — isolation would force a duplicate stylesheet, the problem `.stat-tiles` already has
  between `SeasonStats.razor.css` and `PlayerStats.razor.css`.
- **Route allowlist**, now `AppNav.IsSeasonAware` rather than a copy of the route list living here:
  `/games`, `/stats`, `/stats/positions`, `/players` (the squad is per season), `/players/{id}/stats`,
  and `/` — the start page filters nothing, but it is where a visit begins, so the season can be set
  before navigating. Hidden on `/settings`, where it would be misleading while the season list itself
  is edited, and on the single-game routes, where it is inert. Visibility is recomputed on every
  render, and every navigation is a render, so nothing has to watch for one.
- Selecting a season **never** writes `Season.IsCurrent` — the picker is reachable by anonymous
  visitors, and `IsCurrent` is shared state owned by the admin on `/settings`. It goes in a cookie
  for eight hours instead, which is per-browser, which is the scope a view choice belongs at — see
  [patterns](../patterns/ui-state-and-navigation.md#ui-state-services).


## Navigation: routes, menu, page headers
Everything that knows a URL lives in `UI/Navigation/`. Three rules, and the whole thing holds:

1. **Build URLs from `AppRoutes`** — `AppRoutes.Games`, `AppRoutes.PlayerStats(id)` — in pages,
   in `Href` attributes, everywhere. Not an interpolated literal. The `@page` directives are the
   one exception: Razor needs a compile-time constant, so `AppRoutes` mirrors them by hand.
2. **A page's name lives once**, in `AppNav.PageNameKey` — a localization key, matched by pattern
   on the path segments. It names the menu entries *and* fills in "Back to {0}", so a page is
   called the same thing wherever it is referred to. It returns `null` for anything outside the
   app's routes (`/login`, `/not-found`), which is how the back arrow knows not to offer it.
3. **The menu is `AppNav.Menu`**, rendered by `<NavItems />` in both the app bar and the drawer
   (`ShowIcons="true"` there). Adding an item is one line. There is deliberately **no Start item**:
   the club-and-team title is already a home link in both places.

### The app's own name comes from the current team
`TeamState` (scoped, memoized like `SeasonState`) reads `TeamService.GetCurrentAsync()` once per
request and hands out two things: `DisplayName` — `Club.Name` + `Team.Name`, "GJS MO15-2" — and
`LogoUrl`, the club's crest or its theme's when it has none (`ClubTheme.LogoFor`, the one place that
fallback lives).

Six places used to spell the name out and now read it from there: the app-bar title, the drawer
title, `/`'s `<PageTitle>` and its `PageHeader`, the install banner, and
`apple-mobile-web-app-title` in `App.razor`. The seventh, `manifest.webmanifest`, is no longer a
file at all — `Routing.MapMinimalApi` generates it, because a static copy would be the one place a
rename never reached and it is the name that ends up under the icon on a home screen. The icons in
it stay files, so a crest swap is still a file drop.

`App.razor` is what triggers the load: the head renders before `MainLayout`, and everything below
awaits the same memoized task.

The app-bar crest is an `<img>` now rather than the `--club-logo` background it was — the crest
belongs to a club, and a club is a database row. `--club-logo-bg` still paints the chip behind it,
so a transparent crest keeps its plate.

### PageHeader (`Components/PageHeader.razor`)
Every page opens with one — heading, optional subtitle, optional back arrow, optional actions.
Do not hand-roll a header row.

- `Title` / `TitleContent` and `Subtitle` / `SubtitleContent`: string for plain text, fragment when
  the page needs its own markup. **A fragment is compiled into the calling page**, so that page's
  scoped CSS reaches inside it. Anything on an element *PageHeader* renders — the wrapper, the
  heading — needs a rule in `app.css`; that is what `Class` and `TitleClass` are for, and why
  `.result-header`, `.result-opponent`, `.result-subtitle` and `.builder-opponent` moved there.
- `Meta` sits beside the heading (the formation builder's venue/date/formation badges); `Actions`
  is pushed right by a `MudSpacer` (the Add button on `/games`, the squad actions on `/players`).
- `TitleTypo` is `h4` on top-level pages, `h5`/`h6` on detail pages. `Class` carries the page's
  bottom margin — the existing `mb-2`/`mb-3`/`mb-4`/`mb-6` were kept as they were, so unifying
  the vertical rhythm is still an open, separate job.
- `<PageTitle>` stays on the page. Several deliberately differ from the heading (`/players` is
  titled "Players" but headed "Squad"), and on the detail pages it sits above the loading guard so
  the browser tab is right before the data arrives.

### Back arrow (`Components/BackButton.razor`)
Rendered by `PageHeader` when you pass `BackFallback`. It returns to the page the visitor **came
from** — `NavigationTrail`, see [patterns](../patterns/ui-state-and-navigation.md#ui-state-services) — and names that
destination in `aria-label` + `title` ("Terug naar Seizoen"). Icon-only by design; the tooltip is
the whole affordance, which is why the label and the destination are resolved from one expression.

**A plain `<a href>` styled by `.back-button` in app.css**, not a `MudIconButton` with an `OnClick`:
it renders on pages that have no circuit to dispatch a click to. The rule lives in app.css because
the element `MudIcon` renders is out of reach of a page's scoped stylesheet.

`BackFallback` is used when there is nothing behind — a shared link, a bookmark, a page reached from
one the route table cannot name — **and on every interactive page**, because a circuit's scope holds
the trail it was created with however far the visitor has navigated through it since (see
[patterns](../patterns/ui-state-and-navigation.md#ui-state-services)). Pick the page someone landing
cold most likely wants: `/players` for player stats, `/games` for the game screens, and for
`/games/{id}/overview` the editor for an admin, `/games` for a visitor.

**It used to be the answer on the static pages too, on every visit**, because the trail read the
`Referer` header and Blazor's enhanced navigation sends the destination as the referrer — so a
fallback that happened to be right was indistinguishable from a trail that worked. An assertion that
a back arrow followed the visitor therefore has to be made on a page with no circuit *and* whose
fallback is not where they came from: `/players/{id}/stats` opened from `/trainings` is the one in
`trainings.spec.js`.


