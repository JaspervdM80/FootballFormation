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

## AppUser (table `Users`)
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| DisplayName | string(100) | The person, shown in the app bar and the user list |
| Username | string(50) | The login. **Unique index** |
| PasswordHash | string | PBKDF2, via `PasswordHasher<AppUser>` — never a plaintext column |
| Role | UserRole | Stored as int. Written into the auth cookie as `Role.ToString()` |
| SecurityStamp | string(64) | Guid "N". Changes whenever the account's authority does |
| MustChangePassword | bool | Set on the account a fresh install seeds, whose password is public knowledge. While true the session can sign in and nothing else — every route sends it to `/settings`, and `ICurrentUser.IsAdminAsync()` answers false, so the services refuse it too. Cleared by `ChangePasswordAsync` |

Nothing an account owns can make it undeletable. The one reference to it — `GameComment.AuthorId` —
is `SetNull`, so deleting a user leaves their comments in place, unattributed.

**The role is the grant.** `[Authorize(Roles = AppRoles.Admin)]` and
`<AuthorizeView Roles="@AppRoles.Admin">` match `Role.ToString()`, which `AppRoles` ties back to the
enum member name — so renaming a `UserRole` member breaks the build rather than quietly
unauthorizing everyone. Anonymous (not signed in) is not a role and needs no member.

**SecurityStamp is what makes a change take effect now.** The cookie lasts fourteen days and is
sliding, so without it, deleting an account or changing its role would leave the old session working
until it lapsed. The stamp is copied into the cookie at sign-in and re-checked on every authenticated
request by `OnValidatePrincipal` (Program.cs) via `UserService.FindForSessionAsync`; a mismatch
rejects the principal and signs the browser out. `UserService` regenerates it on password change and
role change — but deliberately **not** on a rename, which changes nothing about what the account may
do.

A live Blazor circuit is still not re-validated per SignalR message — that would be a database read
per keystroke. It is re-validated **on a timer** instead, by
`RevalidatingUserAuthenticationStateProvider` (Web/Security), which asks `FindForSessionAsync` the
same question `OnValidatePrincipal` asks and signs the circuit out when the answer is no. Five
minutes by default, `Auth:RevalidationIntervalSeconds` to change it. Without it a tab open since
before the change kept its authority until someone reloaded — and because `CircuitCurrentUser` reads
that same provider, so did the write guard on every service.

`UserService.DeleteAsync` and `UpdateAsync` both refuse to remove or demote the **last** Admin —
the one operation with no way back short of editing the database by hand. `EnsureAdminSeededAsync`
runs on every startup and does nothing once any account exists, so a changed password survives.

