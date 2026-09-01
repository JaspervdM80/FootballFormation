# Settings and Users

## MatchPreferences (one row per season)
| Property | Type | Default |
|---|---|---|
| Id | int | PK |
| SeasonId | int | FK -> Season, **Cascade** delete. Unique index |
| GameDurationMinutes | int | 60 |
| DefaultSplitType | GameSplitType | Halves |
| DefaultFormation | FormationType | F442 |
| MatchDay | DayOfWeek | Saturday |
| TrainingDays | List\<DayOfWeek\> | empty — stored as comma-separated ints, like every other list column |
| FirstTrainingDate | DateTime? | null — meaning the season's own start |
| LastTrainingDate | DateTime? | null — meaning the season's own end |

The defaults a new game starts from are **per season**, not per app: a team moving up an age group
plays longer games and often a different shape, and the fixture day can move too. Keeping one row
per season means setting this year's values never rewrites the ones last year's games were created
under.

The row is created on first read by `MatchPreferencesService.GetAsync(seasonId)`, seeded via
`MatchPreferences.CopyFor` from the newest season **before** it that has one — so a new season
inherits last year's settings rather than the hardcoded 4-4-2 / 60 minutes, and per-season storage
costs the user no extra work. There is no "current season" overload — every caller has a season in
hand, from the picker or from the game being edited.

`GetNextMatchDateAsync(seasonId)` uses that season's `MatchDay`, counts only that season's games,
and keeps its answer inside the season window: it measures from the opening day for a season not
started yet, and falls back to the last match day of the window for one already over. Without that
clamp, adding the first fixture of next season proposed a date in the season we are living in.

`GetNextTrainingDateAsync(seasonId)` answers with the soonest of `TrainingDays` inside the period
that has **no session on it yet**, and once every one is taken — the ordinary state after the period
has generated them — with the soonest that *has* one, which is the next evening the team trains and
what the caption on `/settings` names. Only a period already behind us falls back to its last
training day, the same clamp the match date uses. It does not step off the latest entry the way the
match date does: with the period generated in full, "the day after the last one entered" is the
closing evening of the season. An
empty `TrainingDays` — the state every season starts in — keeps the old reference-date walk and still
answers with a date rather than refusing, because the dialog needs one and there is no weekday to
land on yet.

## The training period

`FirstTrainingDate` and `LastTrainingDate` are the window `GetNextTrainingDateAsync` walks, in place
of the season's own. **Either end being null means that end of the season**, which is exactly how
every row behaved before the period existed — so nothing changed for a database that has not set one,
and the migration needed no backfill.

The weekdays alone know nothing about the summer: a team that trains from mid-August was being
offered a Tuesday in early July, because July is when the season opens. The period is what stops
that. It is a bound on the date *proposed*, not a rule about what may be *entered* — a one-off
session outside it saves without complaint, because an extra evening in the summer is legitimate and
a guard second-guessing the date the admin typed would be in the way.

