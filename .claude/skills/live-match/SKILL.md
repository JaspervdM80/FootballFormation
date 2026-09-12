---
name: live-match
description: Working on the live match screen, the match clock, goals or substitutions. Covers the halves-only model, NextHalf skipping a mid-half plan, the derived goal minute, the substitution tie-break, and the four-service split. Use for /games/{id}/live, MatchClockService, MatchGoalService, MatchSubstitutionService or MatchClockReport.
---

# Live match

`/games/{id}/live` is phone-first single column, with **no `[Authorize]`**: an admin drives it,
everyone else watches the same URL read-only. Every control sits inside
`<AuthorizeView Roles="@AppRoles.Admin">`.

## Four services, one per thing that happens at the touchline

| Service | Owns |
|---|---|
| `LiveMatchService` | Reading — `GetLiveAsync`, `GetTodaysMatchAsync`. Public like every other read |
| `MatchClockService` | Kick-off, half time, next half, final whistle, and `BankClock` |
| `MatchGoalService` | The live minute a goal is stamped with; storage delegates to `GameService` |
| `MatchSubstitutionService` | The slot swap and its record, in one `SaveChanges`, an injury, and undo |

A page injecting all four is expected. A **facade** over them is the signal the split was cut along
the wrong line.

Every touchline write runs inside `LiveMatchOperation`, which wraps `ServiceOperation.RunAdminAsync`
and raises `LiveMatchNotifier` itself on success — three services each remembering to notify is worse
than one.

## This screen knows halves and nothing else

A quarters game is **two halves with two line-ups each**. The second line-up of a half is a plan the
coach works through by hand: nothing rolls it on, the clock never stops for it, and it is never a
stage of the match here. The controls are therefore identical for both splits — "Half time"
(`EndHalfAsync`), "Start 2nd half" (`StartNextHalfAsync`), "Finish match" throughout.

**`Game.NextHalf()` skips the quarter left behind inside a half already played**, so the second half
opens at Q3 and Q2 is never kicked off. A line-up with no timings costs `GameMinutesReport` nothing,
which is what leaves the whole half credited to the line-up that actually played it.

**There is no pause.** The clock runs from kick-off until the half is whistled off; only half time
stops it. `PauseClockAsync`/`ResumeClockAsync` are gone from `MatchClockService`, not just from the
screen — a clock a stray tap can stop is a clock the season's minutes cannot be trusted from.

## The clock never round-trips

A per-circuit 1-second `System.Timers.Timer` re-renders `Game.ElapsedSecondsAt(DateTime.UtcNow)` from
the anchor the server stored, repainting only while the clock runs. The clock is an anchor plus a
banked total, so a refresh or a second device picks it up exactly where it is.

**`GetLiveAsync` is `AsNoTrackingWithIdentityResolution`, and it has to be.** A spectator's circuit
keeps one scoped context for its life, so a tracked `Game` keeps returning the score, clock and state
from its first load while newly inserted goals appear alongside them — a live screen stuck at the old
scoreline.

`LiveMatchNotifier` is a **singleton**, in-process only. The page filters on its own `GameId`, reloads,
`InvokeAsync(StateHasChanged)`, and **unsubscribes in `Dispose`** — a handler never removed keeps a
dead circuit's component alive. It needs a backplane if the app is ever scaled out.

## A goal's minute is derived, never stored

A goal carries `GamePeriodId` + `AtSeconds`, exactly as a substitution does.
`MatchClockReport.MinuteOf` derives the displayed reading from the half's own timings.

**Two different scales, and that is the point.** The scoreboard reading stops at the end of the half,
so a goal two minutes into first-half stoppage and one just after the restart both read in the
thirties; the elapsed clock runs on across the break and puts them in the order they happened.

Everything sorts on **elapsed seconds** (`MatchClockReport.ElapsedOf`), then `RecordedAt`, then the
id — the timeline, the result page's goal list, and `ScoreProgressionReport`. A goal typed in on
`/result` has only a scoreboard minute, and `ElapsedOf` converts it back through the half timings
rather than reading it as elapsed time; the two scales part company by however long a half over-ran,
and taking one for the other puts a second-half goal under the half-time rule.

