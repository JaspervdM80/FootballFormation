---
name: domain-model
description: The entities, enums and cascade rules, the team every season belongs to, and where domain logic belongs. Use when adding or changing a property or entity under Club, Team, Season, Game, Training or Player, when a rule needs a new computed member, or when a delete/cascade decision is involved.
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
- **A goal's shown minute is derived, not stored.** A live goal keeps `AtSeconds` and
  `MatchClockReport.MinuteOf` derives the minute; `Minute` is stored only for a typed-in or legacy goal
  and is a scoreboard reading, not elapsed time — order on `MatchClockReport.ElapsedOf`.
- **`GamePlayerPosition.SlotIndex` is the source of truth for pitch placement**, not `Position`.
  `(GamePeriodId, PlayerId)` is unique: a player appears once per period, pitch or bench, never both.

## Every season belongs to a team

`Club 1──* Team 1──* Season`. `Season.TeamId` is the source of truth; `Game`, `Training`,
`MatchPreferences` and `SeasonSquadMember` carry a **denormalised copy**, set from the season at
creation, so `AppDbContext`'s query filter scopes a read by one column without a join.
`PushSubscription` carries one too. **`Player` belongs to the club** (`ClubId`), not a team: a
season's squad draws from the club pool, and a girl moving between the club's teams keeps one history.
`Season.IsCurrent` is one row per team, and the season gap/overlap rules run within a team.

A new entity hanging off a season gets the same treatment in the same change: a `TeamId`, a
`HasQueryFilter` in `AppDbContext.OnModelCreating`, a `Restrict` FK to `Team`, and a migration that
backfills the column from the season (`ScopeSeasonDataToTeams` is the worked example). A child of a
`Game` (a goal, an injury) gets none of that — it is reached through the filtered game, and a write
by its own id gates on `GameInScopeAsync` (see the `ef-core-and-queries` skill).

## Enums

`PlayerPosition` (16), `FormationType` (17 — eleven-a-side, then the nine-a-side four appended),
`MatchType` (3, descriptive only — nothing in the reports branches on it), `MatchState`,
`GameSplitType`, `PeriodType`, `UserRole` (`Admin`, `ApplicationAdmin` — the second implies the
first). **Never renumber a member**: the numbers are in the database, which is why new formations
are appended rather than filed beside the shapes they resemble.

**`MatchFormat` is never stored.** `FormationType.Format()` reads it back off the shape, the way
`PeriodCount` is read off the period table, so a game cannot claim one format and field another.

**Duplicate positions in a formation are the design.** `F442.DefaultPositions()` returns two CBs and
two STs; which slot a player occupies comes from `SlotIndex` (ordered by `FormationSlots.OrdinalOf`).
The side-specific members that used to exist — LCB, RCB, LWB, RWB, LCDM, RCDM, LCM, RCM, LCAM, RCAM,
LF, RF, CF, LST, RST — were consolidated away by two migrations that have since been folded into
`InitialCreate`, so there is no file left to read the reasoning from. **Do not reintroduce them:**
`SlotIndex` is what disambiguates two players in the same role, and adding a side-specific member
back would give the pitch two ways to say the same thing.

## Cascades

```
Club 1──* Team 1──* Season 1──* Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player *──1 Club
Season 1──* SeasonSquadMember *──1 Player
Season 1──* Training (Restrict — no navigation either way)
Season 1──1 MatchPreferences
Game 1──* GameGoal *──1 Player (scorer, assister — both SetNull)
Game 1──* GameSubstitution *──1 Player (off, on — both Restrict)
Game 1──* GameInjury *──1 Player (Restrict)
Game 1──* GamePositionSwap *──1 Player (a, b — both Restrict)
Game 1──* GameComment *──1 AppUser (author — SetNull)
```

Cascading throughout **except Season → Game and Season → Training, which are `Restrict`**: deleting a
season must never take a year of games, lineups, goals or attendance with it. `SeasonService.DeleteAsync`
refuses with a readable message rather than letting the caller hit a raw `DbUpdateException`. Every
FK to `Club` or `Team` is `Restrict` too, so a team delete has no silent path through its data — bar
`PushSubscription`, a browser's opt-in with no history, which goes with its team.

`SeasonSquadMember` cascades from both the season and the player, and `MatchPreferences` from its
season — pure membership and pure configuration, with no history of their own, so they must not make
a person or a game-free season undeletable.

A `Training` names no player by foreign key: who missed it is a list of ids in a text column, like
`Game.UnavailablePlayerIds`. It is also the one record that is **not a public read**.

Players are **archived, not deleted**, where a delete would take history with it.

Full property tables: [docs/models/](../../../docs/models/index.md)
