# Training

One session on one date, with the squad members who were not there and a note about it.

| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Date | DateTime | Carries a start time; midnight is how "no time entered" is stored (`HasStartTime`) |
| SeasonId | int | FK → Season, **Restrict** delete. Indexed, not unique. No navigation in either direction |
| UnavailablePlayerIds | List\<int\> | Comma-separated text, like `Game.UnavailablePlayerIds` |
| Notes | string(2000)? | Free text: what was trained, and anything worth remembering |

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

`MatchPreferences.TrainingDays` — per season, beside `MatchDay`. It **seeds the date** and nothing
else: a session is always a row somebody created, so a week off is simply a week nobody entered, and
there is no generated-then-cancelled state to keep in step. See
[settings](settings-and-users.md#matchpreferences-one-row-per-season).
