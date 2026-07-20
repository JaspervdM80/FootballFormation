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

## Game
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| Opponent | string | Required, max 100 |
| Date | DateTime | |
| Notes | string? | |
| FormationType | FormationType | |
| SplitType | GameSplitType | Halves or Quarters |
| GameDurationMinutes | int | Default 60 |
| IsHomeGame | bool | Default true. Venue only — score fields are unaffected |
| ScoreHome / ScoreAway | int? | **Our** score / **opponent** score, regardless of venue |
| Periods | List\<GamePeriod\> | Auto-created on game creation |
| UnavailablePlayerIds | List\<int\> | Squad players opted **out**. Comma-separated |
| GuestPlayerIds | List\<int\> | Guests opted **in**. Comma-separated |

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
Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player
```
All cascading deletes. MatchPreferences is standalone (singleton row).
