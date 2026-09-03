# Game

## Game
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Opponent | string | Required, max 100 |
| Date | DateTime | Optional kick-off time in the time component — `HasStartTime` is the test, `GameDialog`'s "Kick-off Time" field is how it's set |
| SeasonId | int | FK → Season, **required**. Auto-derived from `Date` on creation, reassignable. Delete is **Restrict** |
| MatchType | MatchType | Competition / Cup / Practice. Descriptive only — every type counts towards statistics |
| FormationType | FormationType | |
| SplitType | GameSplitType | Halves or Quarters |
| GameDurationMinutes | int | Default 60 |
| IsHomeGame | bool | Default true. Venue only — score fields are unaffected |
| MeetTime | TimeSpan? | When to be at the club (home) or when the convoy leaves (away). The label flips on `IsHomeGame`, the column does not. Read and written through `ClockText` |
| WarmUpTime | TimeSpan? | Briefing and warm-up. `ClockText`, as above |
| DressingRoom | string? | Max 50 |
| FieldName | string? | Max 50, e.g. "Veld 3" |
| SportsPark | string? | Max 100 |
| City | string? | Max 100 |
| DressingRoomDuty / FlagDuty / WashDuty | string? | Max 100 each. Who is down for the job, written as a name — not a `Player` reference, because it is usually a parent |
| ScoreHome / ScoreAway | int? | **Our** score / **opponent** score, regardless of venue |
| Periods | List\<GamePeriod\> | Auto-created on game creation |
| Goals | List\<GameGoal\> | Cascade delete |
| UnavailablePlayerIds | List\<int\> | Squad players opted **out**. Comma-separated |
| InjuredPlayerIds | List\<int\> | Squad players who missed it injured. Comma-separated. Written once, at the final whistle — see below |
| GuestPlayerIds | List\<int\> | Guests **of this game's season**, opted in. Comma-separated |
| MatchState | MatchState | NotStarted / InProgress / Finished. Driven by the live match screen |
| ClockRunningSince | DateTime? | UTC anchor; null whenever the clock is stopped |
| ClockAccumulatedSeconds | int | Seconds banked from earlier running stretches |
| LivePeriodId | int? | The line-up on the pitch — the row that opened the half being played. Null before kick-off, at half time and after full time |
| Substitutions | List\<GameSubstitution\> | Cascade delete |
| Injuries | List\<GameInjury\> | Cascade delete. Players hurt during this match |
| Comments | List\<GameComment\> | Cascade delete. Never eager-loaded — see GameComment |

The nine **match-day** columns exist for one reader, `MatchInfoTextBuilder`, and every one of them is
optional: a field left blank is left out of the message rather than printed empty, which is what lets
one shape serve a club that fills in everything and a coach who only ever types a departure time.
`GameDialog` writes whitespace back as null for the same reason. They carry no `Player` reference —
the flags and the kit wash are a parent's job, and the app has no row for a parent.

The match clock is stored as an **anchor plus a banked total**, never as a ticking value:
`ElapsedSecondsAt(utcNow)` adds the time since `ClockRunningSince` to `ClockAccumulatedSeconds`.
Every viewer therefore derives the same clock from one row without the server pushing each second,
and a page refresh or a second device picks it up exactly where it is.

`Game.CountOurGoals(goals)` / `Game.CountTheirGoals(goals)` are the one place the scoreline rule
lives: an own goal counts for the opponent, so it is excluded from ours and included in theirs.
`CountScoreFrom(goals)` applies both to a game at once, and it is a **recount** rather than an
increment on purpose — a score derived afresh from the goals repairs itself, which is what lets the
final whistle and the next goal logged both settle a scoreline that drifted. See
[patterns](../patterns/transactions-and-writes.md).

**`Game.IsComplete` decides whether a game counts towards statistics at all**: the final whistle
went on the live screen, or the game was never run live and has a final score on file. A match in
progress is never complete however many goals are logged, or the season table and the scorer lists
would shift while it is still being played. More computed members support the reports:

