---
name: domain-model
description: The entities, enums and cascade rules, and where domain logic belongs. Use when adding or changing a property on Player, Season, Game, GamePeriod, GameGoal or GameSubstitution, when a rule needs a new computed member, or when a delete/cascade decision is involved.
---

# Domain model

## Domain logic lives on the model

Anything computable without the database goes on the entity, not in a service or a page:
`Game.PeriodCount`, `Game.PeriodDurationSeconds`, `Game.IsInRoster`, `Game.SelectRoster`,
`Game.LiveHalf()`, `Game.NextHalf()`, `Game.MidHalfPlan()`, `GameSplitTypeExtensions.*`.

`PeriodCount` derives from `PeriodTypeExtensions.ForSplitType`, so the count can never drift from the
periods actually created. The split-type extensions take the *duration* rather than a `Game`, so the
game dialog can preview a split that has not been saved yet and get the answer the saved game will
give.

Report builders live in `Core/Reporting/` as pure static functions, never in a page — `UI` is a Razor
Class Library meant to be reusable.

## Pass a value object, don't eager-load a navigation

`Game.IsInRoster(player, squad)` takes a `SeasonSquad` rather than reading `Game.Season.SquadMembers`,
because `Game.Season` is nullable: a query that forgot the `.Include` would silently answer "everyone
is a guest" and empty the roster, with no compile-time signal, on any of `GameService`'s four read
paths. `SeasonSquad.Empty` is an honest degraded value; a null nav is not.

The plural `SeasonSquads` exists one level up: reports walk games across seasons, so each game
resolves *its own* season's squad — a player who was a guest one year and a regular the next is judged
correctly in each.

## The rules worth knowing before changing a member

- **The match clock is an anchor plus a banked total, never a ticking value.**
  `ElapsedSecondsAt(utcNow)` adds the time since `ClockRunningSince` to `ClockAccumulatedSeconds`, so
  every viewer derives the same clock from one row without the server pushing each second, and a
  refresh or a second device picks it up exactly where it is.
- **`Game.IsComplete` decides whether a game counts towards statistics at all** — the final whistle
  went, or the game was never run live and has a final score. A match in progress is never complete
  however many goals are logged, or the season table would shift while it is still being played.
- **`PeriodDurationSeconds`, not minutes.** A duration that splits into fractions of a minute (50 in
  quarters is 4 × 12.5) still splits exactly into seconds, so the periods add back up to the full
  match length. Every planned-minutes calculation reads this one.
- **`ScoreHome`/`ScoreAway` are ours/theirs regardless of venue.** `IsHomeGame` is venue only.
- `Game.CountOurGoals`/`CountTheirGoals` are the one place the scoreline rule lives: an own goal
  counts for the opponent. `CountScoreFrom` is a **recount**, not an increment — see the
  `ef-core-and-queries` skill.
- **A goal's minute is derived, not stored.** `GameGoal.Minute` is a scoreboard reading, not elapsed
  time — convert with `MatchClockReport.ElapsedOf` before ordering on it.
- **`GamePlayerPosition.SlotIndex` is the source of truth for pitch placement**, not `Position`.
  `(GamePeriodId, PlayerId)` is unique: a player appears once per period, pitch or bench, never both.

## Enums

`PlayerPosition` (16), `FormationType` (12), `MatchType` (3, descriptive only — nothing in the reports
branches on it), `MatchState`, `GameSplitType`, `PeriodType`, `UserRole`.

**Duplicate positions in a formation are the design.** `F442.DefaultPositions()` returns two CBs and
two STs; which slot a player occupies comes from `SlotIndex` (ordered by `FormationSlots.OrdinalOf`).
The side-specific members that used to exist — LCB, RCB, LWB, RWB, LCDM, RCDM, LCM, RCM, LCAM, RCAM,
LF, RF, CF, LST, RST — were deleted by the `ConsolidatePlayerPositions` and
`ConsolidatePositionsRound2` migrations. **Do not reintroduce them.**

## Cascades

```
Season 1──* Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player
Season 1──* SeasonSquadMember *──1 Player
Game 1──* GameGoal *──1 Player (scorer, assister — both SetNull)
Game 1──* GameSubstitution *──1 Player (off, on — both Restrict)
Game 1──* GameComment *──1 AppUser (author — SetNull)
```

Cascading throughout **except Season → Game, which is `Restrict`**: deleting a season must never take
a year of games, lineups and goals with it. `SeasonService.DeleteAsync` refuses with a readable
message rather than letting the caller hit a raw `DbUpdateException`.

`SeasonSquadMember` and `MatchPreferences` cascade from *both* parents — pure membership and pure
configuration, with no history of their own, so they must not make a person or a game-free season
undeletable.

Players are **archived, not deleted**, where a delete would take history with it.

Full property tables: [docs/models.md](../../../docs/models.md)