**`SaveAsync` writes the sessions the period implies**, in the same `SaveChanges` as the preferences
themselves: one for every training day between the two dates that has no session already, and away
with the generated ones outside them that carry nothing. Both ends have to be set or there is no
schedule at all; the rules, and what is deliberately never removed, are in
[training](training.md#the-schedule-writes-the-sessions). It hands back a `TrainingSync(Created,
Removed)` so `/settings` can report what it did.

The diff runs **only when the period or the training days actually changed** — a save that moved the
game length leaves the sessions alone, so an unrelated preference cannot resurrect an evening the
admin deleted.

`SaveAsync` validates the period itself, since it is the one write path and neither failure is a
preference: an end before the start, and either end falling outside the season's own window. The
second matters because `Training.SeasonId` is resolved from the session's date — a period reaching
into next season would be describing a season it is not attached to. Both checks run **in memory**
on the materialised season row (`Season.Contains`), never in SQL; `DateInSqlInterceptor` discovers
both columns from the EF model and `DateInSqlGuardTests` pins the list.

**`CopyFor` deliberately leaves the period behind**, unlike `TrainingDays`. A date belongs to one
season, last August's opening night is not a guess at this one, and a carried-forward date would
fail the window check the moment anyone pressed Save. Same call `SeasonSquadService.CopyFromAsync`
makes about `IsInjured`.

`TrainingDays` *is* copied, as a new list rather than shared: editing next season's days must not
reach back into last season's row.

## AppUser (table `Users`)
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| DisplayName | string(100) | The person, shown in the app bar and the user list |
| Username | string(50) | The login. **Unique index** |
| PasswordHash | string | PBKDF2, via `PasswordHasher<AppUser>` — never a plaintext column |
| Role | UserRole | Stored as int. Written into the auth cookie as `Role.ToString()` |
| TeamId | int? | The team an `Admin` runs, as the `team_id` claim. Null on an `ApplicationAdmin`, who runs every team. **FK is `Restrict`** — `TeamService` refuses to delete a team accounts are still on |
| SecurityStamp | string(64) | Guid "N". Changes whenever the account's authority does |
| MustChangePassword | bool | Set on the account a fresh install seeds, whose password is public knowledge. While true the session can sign in and nothing else — every route sends it to `/settings`, and `ICurrentUser.IsAdminAsync()` answers false, so the services refuse it too. Cleared by `ChangePasswordAsync` |

Nothing an account owns can make it undeletable. The one reference to it — `GameComment.AuthorId` —
is `SetNull`, so deleting a user leaves their comments in place, unattributed.

`UserService.GetAllAsync` reads a `UserSummary` projection (id, name, username, role, team), never
the entity — the `/users` page and its dialog have no use for `PasswordHash` or `SecurityStamp`, and a
read that returned them would put credential material one careless log line from disclosure.
`/dev/login` is the exception that still needs the whole entity, for the stamp its cookie carries,
so it has its own `FindDevLoginAdminAsync` rather than widening the list read.

**There are two rungs, and the upper one implies the lower.** `Admin` runs **one** team — the team in
`TeamId`, and its squad, fixtures and live match. `ApplicationAdmin` decides which clubs and teams the
app serves at all, runs all of them, and is the only role that can grant itself to anyone else. `Role` stays a single column: `PrincipalFor`
mints a second `Admin` role claim for an application admin, so every existing
`[Authorize(Roles = AppRoles.Admin)]` keeps holding without knowing the new member exists. In C#,
ask `role.GrantsAdmin()` rather than comparing with `UserRole.Admin`, which would read an application
admin as not being an admin at all.

**The role is the grant.** `[Authorize(Roles = AppRoles.Admin)]` and
`<AuthorizeView Roles="@AppRoles.Admin">` match `Role.ToString()`, which `AppRoles` ties back to the
enum member name — so renaming a `UserRole` member breaks the build rather than quietly
unauthorizing everyone. Anonymous (not signed in) is not a role and needs no member.

**SecurityStamp is what makes a change take effect now.** The cookie lasts fourteen days and is
sliding, so without it, deleting an account or changing its role would leave the old session working
until it lapsed. The stamp is copied into the cookie at sign-in and re-checked on every authenticated
request by `OnValidatePrincipal` (Program.cs) via `UserService.FindForSessionAsync`; a mismatch
rejects the principal and signs the browser out. `UserService` regenerates it on password change, role
change and **team change** — the team is part of what the cookie asserts now — but deliberately **not**
on a rename, which changes nothing about what the account may
do.

A live Blazor circuit is still not re-validated per SignalR message — that would be a database read
per keystroke. It is re-validated **on a timer** instead, by
`RevalidatingUserAuthenticationStateProvider` (Web/Security), which asks `FindForSessionAsync` the
same question `OnValidatePrincipal` asks and signs the circuit out when the answer is no. Five
minutes by default, `Auth:RevalidationIntervalSeconds` to change it. Without it a tab open since
before the change kept its authority until someone reloaded — and because `CircuitCurrentUser` reads
that same provider, so did the write guard on every service.

`UserService.DeleteAsync` and `UpdateAsync` both refuse to remove or demote the **last** Admin —
the one operation with no way back short of editing the database by hand. The same pair of rules
applies again to the last **ApplicationAdmin**, for the same reason one rung up: nobody else can hand
the role back.

**Every role entering *or leaving* an account goes through `MayChangeAsync`**, which asks
`ICurrentUser.IsApplicationAdminAsync()` rather than relying on `RunAdminAsync`. Both directions
matter and it is easy to guard only one: without the *revoke* half, an ordinary admin could demote or
delete an application admin — a role they could not hand back — simply because a second application
admin existed and the last-of-their-kind rule therefore did not fire.
`An_admin_cannot_demote_an_application_admin` and `An_admin_cannot_delete_an_application_admin` pin
the two halves separately, because fixing one does not fix the other. `EnsureAdminSeededAsync` runs on every startup, does nothing once any account exists so a
changed password survives, and seeds its account as an `ApplicationAdmin` — otherwise a fresh install
would have nobody who could reach `/teams`.

## Club and Team (tables `Clubs`, `Teams`)
The first thing in this app that sits **above** a season. Nothing hangs off them yet: seasons, games
and players still belong to the deployment, and `TeamService.GetCurrentAsync()` always answers with
the first team there is. It exists as the seam a chosen team will come from — see
[#108](https://github.com/JaspervdM80/FootballFormation/issues/108) for the fork that decides whether
that ever becomes a real choice.

| Property | Type | Notes |
|---|---|---|
| Club.Name | string(100) | **Unique index** |
| Club.LogoUrl | string(255)? | A path under `wwwroot`. Null falls back to the theme's own crest |
| Club.ThemeName | string(50) | Names a `ClubTheme` preset, not a set of colours — the styles are compiled in and not editable. `ClubTheme.Named` falls back to the default rather than throwing |
| Team.ClubId | int | FK, **Restrict** |
| Team.Name | string(50) | **Unique with ClubId** — two clubs may each have an MO15-2 |

`TeamService` refuses to delete a club that still has teams, and refuses to delete **the team
`GetCurrentAsync` answers with** — which, there being no picker, is the lowest-numbered one. That is
stricter than a last-team rule and subsumes it: the only team is always the current one. Without it,
deleting the current team beside another would silently move the app's title, crest and manifest onto
a different team while every season, game and player stayed exactly where it was.

`Club.LogoUrl` is validated as a **relative path**, refusing an absolute URL, a protocol-relative
`//host/…` and a `javascript:` scheme alike — it renders into an `img` on every page for every
anonymous visitor, and there is no Content-Security-Policy to catch it. Same two prefixes
`Routing.IsLocalUrl` refuses, for the same reason. Every write on it goes through `ServiceOperation.RunApplicationAdminAsync`; the reads
are public like every other read.

`AddClubsAndTeams` creates both tables and promotes the deployment's lowest-numbered admin to
`ApplicationAdmin`, rolling its security stamp so the next request mints a cookie carrying the new
role. `TeamService.EnsureSeededAsync` then fills in GJS / MO15-2 on the next boot, and does nothing
once any club exists.

