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
| IsGuest | bool | Guest players are excluded from every game unless listed in `Game.GuestPlayerIds` |
| DisplayName | string | Computed: "First Last" or "First" |
| ShortName | string | Computed: "F. Last" or "First" |

## Season
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Name | string | Required, max 20. e.g. "2025/26". Editable |
| StartDate | DateTime | Unique index |
| EndDate | DateTime | |
| IsCurrent | bool | Exactly one row. `SeasonService.SetCurrentAsync` owns the invariant |
| Games | List\<Game\> | |

Seasons run **1 July – 30 June** (`Season.StartMonth = 7`), matching the KNVB amateur season.
The windows are deliberately **gapless** — every date maps to exactly one season, which is what
lets `Game.SeasonId` be required and `GetOrCreateForDateAsync` always resolve. An Aug–Jun window
would orphan July fixtures and force an "unassigned" branch into every filter and list.

Helpers on the model: `Contains(date)` (date-only), `ShortName` ("25/26", for the app bar),
`StartYearFor(date)`, `NameForStartYear(year)`, and `CreateFor(date)` for a fresh unsaved season.

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
| GuestPlayerIds | List\<int\> | Guests opted **in**. Comma-separated |

A game's season is resolved in `GameService.CreateAsync`: `SeasonId == 0` means "auto by date"
(the game dialog's default) and is looked up via `SeasonService.GetOrCreateForDateAsync`, creating
the season if the date falls beyond those defined. An explicit id passes through untouched, and
changing a game's date later never silently moves it between seasons.

`Game.IsInRoster(player)` / `Game.SelectRoster(players)` centralize the rule: squad players
are in unless marked unavailable, guests are out unless explicitly added. Use these rather
than filtering on the id lists directly.

## GamePeriod
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| GameId | int | FK → Game (cascade delete) |
| PeriodType | PeriodType | FirstHalf, SecondHalf, FirstQuarter..FourthQuarter |
| FormationTypeOverride | FormationType? | Null = use game's formation |
| PlayerPositions | List\<GamePlayerPosition\> | |

## GamePlayerPosition
| Property | Type | Notes |
|---|---|---|
| Id | int | PK, auto-generated |
| GamePeriodId | int | FK → GamePeriod (cascade delete) |
| PlayerId | int | FK → Player (cascade delete) |
| Position | PlayerPosition | Slot on the pitch |
| IsSubstitute | bool | True = bench player |

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
```
Cascading deletes throughout, **except Season → Game, which is `Restrict`**: deleting a season must
never take a year of games, lineups and goals with it. `SeasonService.DeleteAsync` refuses with a
readable message when a season still has games, or when it is the current one, rather than letting
the caller hit a raw `DbUpdateException`.

MatchPreferences is standalone (singleton row).
