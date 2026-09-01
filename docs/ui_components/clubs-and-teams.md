# Clubs and Teams (`/teams`)

The only page in the app gated on `AppRoles.ApplicationAdmin` rather than `AppRoles.Admin` — three
gates on the same fact: `[Authorize(Roles = AppRoles.ApplicationAdmin)]` on the route,
`<InteractiveShell RequiresRole="@AppRoles.ApplicationAdmin" />` inside the island, and
`RequiresRole: AppRoles.ApplicationAdmin` on the `AppNav.Menu` entry so nobody is offered a link that
only bounces them to `/login`. `TeamService`'s writes go through
`ServiceOperation.RunApplicationAdminAsync` underneath all three, which is the one that actually
holds — see [authorization-and-auth](../patterns/authorization-and-auth.md).

Interactive (`@rendermode InteractiveServer`), because the picker and every row action are driven
from the circuit.

## One club at a time, because a team is nothing without its club

The page is scoped to a club: a `MudSelect` picks one, the card under it shows that club, and the
card below lists only its teams. Both cards carry a single icon button for adding — two full-width
`MudButton`s in the `PageHeader` was the version before this one, and on a phone they overflowed the
viewport with their labels clipped.

**The club picker scopes this page and nothing else** — it does not change which team the app is
showing. `_selectedClubId` is page state, not the URL and not a cookie: this page is admin-only and
nobody deep-links into it.

**Switching the app onto a team is the eye button on a team row**, and it is the only thing that
does. It is an `<a href>` to `/team/set` with `data-enhance-nav="false"`, exactly as the season
picker links to `/season/set` and for the same reason: the team is read off the request that creates
a scope, so an island already up would keep the old one while the chrome showed the new. The row the
app is currently showing wears the `Selected` badge and offers no button.

## The team the app is showing is a cookie, and the first team when there is none

`ICurrentTeam` resolves it: the id in `ff.team` while it still names a team, and otherwise the lowest
team id. `TeamService.GetCurrentAsync()`, `TeamState` and the write guard all read that one answer,
memoized per scope.

`ff.team` is written three ways — at sign-in from the account's own team, by `/team/set` when
someone picks one, and by a middleware in `Program.cs` that stamps the resolved team onto every HTML
page response, so "the last team you looked at" is remembered without anyone having to choose. The
sign-in one is what stops an admin of any team but the first landing somewhere they cannot change
anything: this page, where a team is picked, is a rung of authority above them. A year, not the season cookie's eight
hours: which team you follow is not a match-day choice. Nothing validates the value on the way in,
because it is a view choice and not authority — what an account may *change* comes from its own
`team_id` claim, never from this cookie. See
[authorization-and-auth](../patterns/authorization-and-auth.md).

`DeleteTeamAsync` still refuses the lowest-id team, because that is what every visitor who has chosen
nothing falls back to — and it refuses a team accounts are still on, which the `Restrict` FK would
otherwise report as a raw `DbUpdateException`.

The selection is resolved, not stored: `_selectedClubId` is a component field, so it holds across the
page's own `Reload()` after a write and is gone on a browser reload, which starts again from the club
of the team the app is showing and then the first club. That is deliberate — a cookie or a route
parameter is more machinery than a view choice on an admin-only page is worth.

Every write follows its own result: adding a club selects it, so "add a club, then add its first
team" is one movement, and a team saved into another club from the dialog moves the page to that club
rather than disappearing off it. Deleting the selected club drops the selection onto whatever is left.

The rows are `.list-row` in `app.css`, shared with the season list on `/settings`, rather than a
`MudTable`: under a single club there is no second column to fill, and a flex row needs no
stacked-table breakpoint to survive a phone. `Teams.razor.css` holds only what this page alone uses —
the picker row, the crest and the empty states.

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