| Member | Answers |
|---|---|
| `PeriodDurationSeconds` | How long one period lasts on an even split. **Seconds, not minutes** — a duration that splits into fractions of a minute (50 in quarters is 4 × 12.5) still splits exactly into seconds, so the periods add back up to the full match length. Every planned-minutes calculation reads this one |
| `PeriodDurationMinutes` | The same length as a `decimal`, fractional when it has to be. Display only |
| `HasLineup` | Does any period have someone on the pitch? Needs `PlayerPositions` loaded |
| `HasActualTimings` | Was any half actually kicked off, i.e. are there real timings to prefer over the plan? |
| `PlayedDurationSeconds` | The same sum in seconds, without the fallback — the exact seconds a game was live for |
| `SecondsToMinutes(seconds)` | Rounds to the nearest minute (ties to even). The one conversion every played/available minutes figure goes through, on this type and in `PlayerStatsReport` — see [known_issues](../known_issues/domain.md) for why a figure summed across several games must stay in seconds until this is called once, at the end, rather than being applied per game and summed |
| `PlayedDurationSecondsEffective` | `PlayedDurationSeconds` when the game was run live, `GameDurationMinutes * 60` otherwise — the seconds form of `PlayedDurationMinutes`. What an accumulator spanning several games should sum |
| `PlayedDurationMinutes` | `SecondsToMinutes(PlayedDurationSecondsEffective)`. The denominator for one game's utilisation |
| `AvailableSecondsFor(playerId)` | `PlayedDurationSecondsEffective`, cut short at the moment that player went off hurt. The seconds form of `AvailableMinutesFor` |
| `AvailableMinutesFor(playerId)` | `AvailableSecondsFor(playerId)`, rounded. A single game's figure only — `PlayerStatsReport` sums `AvailableSecondsFor` across a player's games and rounds once, rather than adding up this already-rounded result, so being carried off at 20' is not scored as an hour on the bench and a season of over-running matches cannot push utilisation past 100% |
| `WasReplaced(injury)` / `InjuryFor(substitution)` | The two directions of the pairing between an injury and the substitution made for it. One touchline action writes both rows, and both the timeline and `GameMinutesReport` need to know which injuries a substitution already accounts for |
| `CurrentOrLastHalf()` | The half the match is *about*, as the line-up it is played with: the live one, else the last played, else the one the match opens with — so the live screen is never blank |
| `LiveHalf()` | The half on the pitch, or null before kick-off, at half time and after full time. What a substitution may touch |
| `NextHalf()` | The half the clock goes to next, as the line-up opening it. Skips a line-up planned for the middle of a half already played, so a quarters second half opens at Q3 |
| `MidHalfPlan(half)` | The line-up planned to take over partway through that half, or null. Only a quarters game has one, and the clock never stops for it — the live screen offers it as a reference |
| `HasStartTime` | Was a kick-off time set, or is `Date`'s time component just midnight? |
| `DateLine(format)` | `Date` formatted, plus the kick-off time when there is one — the one place the result page, the overview and the copyable summary share this |
| `HasFinalScore` | Is the scoreline settled — not `InProgress`, and both scores on file? Splits `/games` into fixtures and results, and gates the copyable summary's existence |
| `InVenueOrder(us, them)` / `ScoreboardOrder()` | Flips a (us, them) pair — or the game's own `ScoreHome`/`ScoreAway` — into home-first order. The one flip between what is stored and what a scoreboard shows, used by the home banner and the copyable summary |

