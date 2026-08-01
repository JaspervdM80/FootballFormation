# Domain Models

## Player
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| FirstName | string | Required, max 50 |
| Surname | string? | Optional, max 50 |
| ShirtNumber | int? | Optional |
| PreferredPosition | PlayerPosition | Single preferred |
| AlternativePositions | List\<PlayerPosition\> | Stored as comma-separated ints |
| DisplayName | string | Computed: "First Last" or "First" |
| ShortName | string | Computed: "F. Last" or "First" |

`Player` is a season-agnostic **person** record, deliberately with no guest flag and no membership
navigation. Whether someone is in the squad, and whether they are a guest, belongs to a season —
see `SeasonSquadMember`.

## Season
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Name | string | Required, max 20. e.g. "2025/26". Editable |
| StartDate | DateTime | Unique index |
| EndDate | DateTime | |
| IsCurrent | bool | Exactly one row. `SeasonService.SetCurrentAsync` owns the invariant |
| Games | List\<Game\> | |
| SquadMembers | List\<SeasonSquadMember\> | This season's squad |

Seasons run **1 July – 30 June** (`Season.StartMonth = 7`), matching the KNVB amateur season.
The windows are deliberately **gapless** — every date maps to exactly one season, which is what
lets `Game.SeasonId` be required and `GetOrCreateForDateAsync` always resolve. An Aug–Jun window
would orphan July fixtures and force an "unassigned" branch into every filter and list.

Helpers on the model: `Contains(date)` (date-only), `ShortName` ("25/26", for the app bar),
`StartYearFor(date)`, `NameForStartYear(year)`, and `CreateFor(date)` for a fresh unsaved season.

## SeasonSquadMember
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| SeasonId | int | FK → Season, **Cascade** delete |
| PlayerId | int | FK → Player, **Cascade** delete |
| IsGuest | bool | Guest **for this season only** |

Unique index on `(SeasonId, PlayerId)` — one row per player per season.

The squad is **authoritative**: it decides who can be picked for that season's games and who appears
in its stats. This is what stops a past season showing today's squad. Guest status lives here rather
than on `Player` so that someone can be a guest in 2025/26 and a full squad member in 2026/27.

Cascade on both sides is the exception to the Restrict rule below: a membership row carries no
history, so it must never block deleting a person or an (already game-free) season.

New seasons start with an **empty** squad; they are populated by `SeasonSquadService.CopyFromAsync`
("copy squad from {previous season}" on `/players`), which preserves guest flags and is idempotent.
`RemoveMemberAsync` refuses once the player has minutes or goals that season.

### SeasonSquad / SeasonSquads
Two immutable value objects in `Models/SeasonSquad.cs`, not entities:

- **`SeasonSquad`** — one season's members as a lookup: `Contains(id)`, `IsGuest(id)`,
  `IsFullMember(id)`, `Players` / `FullMembers` / `Guests`, plus `SeasonSquad.Empty`. Its
  constructor owns the guests-last ordering that `PlayerService.GetAllAsync` used to provide.
- **`SeasonSquads`** — several seasons keyed by id, for reports spanning them: `For(seasonId)`,
  `AllPlayers`, `IsFullMemberAnywhere(playerId)`, `Of(squad)`, `SeasonSquads.Empty`.

`IsGuest` returns true for anyone **outside** the squad as well as for actual guests. Both mean
"not a regular", which collapses three membership states back into the two branches the roster rule
always had, and keeps games referencing a since-departed player rendering sensibly.

## Game
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Opponent | string | Required, max 100 |
| Date | DateTime | |
| SeasonId | int | FK → Season, **required**. Auto-derived from `Date` on creation, reassignable. Delete is **Restrict** |
| Notes | string? | |
| FormationType | FormationType | |
| SplitType | GameSplitType | Halves or Quarters |
| GameDurationMinutes | int | Default 60 |
| IsHomeGame | bool | Default true. Venue only — score fields are unaffected |
| ScoreHome / ScoreAway | int? | **Our** score / **opponent** score, regardless of venue |
| Periods | List\<GamePeriod\> | Auto-created on game creation |
| UnavailablePlayerIds | List\<int\> | Squad players opted **out**. Comma-separated |
| GuestPlayerIds | List\<int\> | Guests **of this game's season**, opted in. Comma-separated |
| MatchState | MatchState | NotStarted / InProgress / Finished. Driven by the live match screen |
| ClockRunningSince | DateTime? | UTC anchor; null whenever the clock is stopped |
| ClockAccumulatedSeconds | int | Seconds banked from earlier running stretches |
| LivePeriodId | int? | The period on the pitch. Null before kick-off, at the break and after full time |
| Substitutions | List\<GameSubstitution\> | Cascade delete |

