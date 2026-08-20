# Blazor / MudBlazor 9.x

- **The render mode is per page, and the layout is static for all of them.** `<Routes>` and
  `<HeadOutlet>` carry none; eight pages declare `@rendermode InteractiveServer` and the rest are
  plain server HTML (see architecture.md). What surprises people is the layout: `RouteView` applies
  it **outside** the page's interactive island, and a render mode cannot be put on a layout at all —
  `@Body` is a `RenderFragment`, and Blazor refuses to serialise one as a root component parameter
  (*"Cannot pass the parameter 'ChildContent' to component with rendermode
  'InteractiveServerRenderMode' … arbitrary code and cannot be serialized"*). So `MainLayout`
  renders statically even on `/games/{id}/live`. Everything the chrome does is written for that: a
  checkbox drawer, `<details>` pickers, and links to `/culture/set` and `/season/set` instead of
  handlers. **Anything a page needs that the layout used to supply has to come down into
  `<InteractiveShell />`** — today the MudBlazor providers and the revocation gate below.
- **A statically rendered page has no snackbar.** `ISnackbar` needs `MudSnackbarProvider`, which
  needs an interactive render mode, so a `Snackbar.ReportFailure` on one of those pages reports into
  nothing at all — silently. Use `PageNotice` + `<InlineNotice>`, which put the message on the page.
  A message still cannot survive a redirect there, the way a circuit's snackbar could: the
  "game not found" line on `/games/{id}/overview` was dropped rather than reinvented, and the log
  keeps the id.
- **`[Authorize]` on a route stops being re-checked once the page is up.** `AuthorizeRouteView` is
  in the static router, so it is evaluated per request and hears nothing afterwards — where
  `RevalidatingUserAuthenticationStateProvider` goes on checking for as long as the circuit lives.
  Before the split, revoking an account booted the tab within the revalidation interval; after it,
  nothing did. `<InteractiveShell AdminOnly="true" />` puts the same `NotAuthorized` branch inside
  the island on the admin-only pages, which is what `session.spec.js` pins. The public pages need no
  gate — losing the role there re-renders their `AuthorizeView` blocks into the visitor view, which
  is right, and redirecting a visitor who was never signed in would be plainly wrong.
- **Enhanced navigation does not make a new circuit, so anything fixed at scope creation goes
  stale.** It fetches the new page and patches the DOM; an island that is already up keeps its
  circuit, and with it the culture it started in and the season it read off `/_blazor`. Left
  enhanced, the language switcher repainted the static chrome in English over a page still rendering
  in Dutch. Both the culture links and the season links carry `data-enhance-nav="false"`, which is
  the same opt-out the logout form has always used. **Any new link that changes per-request state
  needs it too.**
- **`FocusOnNavigate` is gone**, not moved: it only ever worked inside an interactive router, and a
  static one leaves it a no-op. Moving focus to the `h1` after a navigation is a real accessibility
  affordance and this change lost it — worth restoring with something that works under enhanced
  navigation, but markup that looks like it moves focus and does not is worse than none.
- **Dialogs not showing**: `MudDialogProvider` must be inside an interactive render mode, and so
  must `MudPopoverProvider` and `MudSnackbarProvider`. They used to sit in `MainLayout`, which
  worked while `<Routes>` carried `@rendermode="InteractiveServer"` and every page was
  interactive. **A layout cannot carry a render mode** — `@Body` is a `RenderFragment` and Blazor
  cannot serialise one as a root component parameter — and it is applied by `RouteView` *outside*
  a page's interactive island, so once the render mode came off `<Routes>` the layout became
  static for every page, interactive ones included. The three live in `<InteractiveShell />` now,
  rendered by each page that opens a dialog, a popover or a snackbar. `MudThemeProvider` stays in
  the layout: MudBlazor separated theming from the popover provider precisely so it can.
- **`Position` enum ambiguity**: Renamed to `PlayerPosition` because `MudBlazor.Position` exists.
- **`MudForm.Validate()` is obsolete**: Use `ValidateAsync()`.
- **`ShowMessageBox` removed**: Use custom `ConfirmDialog` component instead.
- **Multi-select binding**: Use `IReadOnlyCollection<T>` not `IEnumerable<T>`.
- **`RenderFragment` in code-behind**: Use `=> __builder =>` lambda pattern in `@code` block; can't use regular methods.
- **Dropdowns rendered as a full-width band across the page**: `MudPopover` carries `.mud-paper`, and app.css's card rule set `position: relative` on it — same specificity as MudBlazor's `.mud-popover{position:absolute}` but later in source order, so it won. A relatively positioned block fills the popover provider's width and treats the placement JS's `left`/`top` as an offset from its static spot at the top of the page. Fixed twice over: first by patch rules putting `position` back, and now — the current state — by the card rule never claiming a popover in the first place, `.mud-paper:not(.mud-popover):not(.mud-dialog)` in app.css. The patch rules are gone, so MudBlazor's own positioning is never disturbed and there is nothing left to restore. Watch for this whenever a global `.mud-*` rule touches layout: excluding the popover beats overriding it back.
- **An open dialog locks the page by shrinking `<body>`, which breaks any geometry you measure from
  the DOM.** MudBlazor adds `scroll-locked-no-padding` — `overflow: hidden` on a `<body>` whose box
  is then *shorter than the viewport*. Walk an element's ancestors intersecting every clipping box,
  the obvious way to work out what is actually on screen, and the answer is that every dialog and
  popover in the app is half scrolled out of view. It is not: an ancestor's overflow only clips a
  descendant it is a containing block for, and MudBlazor's dialog container is `position: fixed`, so
  a plain `overflow: hidden` on `<body>` cannot clip it. `scripts/touch-targets.mjs` carries that
  rule (`containsFor`); it was worth about an hour of believing the buttons were off screen.
- **The date picker's month header slides, so it reads the month you just left.** The element is a
  `.mud-picker-slide-transition`, and for a moment after *Previous month* it still reports the old
  text. A loop that clicks and then immediately re-reads therefore spends a second click on a month
  it had already stepped past — `pickEarlierThisMonth` walked to July while asserting August, on a
  loaded CI runner only. Wait for the header text to *change* after each click before reading it
  again, and choose the direction from that settled value so an overshoot walks back instead of
  spiralling. Anything that steps a MudBlazor picker has this shape.
- **A table's small-devices sort select only appears on the second render, so a phone gets an empty
  dropdown out of nowhere.** `MudTable` renders `.mud-table-smalldevices-sortselect` from the sort
  labels its header registered, and they register *during* the first render — so the select is
  absent on load and present after anything re-renders the table (on `/players`, marking a player a
  guest is enough). It arrives unlabelled, because nothing on this table sets `SortLabel`, which is
  what made it look like a stray control rather than a sort box. Two consequences: `app.css` hides
  it on `/players`, and **a first-load screenshot is not evidence that it is gone** — re-render the
  table before you look, or read `getComputedStyle` on the element rather than trusting the picture.
  `Breakpoint="Breakpoint.None"` does not stop it either: MudBlazor renders the select at every
  breakpoint and shows it with CSS, so the only way to be rid of it is to have no sort labels.
- **A row click that navigates to a page with no circuit races its own re-render, and MudBlazor
  logs.** `/players` used to navigate from `OnRowClick`, and dispatching that click did two things
  at once: it started an enhanced navigation to `/players/{id}/stats`, and it re-rendered the table
  — which is exactly when the small-devices sort select above appears, popover and all. The
  popover's `mudPopover.connect` then travels down the circuit and looks for `.mud-popover-provider`
  in a document enhanced navigation may already have swapped for the static page, which has no
  `<InteractiveShell />` and so no provider: *"No Popover Container found with class
  mud-popover-provider"*, on the console, from JS that is not ours to fix. It reproduces about one
  run in six under `--repeat-each`, which is what made it read as ordinary CI noise (issue #115).
  **A navigation to a circuit-less page belongs in an `<a href>`, not a handler** — `/players`,
  `/stats/positions` and `/stats` all link the player's name now. A handler that only navigates is
  a round trip that leaves a render behind it.
- **`MudMenu`'s `Class` lands on the root wrapper, not the activator button**: `Class="btn-gold"` painted an invisible `div` while the button kept MudBlazor's default filled colours. There is no `ActivatorClass` parameter in 9.7 — style `.<your-class>.mud-menu .mud-button-root` instead (see `.btn-gold.mud-menu` in app.css, and `SeasonPicker`'s `.season-picker .mud-button-root`).

