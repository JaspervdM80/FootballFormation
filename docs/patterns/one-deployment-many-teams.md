# One Deployment, Many Teams

[#108](https://github.com/JaspervdM80/FootballFormation/issues/108) put a fork in the road: one
deployment per team (Option A), or one deployment serving many (Option B). **This app took Option
B.** Other teams use the one at gjs-meiden.nl; nobody is expected to run a copy of their own.

## Why B, given what A would have cost

A was cheaper to build and expensive to run. Merging to `main` releases straight onto the live
volume with no staging step, so N deployments would mean N deploys, N auto-migrations against N live
volumes, and N chances for the one that matters to fail unnoticed. B keeps the operational story at
one container, one volume and one migration, and moves the cost into code, where a test can hold it.

## What B commits to

- **Every read is scoped to the team in scope**, by `AppDbContext`'s query filters and the team the
  `TeamScopedDbContextFactory` stamps on every context — not by each query remembering. A missed
  scope does not fail: it returns another team's rows looking completely normal. That is why
  `TeamDataScopingTests` exists beside `AuthorizationTests`. The rules live in the
  `ef-core-and-queries` and `domain-model` skills.
- **Every write asks about a team.** `ICurrentUser.IsAdminAsync()` means admin *of the team in
  scope*; an application admin is the one rung above every team. See
  [authorization-and-auth](authorization-and-auth.md).
- **Anything shared across circuits says which team it is about.** `LiveMatchNotifier` is one
  in-process singleton for the whole deployment, so `LiveMatchChange` carries the team as well as the
  game: the start page's banner has no game of its own to filter on, and without the team every
  team's goal reloaded every other team's start page.
- **A club's look is a preset, chosen per club.** `TeamState.Theme` resolves the club's
  `ThemeName` through `ClubTheme.Named`, and both the CSS custom properties in `App.razor` and the
  MudBlazor theme in `MainLayout` read it. Adding a theme is a code change on purpose: the values are
  emitted into the page head unescaped, so they must never come from a database row.

## How a visitor reaches their team

The team in scope is a view choice in the `ff.team` cookie, never authority — see
[clubs-and-teams](../ui_components/clubs-and-teams.md). With one team, a visitor who has chosen
nothing gets it, which is the app exactly as it looked before there was a second team.

- **A first visit is asked.** While there is more than one team, a page request with no `ff.team`
  cookie and no session is sent to `/team/choose`, which lists every team by club and hands the
  choice to `/team/set` with the page asked for as the way back. Nothing is guessed for them, and the
  bar and the drawer name no team until they choose. A calendar event's link goes through `/team/set`
  with its game's team, because a calendar app opens a browser that may never have chosen — and a
  wrong answer there opens the match as not found.

- **The team link**, `/team/{id}` (`AppRoutes.TeamLink`), is what a team hands its parents. It
  writes the cookie and opens the start page; an id that names no team answers 404, so a stale link
  says so rather than quietly opening someone else's team. `/teams` copies it from each team's row.
- **The team picker** in the drawer (`TeamPicker`) is for someone following more than one team. It
  is hidden while the deployment serves one team. It keeps the page you were on unless that page
  names a game or a player (`AppNav.AfterTeamChange`), which belongs to the team being left.

The routes carry no team on purpose. Putting one in every URL would make a shared link
self-describing, but it would rewrite every route, `AppRoutes` and the browser suite for a problem
the team link already solves.

## Deliberately not in scope

Anything that assumes a paying tenant: billing, sign-up, a domain per team. Adding a club or a team
is an application admin's job on `/teams`. If the app ever goes that way, it is a product decision
first.

## An admin can look at a team they do not run

The picker and the team link are open to everyone, team admins included. On another team an admin
is a visitor with a sign-out button. `TeamScopedAdminRole`, an `IClaimsTransformation`, drops the
`Admin` role claim on every request whose team in scope is not the account's own, by the same
`TeamAuthority` rule the services use — so every `AuthorizeView`, every `[Authorize(Roles)]` route
and the drawer's admin entries follow it without each one being told. An application admin keeps the
role everywhere.

A circuit is judged against its own team, not the request's. It keeps the team it resolved when it
started, but a rejoin after a phone suspends hands it the principal of the *reconnecting* request,
transformed against whatever `ff.team` says by then — another tab may have moved it. So the circuit's
`RevalidatingUserAuthenticationStateProvider` runs every principal it is handed back through
`TeamScopedAdminRole.For` with the circuit's team: a dropped role carries `AppClaims.AdminElsewhere`
in its place, so it can be given back. Without that, a coach's live screen lost its controls on
rejoin, and every goal was refused until a reload.

A refused route sends a signed-in session to `/` rather than the login page — the cookie handler's
`AccessDeniedPath`, and `RedirectToLogin` inside the router — because signing in again would not
make them an admin of this team. The service guard still stands on its own behind all of this. See
[authentication](../known_issues/authentication.md).

## A second deployment is still possible

Nothing assumes there is only one. The keep-alive URL is configuration: a second deployment sets
`KeepAlive__PingUrl` to its own hostname — see [deployment](../deployment.md).
