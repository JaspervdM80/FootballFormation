# Training

One session on one date, with the squad members who were not there and a note about it.

| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Date | DateTime | Date only — a session has no start time, and the migration wiped the ones rows were carrying |
| SeasonId | int | FK → Season, **Restrict** delete. Indexed, not unique. No navigation in either direction |
| UnavailablePlayerIds | List\<int\> | Comma-separated text, like `Game.UnavailablePlayerIds`. Always empty when the session did not take place |
| InjuredPlayerIds | List\<int\> | The same, for the ones who missed it through injury. Kept disjoint from the list above, and emptied with it on a cancelled session |
| AbsencesRecorded | bool | The standing injuries have been copied onto this session. Set once, then never rewritten |
| DidNotTakePlace | bool | The evening was cancelled — frost, a holiday, a hall double-booked |
| FromSchedule | bool | Generated from the season's training period rather than entered by hand |
| Notes | string(2000)? | Free text: what was trained, or why it did not go ahead |

Helpers on the model: `IsUnusedSchedule` — generated, not cancelled, nobody marked absent, no note —
and `TrainingOrdering.UpcomingFirst(today)` / `MondayOf` at the bottom of the file, in memory and
never in SQL, for the reason in [patterns](../patterns/ef-core.md). `UpcomingFirst` puts this week
and the weeks ahead first, ascending, and the weeks already over below them, most recent first —
whole ISO weeks on both counts, so a Tuesday session does not drop to the foot of the page on the
Wednesday.

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
sentence about the session, not a field per absentee. **Injury is the one exception**, and it is a
second list rather than a note: it is the one reason the statistics have to be able to count.

## Two absence lists, and why they never overlap

`UnavailablePlayerIds` and `InjuredPlayerIds` both mean "not at this session"; only the second says
why. `Training.AbsentCount` **adds** them, so anyone in both would be counted twice — `TrainingService`
keeps them disjoint on every write, injury winning. `Training.WasAbsent` is the member to ask when only
"was she there" matters — `TrainingAttendanceReport` counts attendance through it and reads
`InjuredPlayerIds` directly only for the reason — and `AbsentCount` is what the badge on `/trainings`
counts, so the badge and the report's denominator cannot drift apart the first time only the injured
list has anyone in it.

## How an injury reaches a session

`SeasonSquadMember.IsInjured` is a standing flag. A game copies it into `Game.InjuredPlayerIds` as it
settles, and a session does the same through `StandingTrainingInjuries` — but the two settle at
different moments, and that difference is the whole design:

- **A match settles when it is played**, so the undated flag is the right answer at that moment.
- **A session has no such moment.** Nothing in the app revisits an evening nobody opened, so a session
  the schedule generated and nobody has written up is stamped the first time
  `TrainingService.GetAllAsync` runs after the evening has passed. That read writes, which is why it
  says so on the method.
- **`SeasonSquadMember.InjuredSince`** is what makes that safe. Without a date, a player flagged injured
  in November would be stamped absent from every session since August the next time the page was opened.
  The settle only stamps a session dated on or after `InjuredSince`. The column is set from the injected
  `TimeProvider` when the flag goes on, left alone while it stays on — re-saving must not move it — and
  cleared when she recovers. The migration backfills deploy day for everyone already flagged, so an
  injury nobody dated cannot reach backwards either.
- **Stamped once.** `AbsencesRecorded` marks it done, because an empty injured list is otherwise
  indistinguishable from an unwritten one. Recovering afterwards does not empty the register: by then
  it is history rather than a status, the same reasoning as `Game.AbsencesRecorded`.
- **The coach outranks the flag.** The dialog offers an *Injured players* picker, prefilled once from the
  squad's standing injuries while the session is still unstamped and free to edit — a girl carrying an
  injury can still turn up and train lightly. **Saving a session stamps it**, whatever its date, the same
  rule `FromSchedule` follows: a session the coach has been into is the coach's. Without that, a register
  written up on the evening itself was still unstamped at midnight and the next morning's first read
  replaced it — and an emptied injured list could never be saved at all, because the prefill put
  everybody straight back.

Guests are not tracked either: a training is the season's squad, and nobody else is expected.

## A session that did not take place

`DidNotTakePlace` is a fact about the evening, not a status with a workflow: the row stays on file so
the week reads honestly, and `Notes` says why. The alternative — deleting it — loses the fact that
the club had intended to train, which is exactly what the register is for.

**A cancelled session records nobody as absent.** `TrainingService.CreateAsync` and `UpdateAsync`
both clear `UnavailablePlayerIds` and `InjuredPlayerIds` when the flag is set, because a session nobody had is not one
everybody missed, and two facts that can disagree eventually do. That guard lives in the service,
not only in the dialog that hides the picker: an invariant enforced in the render tree stops holding
the moment the service is reached another way, the same reasoning as the admin guard above. The
update path is the one that matters — a session entered as held and *later* corrected is where the
stale absences would otherwise survive.

`TrainingAttendanceReport` is what reads the flag back out, dropping a cancelled evening from the
denominator: a player cannot miss one that never happened. Nothing else distinguishes it from a
session everybody attended, since both carry an empty absence list.

## Reading the register back

`Core/Reporting/TrainingAttendanceReport.cs` — a pure builder like the rest of `Core/Reporting/`,
taking the sessions, a `SeasonSquads` and the date today. **Attendance is the squad minus the
absentees**, because a session records who was *not* there; who was *expected* has to come from the
squad of the season the session belongs to.

