# Authorization and Authentication

## Authorization is at the service boundary, not only in the markup
Every mutating service method goes through `ServiceOperation.RunAdminAsync`, which asks
`ICurrentUser` and refuses before running. The UI already hides those controls behind
`<AuthorizeView Roles="@AppRoles.Admin">` and an unrendered handler has no id to dispatch to — but
that is enforcement in the render tree only, and it stops holding the moment a service is reached
some other way. Reads stay open: the squad, fixtures and statistics are public.

**Minute figures are the exception inside those public reads.** How long someone has played is the
raw material of a rotation argument, so it belongs to whoever has to make one: a visitor gets the
*split* — which positions a player spent their time in, as a share of that time — and an admin gets
the minutes, the totals and the share of *available* time behind them. On `/players/{id}/stats` that
hides the Minutes tile whole (total, per-game average and utilisation alike), the minutes half of
each position label, the per-game `Min` column and the footnote explaining its `~`; on `/stats` it
hides the Playing-time card outright, because that card is minutes from top to bottom and a redacted
version of it would say nothing. Note what this costs: **utilisation — minutes played over minutes
available — is admin-only in full**, so a visitor cannot see how much of their own time a player
got, only how they divided it. That is the intended reading of the rule, not an oversight; a public
utilisation figure is the rotation argument with the units filed off. Games, goals, assists and the
scoreline are counts, not minutes, and are unaffected.

**Goalkeeper minutes on `/stats` stay public, deliberately** — who has kept goal and for how long is
what the squad actually asks about, and it is one figure per keeper rather than a table ranking
everybody. It is the only card that shows minutes to a signed-out visitor, and
`authorization.spec.js` asserts it is still there, so removing it has to be a decision someone takes
rather than a line that rots away.

Nothing in `Core` knows about any of this — the reports return the same numbers either way and the
render is what chooses — so the rule is pinned in `tests/ui/specs/` instead. `match-day.spec.js`
completes a match and then reads the player page twice, once as an admin and once in a visitor
context, because proving an absence is only worth anything next to the presence it is measured
against.

**Squad injury status is admin-only on `/players`, for the same reason.** Whether a named child is
injured is a personal fact, not squad news, so the row's medical marker and the "{0} injured" count
in the header both sit behind `<AuthorizeView Roles="@AppRoles.Admin">` — a visitor sees the player
and the squad total, not who is hurt. `injury.spec.js` pins the pair: an admin sees the mark, a
visitor on the same page does not. (The mid-match "off injured" event on `/games/{id}/live` is a
separate, deliberately public part of the match narrative and is not covered by this.)

**One read is not open, and it is the exception worth knowing.**
`GameService.GetCommentsAsync(gameId, includePrivate)` takes a flag saying whether to include
admin-only comments — and then confirms it against `ICurrentUser` rather than believing it, so a
caller passing `true` without the role gets the public ones. A read with something to hide should
not be the one place a boolean argument is trusted. `AuthorizationTests` pins both halves —
`An_anonymous_caller_asking_for_private_comments_gets_only_the_public_ones` alongside
`Reads_stay_open_to_everyone`.

**The third is the account list.** `UserService.GetAllAsync` is `RunAdminAsync` rather than a public
read: it is display names and **logins**, and the team it is scoped to comes from the `ff.team`
cookie, which anyone can point anywhere. A read scoped by something the visitor sets has to ask the
same question the writes do, or an admin of one team reads every other team's accounts by editing a
cookie. `The_account_list_is_not_one_of_the_public_reads` and
`Pointing_the_team_cookie_at_another_team_does_not_list_its_accounts` pin both halves.

**The second such read is the training register**, and it goes further: `TrainingService`'s
`GetAllAsync` is `RunAdminAsync` outright rather than a filtered read, because
there is no public half of a session to hand back. Who missed a training, and the note usually
saying why, is a personal fact rather than a team one — unlike the squad, the fixtures and the
statistics, which exist to be shared with parents. `/trainings` carries
`[Authorize(Roles = AppRoles.Admin)]` and the menu entry carries `RequiresRole` on top of that, so nobody is
offered a link that only bounces them to `/login`. `Trainings_are_the_one_read_that_is_not_public`
pins the service half; `authorization.spec.js` pins the route and the missing menu entry.

Everything else public stays genuinely public; if a fourth such read appears, it belongs here too.

`CircuitCurrentUser` answers false for an account still on its seeded password, so the first-login
gate is a real restriction rather than a redirect that could be navigated around.

## Admin authority names a team, and the guard asks about the team in scope

`Admin` is not "may change data" any more — it is "may change *this* team's data". The question is
still asked in exactly one place, so no call site had to change: `ICurrentUser.IsAdminAsync()` now
means *admin of the team the request is about*, and every `RunAdminAsync` reads the same way it did.

Three pieces carry it:

- **`AppUser.TeamId`**, minted into the cookie as the `team_id` claim by `Routing.PrincipalFor`, and
  absent on an application admin — so a missing claim reads as "every team" in one place only.
  **Signing in also writes `ff.team` from it** (`Routing.SelectOwnTeam`, on both sign-in routes), or
  an admin of any team but the lowest-numbered one would land on a team they cannot change — every
  button rendered, every write refused — with no way back, since `/teams` is a rung above them.
- **`ICurrentTeam`**, the team the request is about. `CurrentTeam` (Core/Security) resolves the
  `ff.team` cookie while it still names a team, and otherwise the first team in the database.
  Registered by hand in `Program.cs` because the cookie is the host's to read; it takes the id, so
  `Core` gains no HTTP dependency. It depends on nothing but the context factory, which is what keeps
  it out of the `UserService → ICurrentUser → AuthenticationStateProvider` loop described below.