**A goal is corrected, not removed and retyped** (`EditGoalAsync`, `EditGoalDialog`): re-entering one on
`/result` leaves only a scoreboard minute, losing the half and the clock reading. The edit keeps the
shape the goal was recorded in — a live goal stays placed by the clock inside its own half, a hand-typed
one keeps its `Minute` — and a minute left as it was shown keeps the stored reading, for the same
stoppage-time reason `ElapsedForEditedMinute` exists. Which side it counts for is fixed.

**A half whistled off late is corrected by moving its end, never its start** (`AdjustHalfLengthsAsync`,
finished matches only). Every event stores an absolute elapsed second and `MatchClockReport` reads
`elapsed - start`, so shifting a start would silently re-time everything in the half; writing
`EndedAtSeconds` alone re-times nothing and takes the overrun back off `GameMinutesReport`.

Half time is a **dashed rule across the timeline**, not an event.

## Substitutions

Tapping a player offers two changes, one dropdown each (`LiveSubDialog`): someone comes on for them
(`SubstituteAsync`), or they trade positions with a team-mate who stays on (`SwapPositionsAsync`).
Choosing in either list clears the other, so the single action button always has exactly one change to
make.

**A position swap writes no `GameSubstitution`** — nobody's minutes changed, and a row there would say
they did. The price is the *split by position*: `GameMinutesReport` reads the lineup as it finally
stands, so after a swap the whole half is credited to the position each player moved **into**. Totals
are unaffected.

**Undoing a substitution follows the slot, not the recorded one** — a swap can have moved it since,
and handing the recorded slot back would seat two players in it.

**Any substitution can be undone or edited, from the live screen or the result page**, not only the
newest — as long as the player it brought on is still on the pitch. Undo follows her to wherever she
stands now; once a later change has taken her off, that one goes first (`RemoveSubstitutionAsync`
refuses with *"Undo the later substitution first"*). `EditSubstitutionAsync` (`EditSubDialog`) changes
who came on and the minute by **reversing the line-up change and re-applying** with the same
`TakeOffThePitch`/`BringOnThePitch` a live sub uses, so the slot chain and the minutes stay
consistent; the minute round-trips through `MatchClockReport.ElapsedForMinute` and is clamped inside
its half. **A minute left alone keeps the stored reading** (`ElapsedForEditedMinute`) — the shown
minute drops stoppage time (30+3 reads 30), so converting an untouched one back would move the change
minutes earlier. Candidates come from the substitution's **own** half, not the one on screen. A
substitution made for an injury is not editable — its leaver is fixed by the injury.

Two substitutions in the same second settle by **id**, not just the clock.

**Half-time changes are substitutions too, recorded when the next half kicks off.** At the break the
pitch shows the *upcoming* half and is tappable; a tap only edits that line-up
(`PlanBreakSubstitutionAsync` writes no row, offers who comes on and nothing else). `StartNextHalfAsync`
then diffs the half just finished against the one starting (`LineupDiff.Swaps`, injured excluded) and
writes a `GameSubstitution` per paired change at the restart second. Minutes are untouched: each sits
at `AtSeconds == start`, so the next half rewinds it back to its starter and credits the leaver no
second-half time. A next half never planned is **carried over** from whoever finished the last one
(`EndHalfAsync`), so the break is never an empty pitch. This is why between-half changes are no longer
just a silent line-up difference.

## An injury is a substitution that also stops the clock on her availability

`MarkInjuredAsync` lives in the same service because it is the same write: it takes her off the
pitch, and brings a replacement on when one was picked. What it adds is a `GameInjury` — the only
record that can say the rest of the match was never hers to play, which is what
`Game.AvailableMinutesFor` reads and `PlayerStats.Utilization` divides by.

**No replacement is a real case**, and it is the one the injury row has to carry alone: nothing else
says she left the pitch, so `GameMinutesReport` walks an unreplaced injury as a line-up change and
skips a replaced one (`Game.WasReplaced`). Walking both would take her off twice and hand her slot
back in the rewind.

One touchline action, one timeline entry: a substitution made for an injury is marked with the red
cross rather than listed twice, and undoing it removes both rows. The standing
`SeasonSquadMember.IsInjured` flag is a different thing — it has no date, so it can say nothing
about a match.

Each select's `Placeholder` is set **only** when its list is empty: MudSelect shows a placeholder
whenever nothing is chosen, so a standing "nobody is on the bench" would greet a full bench.

Detail: [docs/ui_components/](../../../docs/ui_components/live-match-screen.md) ·
[docs/known_issues/](../../../docs/known_issues/live-match.md)
