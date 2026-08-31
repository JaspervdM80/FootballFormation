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

**The second such read is the training register**, and it goes further: `TrainingService`'s
`GetAllAsync` is `RunAdminAsync` outright rather than a filtered read, because
there is no public half of a session to hand back. Who missed a training, and the note usually
saying why, is a personal fact rather than a team one — unlike the squad, the fixtures and the
statistics, which exist to be shared with parents. `/trainings` carries
`[Authorize(Roles = AppRoles.Admin)]` and the menu entry carries `RequiresRole` on top of that, so nobody is
offered a link that only bounces them to `/login`. `Trainings_are_the_one_read_that_is_not_public`
pins the service half; `authorization.spec.js` pins the route and the missing menu entry.

Everything else public stays genuinely public; if a third such read appears, it belongs here too.

`CircuitCurrentUser` answers false for an account still on its seeded password, so the first-login
gate is a real restriction rather than a redirect that could be navigated around.

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

