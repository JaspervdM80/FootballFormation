# Training

One session on one date, with the squad members who were not there and a note about it.

| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Date | DateTime | Carries a start time; midnight is how "no time entered" is stored (`HasStartTime`) |
| SeasonId | int | FK → Season, **Restrict** delete. Indexed, not unique. No navigation in either direction |
| UnavailablePlayerIds | List\<int\> | Comma-separated text, like `Game.UnavailablePlayerIds`. Always empty when the session did not take place |
| DidNotTakePlace | bool | The evening was cancelled — frost, a holiday, a hall double-booked |
| Notes | string(2000)? | Free text: what was trained, or why it did not go ahead |

Helpers on the model: `HasStartTime`, `DateLine(format)`, and `TrainingOrdering.NewestFirst` at the
bottom of the file — in memory, never in SQL, for the reason in
[patterns](../patterns/ef-core.md). There is no `OldestFirst` beside it, unlike `GameOrdering`: this
list only ever runs newest-first.

`TrainingConfiguration` names the FK with `HasOne<Season>()` rather than a navigation, the way
`GameInjury` does — nothing reads the season off a session, and a nav nobody includes is a null
waiting to be trusted.

## Why it looks like a Game and not like a squad

Absence is stored as **a list of ids on the row**, not as a child entity per player. It is the same
shape `Game.UnavailablePlayerIds` already has and for the same reason (see
`Data/Configurations/CsvListConverters.cs`): the list is short, always read whole, and never queried
by element, so a join table would cost a second query and buy nothing. It also means deleting a
session, a player or a squad membership can never take the other with it — the ids are text, not
foreign keys.

There is deliberately **no per-player note**. What was recorded is who was missing; why is one
sentence about the session, not a field per absentee.

Guests are not tracked either: a training is the season's squad, and nobody else is expected.

## A session that did not take place

`DidNotTakePlace` is a fact about the evening, not a status with a workflow: the row stays on file so
the week reads honestly, and `Notes` says why. The alternative — deleting it — loses the fact that
the club had intended to train, which is exactly what the register is for.

**A cancelled session records nobody as absent.** `TrainingService.CreateAsync` and `UpdateAsync`
both clear `UnavailablePlayerIds` when the flag is set, because a session nobody had is not one
everybody missed, and two facts that can disagree eventually do. That guard lives in the service,
not only in the dialog that hides the picker: an invariant enforced in the render tree stops holding
the moment the service is reached another way, the same reasoning as the admin guard above. The
update path is the one that matters — a session entered as held and *later* corrected is where the
stale absences would otherwise survive.

Nothing in `Core` branches on the flag yet. It is what the attendance report in
[#124](https://github.com/JaspervdM80/FootballFormation/issues/124) has to exclude from its
denominator: a player cannot miss an evening that never happened.

## Restrict, and the guard in front of it

`Season → Training` is `Restrict`, like `Season → Game`: a session records attendance, so deleting a
season must never take a year of it away silently. `SeasonService.DeleteAsync` counts trainings
beside games and refuses with a readable message, rather than letting the caller hit a raw
`DbUpdateException`.

## The one read that is not public

Everything else in this app is readable without signing in — the squad, the fixtures, the
statistics. Trainings are not: `TrainingService.GetAllAsync` goes through
`ServiceOperation.RunAdminAsync`, not `RunAsync`, and `/trainings` carries
`[Authorize(Roles = AppRoles.Admin)]` on top of that. Who missed a session, and the note usually
saying why, is a personal fact rather than a team one — and the markup gate stops holding the moment
the service is reached another way, which is the rule in
[patterns](../patterns/authorization-and-auth.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).

The menu entry is `AdminOnly` for the same reason: a visitor offered a link that only bounces them
to `/login` has been told the section exists and nothing else.

## Which weekdays the team trains

`MatchPreferences.TrainingDays` — per season, beside `MatchDay` — bounded by
`FirstTrainingDate`/`LastTrainingDate`, the season's **training period**. Together they **seed the
date** and nothing else: a session is always a row somebody created, so a week off is simply a week
nobody entered, and there is no generated calendar to keep in step with reality.

The period is a bound on what gets *proposed*, not a rule about what may be *entered*: a one-off
extra session in the summer saves without complaint. What is validated is the period itself — see
[settings](settings-and-users.md#matchpreferences-one-row-per-season).