A game's season is resolved in `GameService.CreateAsync`: `SeasonId == 0` means "auto by date"
(the game dialog's default) and is looked up via `SeasonService.GetOrCreateForDateAsync`, creating
the season if the date falls beyond those defined. An explicit id passes through untouched, and
changing a game's date later never silently moves it between seasons.

`Game.IsInRoster(player, squad)` / `Game.SelectRoster(players, squad)` centralize the rule: squad
players are in unless marked unavailable or recorded injured, guests are out unless explicitly
added. Use these rather than filtering on the id lists directly.

**Deliberately blind to `SeasonSquad.IsInjured`**, the same way it is blind to `Player.IsArchived`
(see above): this judges a game the way it was played, and `PlayerStatsReport.AvailableMinutes` reads
it for games already complete, so a status set after the fact must never rewrite one. A caller
building a *future* line-up — `FormationBuilder.RosterPlayers`, `GameDialog.SquadPlayers`,
`LiveMatch.SubCandidates` — filters injured players out itself, on top of this.

Injury reaches the rule through the game's own `InjuredPlayerIds` instead, which is history rather
than a status. `StandingInjuries.RecordAsync` copies the flag into it **once, on the transition to
`IsComplete`** — from `MatchClockService.FinishMatchAsync` for a match run live, and from
`GameService.SaveScoreAsync` for one played on paper — because `SeasonSquadMember.IsInjured` carries
no date and there is nothing left to reconstruct from once it is cleared. Anyone named in a line-up
is left out of the copy however she was flagged: the line-up is the better witness of who was
actually there. Correcting a score that had already settled the match does not restamp it, so a
recovery weeks later cannot backdate itself into matches that were played before it.

The season's squad is passed **in** rather than eager-loaded through `Game.Season`. That is
deliberate: `Game.Season` is nullable, so any query forgetting the `.Include` chain would silently
report "everyone is a guest" and empty the roster, with no compile-time signal — and `GameService`
has four read paths. An explicit parameter makes the dependency visible and `SeasonSquad.Empty` an
honest degraded value.

There is a second overload, `IsInRoster(player, squads)`, taking the plural `SeasonSquads`. Reports
walk games that may span seasons (the picker's "All seasons"), and each game resolves its own
season's squad — so a player who was a guest one year and a regular the next is judged correctly in
each. `PlayerStatsReport.Build` and `SeasonStatsReport.Build` both take `SeasonSquads` for this reason.

## GamePeriod
One **planned line-up**, for a half or for a quarter. The match itself is only ever two halves, so
the row that opens a half is the one the live screen plays, times and records against, while a row
planned for the middle of a half stays a plan and is never kicked off. `PeriodType.Half()` maps one
to the other.

| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| PeriodType | PeriodType | FirstHalf, SecondHalf, FirstQuarter..FourthQuarter |
| FormationTypeOverride | FormationType? | Null = use game's formation. Nothing in the UI sets one, and changing the game's formation clears them all |
| StartedAtSeconds | int? | Match-clock second the half this opens kicked off. Null unless run live, and always null for a plan for the middle of a half |
| EndedAtSeconds | int? | Match-clock second that half was whistled off |
| PlayerPositions | List\<GamePlayerPosition\> | |

## GamePlayerPosition
| Property | Type | Notes |
|---|---|---|
| Id | int | PK, auto-generated |
| GamePeriodId | int | FK → GamePeriod (cascade delete) |
| PlayerId | int | FK → Player (cascade delete) |
| Position | PlayerPosition | Which role — not which slot; see SlotIndex |
| SlotIndex | int? | **The source of truth for pitch placement.** Which of the formation's slots this is, so two CBs stay distinguishable. Null for a substitute |
| IsSubstitute | bool | True = bench player |

`(GamePeriodId, PlayerId)` is unique: a player appears once per period, on the pitch or on the
bench, never both and never twice.

## GameGoal
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| ScorerId | int? | FK → Player, **SetNull**. Null for an opponent goal — we don't track their players |
| AssisterId | int? | FK → Player, SetNull |
| GamePeriodId | int? | FK → GamePeriod (cascade delete). The half that was being played. Null for a goal typed in on `/result` |
| AtSeconds | int? | Match-clock second the ball went in. Null for the same reason |
| Minute | int? | Free-typed on `/result`, and the fallback for goals logged before `AtSeconds` existed. Not written by `/live` any more. A scoreboard reading, not elapsed time — convert with `MatchClockReport.ElapsedOf` before ordering on it |
| IsOwnGoal | bool | One of ours into our own net. Counts for the opponent |
| IsOpponentGoal | bool | The opponent scored. Counts for them, and has no scorer |
| RecordedAt | DateTime | UTC entry time — orders events that share a minute |

## GameSubstitution
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| GamePeriodId | int | FK → GamePeriod (cascade delete) |
| PlayerOffId / PlayerOnId | int | FK → Player, both **Restrict** |
| AtSeconds | int | Match-clock second of the change |
| SlotIndex | int? | The pitch slot that changed hands |
| Position | PlayerPosition | The position that changed hands |
| RecordedAt | DateTime | UTC entry time — orders events that share a minute |

**Neither kind of event stores the minute it is shown against.** Both store where they happened —
the half, and the reading on the match clock — and `MatchClockReport.MinuteOf` derives the minute
from that pair, so the two kinds read off one code path and correcting a half's `StartedAtSeconds`
corrects the goals in it as well as the substitutions. A goal typed in on `/result` has no clock
behind it and falls back to `Minute`; a goal with neither shows no minute at all, which the result
page allows.

`RecordedAt` exists on both `GameGoal` and `GameSubstitution` because the clock alone cannot order
a timeline: a goal and the substitution that followed it routinely share a second, and several
events in the opening minute is the normal case, not the edge case. The live timeline sorts by
elapsed seconds (`MatchClockReport.ElapsedOf`, `GameSubstitution.AtSeconds`), then by `RecordedAt`,
then by `Id`, all descending. Rows written before the column existed default to `0001-01-01`, and
two changes entered in one instant share it, so `RecordedAt` cannot settle a double substitution on
its own — the id is the last word, and it is the same one `RemoveSubstitutionAsync` uses, so the
entry the timeline puts on top is the entry whose Undo works.
Ids from the two tables are not comparable with each other, so a goal and a substitution that tie on
both minute and `RecordedAt` keep an arbitrary (but stable) order.

The lineup stays the source of truth for *who stands where*; this records **when** the swap
happened, which the line-up alone cannot express. `MatchSubstitutionService.SubstituteAsync` writes
both in one `SaveChangesAsync`, so they cannot diverge — and it updates the lineup **in place**
rather than going through `GameService.SavePeriodLineupAsync`, which is delete-and-reinsert.

Both player legs are `Restrict`, not `Cascade`: two cascading paths from `Players` to the same row
is the shape SQLite rejects, and neither leg is nullable, so deleting a player who was substituted
fails loudly instead of silently rewriting match history.

Only the **most recent** substitution of a half can be undone (`RemoveSubstitutionAsync`);
reversing an older swap would fight every change made on that slot since. "Most recent" is
`AtSeconds` then `Id`: a double substitution puts two rows in the same second, and the id is what
says which of them came second. `GameMinutesReport` walks them in that same order — see
[known_issues](../known_issues/domain.md).

## GameInjury
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete). Unique with `PlayerId` — one injury per player per match |
| GamePeriodId | int | FK → GamePeriod (cascade delete) |
| PlayerId | int | FK → Player, **Restrict**, for the same reason as `GameSubstitution`'s legs |
| AtSeconds | int | Match-clock second she went off |
| SlotIndex | int? | The pitch slot she left, so the record can be undone |
| Position | PlayerPosition | The position she was holding — the minutes before it are credited there |
| RecordedAt | DateTime | UTC entry time — orders events that share a minute |

**A player hurt on the day, as opposed to `SeasonSquadMember.IsInjured`, which is a standing status
with no time dimension.** The flag says she cannot be picked; this says the rest of *this* match was
never hers to play, which is a thing only a moment on the clock can say. `Game.AvailableMinutesFor`
is what reads it, and `PlayerStats.Utilization` is what it changes: carried off on 20' of an hour,
she is judged on 20 minutes rather than on 60. The other 40 become `PlayerStats.InjuredMinutes` —
which `Game.InjuredPlayerIds` also feeds, with the whole of a match she missed rather than played
part of. Two records, because they answer different questions: this one needs a moment on the clock,
that one only needs to survive the flag being cleared.

`MatchSubstitutionService.MarkInjuredAsync` writes it, with an optional replacement. **Naming one
writes a `GameSubstitution` beside the injury, in the same `SaveChangesAsync`**; leaving it out
writes only the injury, and the team plays on a player short. `Game.WasReplaced(injury)` /
`Game.InjuryFor(substitution)` are the pairing, and two things read it:

- `GameMinutesReport` walks an **unreplaced** injury as the line-up change it is — nothing else
  records that she left the pitch. A replaced one is skipped, because its substitution already
  takes her off and walking both would hand her slot back in the rewind.
- The live timeline shows one entry per touchline action: a substitution made for an injury is
  marked with the cross rather than listed twice, and undoing it removes both rows.

Undoing an unreplaced injury (`RemoveInjuryAsync`) puts her back in the slot she left, and refuses
if anything is standing in it — which nothing should be, since a swap needs two players already on
and a substitution reuses the slot of whoever it takes off.

## GameComment
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| Body | string(2000) | Required |
| IsPublic | bool | **Default false** — admin-only until deliberately published |
| AuthorId | int? | FK → AppUser, **SetNull**. Shown to admins only |
| CreatedAt | DateTime | UTC |
| EditedAt | DateTime? | Null until the body changes. Publishing alone is not an edit |

Replaced the old `Game.Notes` string, which was written in the game dialog and displayed nowhere.
The `AddMatchTypeAndComments` migration carried every non-empty `Notes` value over as one
admin-only comment dated to the match.

**Visibility is enforced in the query, not in the markup.** `GameService.GetCommentsAsync(gameId,
includePrivate)` is the only read path, and it is deliberately *not* an `.Include` on
`GetByIdAsync`: `/games/{id}/result` prerenders server-side, so a private body filtered out only in
the razor would still ship in a visitor's HTML. The page passes `includePrivate: IsAdmin`, read from
the same cascading auth state that decides what it renders.

**And the service does not take that flag on trust.** `GetCommentsAsync` re-confirms it against
`ICurrentUser`, so a caller passing `true` without being an admin gets the public comments and
nothing else. This is the one read in the app with something to hide, which makes it the wrong
place for a boolean argument nobody checks — see [patterns](../patterns/authorization-and-auth.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).

Indexed on `(GameId, CreatedAt)` — every read is "this game's comments, newest first". The author
leg is `SetNull` like `GameGoal.Scorer`: a comment is part of the match record and outlives the
account that wrote it.

