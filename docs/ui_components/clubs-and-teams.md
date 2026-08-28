# Clubs and Teams (`/teams`)

The only page in the app gated on `AppRoles.ApplicationAdmin` rather than `AppRoles.Admin` — three
gates on the same fact: `[Authorize(Roles = AppRoles.ApplicationAdmin)]` on the route,
`<InteractiveShell RequiresRole="@AppRoles.ApplicationAdmin" />` inside the island, and
`RequiresRole: AppRoles.ApplicationAdmin` on the `AppNav.Menu` entry so nobody is offered a link that
only bounces them to `/login`. `TeamService`'s writes go through
`ServiceOperation.RunApplicationAdminAsync` underneath all three, which is the one that actually
holds — see [authorization-and-auth](../patterns/authorization-and-auth.md).

Interactive (`@rendermode InteractiveServer`), because both tables are driven by dialogs.

## Two tables, because a team is nothing without its club

Teams first — it is what the page is for — then the clubs beneath, since adding a club is something
you do *in order to* add a team. **Add Team** is disabled while there are no clubs, rather than
opening a dialog with an empty picker.

The team currently selected carries a `Selected` badge and nothing else: there is no picker, and
`TeamService.GetCurrentAsync()` always answers with the first team. The badge says which one the app
would show, not which one you have chosen.

Both tables reuse the `.users-table` layout in `app.css` (the selectors name both), including the
grid it collapses into below 600px: name and badge on the first line, the secondary column on the
second, the actions hugging the right edge across both.

## The theme is named, not edited

A club's theme is a `ClubTheme` preset chosen by name; the colours behind it are compiled in. The
dialog says so in its helper text, because a select box with one option otherwise reads as a page
that failed to load its data. `ClubTheme.All` is the list, `ClubTheme.Named` resolves one and falls
back to the default rather than throwing — a club naming a theme a later build dropped renders in
GJS colours rather than failing.

The logo is a path under `wwwroot`, so swapping a crest is a file drop — and `TeamService` enforces
that rather than trusting the helper text, refusing an absolute URL, a protocol-relative `//host/…`
and a `javascript:` scheme. The crest renders into an `img` on every page for every anonymous
visitor and the app sets no Content-Security-Policy, so an off-site path would have the whole
audience fetching a third party. Left empty it falls back to the theme's own crest, which is why a
blank is stored as null: a blank string would render a broken image instead.

## A rename has to redraw the chrome

`MainLayout` renders statically in the page-load request, so a rename made in this island never
reaches the app bar on its own. After a write the page refreshes its own `TeamState` and compares the
name and crest against what it was rendered beside; only when they actually moved does it
`NavigateTo(..., forceLoad: true)`. Reloading after every edit would be the easy version and a worse
one — most edits here do not touch the app's own identity.

Context for why this page exists at all, and what it deliberately does *not* do yet, is in
[#108](https://github.com/JaspervdM80/FootballFormation/issues/108).