That per-season lookup is the denominator rule: a player is measured against the sessions of the
seasons she was a full member of, never against every session in view. On "All seasons" someone who
joined this year is not charged for the year before she was here — the same problem
`PlayerStats.Utilization` solves for minutes, solved the same way.

- **Only sessions that have already been held count**, which is why the builder needs the date at
  all — `StatsService` takes it from the injected `TimeProvider` and passes it in. A season's
  evenings are all written the day its training period is saved, so in September most of them are
  still ahead, and an evening nobody has been to yet carries the same empty absence list as one the
  whole squad turned up to. Counting them put every figure within a few points of 100% and moved it
  by whole percent as the calendar passed each session. A session dated *today* waits until tomorrow:
  a row carries a date and no start time, so nothing can tell this evening's training from one that
  is over. Sessions ahead are left out of `Cancelled` for the same reason, or a fresh season would
  read *"2 held, 40 cancelled"*. The test lives on the entity as `Training.HasBeenHeld(today)` — been and
  gone, and it went ahead — because `/trainings` needs the same answer for its green *"0 afwezig"* badge,
  and a second copy of the rule there would let the badge and the percentage above it drift apart.
- **Guests are left out entirely.** The dialog offers only full members, so a guest carries no
  absence and would otherwise read as a perfect attender.
- **The squad figure weighs player-sessions, not players** — `Attended` summed over `Held` summed,
  not the mean of the individual percentages, so a girl who was there for the last month does not
  count for as much as one who was there all year.
- **A session missed through injury is missed.** It counts in `Missed` and is reported again on its own
  as `Injured`, on the player's tile and beside her name in the attendance list, while the denominator
  stays every session held — so the percentage still matches the *"n sessions held"* printed above it.
  Removing those sessions from the denominator instead was considered and rejected: it makes a player
  who simply did not turn up rank below one who was injured, and a row then reads *"6 / 7"* on a season
  with twelve sessions. Where the injury came from is [above](#how-an-injury-reaches-a-session).

`StatsService.GetTrainingAttendanceAsync` / `GetPlayerTrainingAttendanceAsync` are the way in, beside
the other reports. **Neither carries an admin guard of its own and neither is cached**:
`TrainingService.GetAllAsync` is the guard, and a cache entry in front of it would hand the
attendance to a caller that never passed it.

## Restrict, and the guard in front of it

`Season → Training` is `Restrict`, like `Season → Game`: a session records attendance, so deleting a
season must never take a year of it away silently. `SeasonService.DeleteAsync` counts trainings
beside games and refuses with a readable message, rather than letting the caller hit a raw
`DbUpdateException`.

**It counts the ones that record something.** `IsUnusedSchedule` evenings are removed along with the
season instead of blocking it — in memory, since the property does not translate. Without that,
saving a training period would be the thing that locked a season in place: ninety rows nobody had
written on, and a refusal reading *"still has 90 trainings"* for a season that holds no attendance
at all.

## The one read that is not public

Everything else in this app is readable without signing in — the squad, the fixtures, the
statistics. Trainings are not: `TrainingService.GetAllAsync` goes through
`ServiceOperation.RunAdminAsync`, not `RunAsync`, and `/trainings` carries
`[Authorize(Roles = AppRoles.Admin)]` on top of that. Who missed a session, and the note usually
saying why, is a personal fact rather than a team one — and the markup gate stops holding the moment
the service is reached another way, which is the rule in
[patterns](../patterns/authorization-and-auth.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).

The menu entry carries `RequiresRole` for the same reason: a visitor offered a link that only bounces them
to `/login` has been told the section exists and nothing else.

## The schedule writes the sessions

`MatchPreferences.TrainingDays` — per season, beside `MatchDay` — bounded by
`FirstTrainingDate`/`LastTrainingDate`, the season's **training period**. Saving those in
`/settings` is what creates the rows: `MatchPreferencesService.SaveTrainingScheduleAsync` diffs the
season's sessions against `TrainingSchedule.DatesIn(first, last, days)` in the same `SaveChanges` that
writes the schedule, so ninety evenings stop being ninety trips through a dialog. The match defaults
share the row and are saved by a method of their own, which never touches the sessions at all.

**Both ends or no schedule.** An open end means "the season's own window" everywhere else, and a
session for every training day until the end of June is not what ticking a weekday asks for — so
`TrainingDays` on their own generate nothing, and clearing either end takes the generated evenings
back out again. That is the undo for a period entered against the wrong dates.

**Only when the schedule moved.** `SyncTrainingsAsync` compares the stored period and training days
against the incoming ones first and does nothing when they match, so a save that changed the game
length leaves the calendar alone. Without that gate, deleting an evening and later touching any
unrelated preference would bring it straight back, since the diff has no memory of a row that is no
longer there. **Deleting is for a session entered by mistake; a week off is what "Did not take place"
is for** — that keeps the row, so the schedule cannot re-create it, and the week still reads
honestly.

What the diff may remove is only `IsUnusedSchedule`: generated, with nothing recorded against it. An
evening carrying absences, a note or a cancellation outlives the window it was drawn from and is the
admin's to delete. So does one entered by hand — `TrainingDialog.Submit` clears `FromSchedule` on
every save, so a session the coach has opened is the coach's, and the extra Friday in the summer
survives the next save of the training settings.

The period is still a bound on what gets *proposed* rather than a rule about what may be *entered*:
a one-off outside it saves without complaint. What is validated is the period itself — see
[settings](settings-and-users.md#matchpreferences-one-row-per-season).