- **`TeamAuthority.GrantsAdminOf`**, the whole rule as one function: an application admin, or the
  account's team and the team in question being the same, non-null team. `TeamAuthorityTests` pins
  it structurally, including the two nulls that must not read as a match.

**Where the write's subject is not the team in scope, the call site says which team it is.**
`ICurrentUser.IsAdminOfAsync(teamId)` is that overload, and `UserService` is its only caller: an
account being edited may belong to another team, so `MayManageAsync` passes every team the change
touches — the one it is on and the one it is moving to — through the same question. Without it,
`/users` would be the way from running one team to resetting the password of an admin of every other.
`AuthorizationTests` pins both directions.

**The data below a season now carries a team, so the guard finally bites on it.** `Season` holds a
`TeamId`, and `Game`, `Training`, `MatchPreferences` and `SeasonSquadMember` each carry a copy of it
denormalised from the season they hang off, so a filter can read one column without a join.
`Player` is the exception: it belongs to the *club* (a `ClubId`), because a season's squad draws from
the club pool and a girl who moves between the club's teams must keep one history.

**The read side is scoped by default, not per query.** `AppDbContext` applies a global
`HasQueryFilter` to each of those entities reading its own `CurrentTeamId`/`CurrentClubId`, and
`TeamScopedDbContextFactory` — the `IDbContextFactory<AppDbContext>` every service receives — stamps
those from `ICurrentTeam` before handing a context over. So a query that forgets to mention the team
still returns only the team in scope, and one that somehow escapes the factory (a null stamp) returns
*nothing* rather than another team's rows. The raw factory that makes an unstamped context is taken
by exactly two things: `CurrentTeam`, which must resolve the team without asking a context which team
it is, and `SeasonService`'s boot loops, which stamp each team by hand to walk them all.

**The one trap the filter does not cover is `FindAsync`, which bypasses global query filters.** A
`db.Games.FindAsync(id)` returns another team's game; the scoped services use
`FirstOrDefaultAsync(x => x.Id == id)` on the filtered set instead, and a write reaching a game's
child by the child's own id (a goal, a comment) gates on `AppDbContext.GameInScopeAsync` first.
`TeamDataScopingTests` is the read-side counterpart to `AuthorizationTests`: it seeds two teams and
asserts every public read returns only the team in scope, so a forgotten filter fails there.

## There are two rungs of authority, and the upper one is a second guard
`ServiceOperation.RunApplicationAdminAsync` is `RunAdminAsync` asking a different question:
`ICurrentUser.IsApplicationAdminAsync()` rather than `IsAdminAsync()`. Only `TeamService`'s writes
and the role grant in `UserService` use it. The two are not independent — an `ApplicationAdmin` is
minted an `Admin` role claim too (`Routing.PrincipalFor`), so every existing guard keeps holding
unchanged and the new one is strictly narrower.

The role itself is the part worth being careful about, and **it is two questions, not one**. An
ordinary admin passes `RunAdminAsync`, so `UserService` routes every role entering *or leaving* an
account through `MayChangeAsync`, which asks `IsApplicationAdminAsync()`:

- **granting** — `CreateAsync`/`UpdateAsync` writing `UserRole.ApplicationAdmin`, or the role picker
  on `/users` would be a self-promotion;
- **revoking** — `UpdateAsync` replacing it and `DeleteAsync` removing the account. Guarding only the
  grant leaves an ordinary admin able to demote or delete an application admin whenever a second one
  exists, since the last-of-their-kind rule does not fire — and they could not hand the role back.

`UserServiceTests` pins each of those separately; `AuthorizationTests` pins the service-boundary
refusals on `TeamService`.

## The sign-in cookie has three settings that are easy to get wrong
All three live in `Program.cs`, and each failed in a way that reads as "it logged me out again"
rather than as a bug with a cause. The rules, with the evidence and the symptoms in
[known_issues](../known_issues/authentication.md):

- **`IsPersistent`, on the sign-in**, not `ExpireTimeSpan`, is what makes the cookie outlive the
  browser session. Both sign-in routes pass `PersistentSession()`, which returns a *fresh*
  `AuthenticationProperties` each call.
- **`SameSite` is `Lax`, and must not go back to `Strict`.**
- **Data protection pins an application name**, or the purpose string follows the content root path.

`tests/ui/specs/session.spec.js` holds all three — they are browser decisions, so no C# test can see
them.

## Revoking authority takes two halves, because a circuit barely makes requests
`OnValidatePrincipal` re-checks the security stamp on every HTTP request — and a Blazor Server tab
makes almost none after the page loads, so it is not what revokes a session here (see
[known_issues](../known_issues/authentication.md) for what that measured).
`RevalidatingUserAuthenticationStateProvider` (Web/Security) is the other half: it re-asks
`UserService.FindForSessionAsync` on a timer for the life of the circuit and signs it out when the
account is gone or its stamp has moved. Five minutes by default;
`Auth:RevalidationIntervalSeconds` sets it, and `0` leaves the stock provider in place so the UI test
can be run against the old behaviour.

Both halves call the same `FindForSessionAsync(ClaimsPrincipal)` overload on purpose — two places
deciding separately what a valid session looks like is how they drift.

The provider takes an `IServiceScopeFactory` rather than a `UserService`, and **not** for the usual
short-lived-context reason. It *is* the circuit's `AuthenticationStateProvider`; `UserService`
depends on `ICurrentUser`, which depends on the `AuthenticationStateProvider`. Injecting it directly
closes the loop and the container refuses to build.

A failed check makes the circuit anonymous. It cannot clear the cookie — a circuit has no HTTP
response to set a header on — so `[Authorize]` renders `NotAuthorized`, `RedirectToLogin`
force-loads, and *that* request is where `OnValidatePrincipal` finally drops the cookie.