The match clock is stored as an **anchor plus a banked total**, never as a ticking value:
`ElapsedSecondsAt(utcNow)` adds the time since `ClockRunningSince` to `ClockAccumulatedSeconds`.
Every viewer therefore derives the same clock from one row without the server pushing each second,
and a page refresh or a second device picks it up exactly where it is.

`Game.CountOurGoals(goals)` / `Game.CountTheirGoals(goals)` are the one place the scoreline rule
lives: an own goal counts for the opponent, so it is excluded from ours and included in theirs.

A game's season is resolved in `GameService.CreateAsync`: `SeasonId == 0` means "auto by date"
(the game dialog's default) and is looked up via `SeasonService.GetOrCreateForDateAsync`, creating
the season if the date falls beyond those defined. An explicit id passes through untouched, and
changing a game's date later never silently moves it between seasons.

`Game.IsInRoster(player, squad)` / `Game.SelectRoster(players, squad)` centralize the rule: squad
players are in unless marked unavailable, guests are out unless explicitly added. Use these rather
than filtering on the id lists directly.

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
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| PeriodType | PeriodType | FirstHalf, SecondHalf, FirstQuarter..FourthQuarter |
| FormationTypeOverride | FormationType? | Null = use game's formation |
| StartedAtSeconds | int? | Match-clock second it kicked off. Null unless run live |
| EndedAtSeconds | int? | Match-clock second it was whistled off |
| PlayerPositions | List\<GamePlayerPosition\> | |

## GamePlayerPosition
| Property | Type | Notes |
|---|---|---|
| Id | int | PK, auto-generated |
| GamePeriodId | int | FK → GamePeriod (cascade delete) |
| PlayerId | int | FK → Player (cascade delete) |
| Position | PlayerPosition | Slot on the pitch |
| IsSubstitute | bool | True = bench player |

## GameGoal
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| ScorerId | int? | FK → Player, **SetNull**. Null for an opponent goal — we don't track their players |
| AssisterId | int? | FK → Player, SetNull |
| Minute | int? | Free-typed on `/result`; stamped from the clock on `/live` |
| IsOwnGoal | bool | One of ours into our own net. Counts for the opponent |
| IsOpponentGoal | bool | The opponent scored. Counts for them, and has no scorer |

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
| Minute | int | Computed: `AtSeconds / 60 + 1` — a timeline's first minute is 1', not 0' |

The lineup stays the source of truth for *who stands where*; this records **when** the swap
happened, which the period lineup alone cannot express. `LiveMatchService.SubstituteAsync` writes
both in one `SaveChangesAsync`, so they cannot diverge — and it updates the lineup **in place**
rather than going through `GameService.SavePeriodLineupAsync`, which is delete-and-reinsert.

Both player legs are `Restrict`, not `Cascade`: two cascading paths from `Players` to the same row
is the shape SQLite rejects, and neither leg is nullable, so deleting a player who was substituted
fails loudly instead of silently rewriting match history.

Only the **most recent** substitution of a period can be undone (`RemoveSubstitutionAsync`);
reversing an older swap would fight every change made on that slot since.

## MatchPreferences (singleton)
| Property | Type | Default |
|---|---|---|
| GameDurationMinutes | int | 60 |
| DefaultSplitType | GameSplitType | Halves |
| DefaultFormation | FormationType | F442 |
| MatchDay | DayOfWeek | Saturday |

## Key Enums
- **PlayerPosition** (32 values): GK, LB, LCB, CB, RCB, RB, LWB, RWB, DEF, LCDM, RCDM, CDM, LCM, CM, RCM, LM, RM, LCAM, RCAM, CAM, MID, LW, RW, W, LF, RF, CF, LST, RST, ST, ATT
- **FormationType** (12): F442, F433, F4231, F352, F343, F4141, F4411, F532, F541, F4321, F3421, F3511
- Dual-slot variants: LCDM/RCDM for 4-2-3-1, LST/RST for 4-4-2, LCAM/RCAM for dual-CAM formations

## Relationships
```
Season 1──* Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player
Season 1──* SeasonSquadMember *──1 Player
Game 1──* GameGoal *──1 Player (scorer, assister — both SetNull)
Game 1──* GameSubstitution *──1 Player (off, on — both Restrict)
```
Cascading deletes throughout, **except Season → Game, which is `Restrict`**: deleting a season must
never take a year of games, lineups and goals with it. `SeasonService.DeleteAsync` refuses with a
readable message when a season still has games, or when it is the current one, rather than letting
the caller hit a raw `DbUpdateException`.

`SeasonSquadMember` cascades from *both* parents — it is pure membership with no history of its own,
so it must not make a person or a game-free season undeletable. Deleting a season therefore takes
its squad rows with it; deleting a **person** removes them from every season's squad and cascades
their lineup and goal rows (see [known_issues.md](known_issues.md)).

MatchPreferences is standalone (singleton row).
