# Season and Squad

## Season
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| TeamId | int | FK → Team, `Restrict`. The team this season belongs to; the root every season-scoped row reaches its team through |
| Name | string | Required, max 20. e.g. "2025/26". Editable |
| StartDate | DateTime | Unique per team — `(TeamId, StartDate)` |
| EndDate | DateTime | |
| IsCurrent | bool | Exactly one row **per team**. `SeasonService.SetCurrentAsync` owns the invariant |
| Games | List\<Game\> | |
| SquadMembers | List\<SeasonSquadMember\> | This season's squad |

Seasons belong to a **team** (`TeamId`), and everything under a season — games, trainings,
preferences, the squad — carries a denormalised copy so a read scopes by one column. The gapless-
window and current-season rules below all run **within a team**; two teams may share a 2025/26
window, and each has its own current season. See
[enums-and-relationships](enums-and-relationships.md) and
[authorization-and-auth](../patterns/authorization-and-auth.md).

Seasons run **1 July – 30 June** (`Season.StartMonth = 7`), matching the KNVB amateur season.
The windows are deliberately **gapless** — every date maps to exactly one of the team's seasons,
which is what lets `Game.SeasonId` be required and `GetOrCreateForDateAsync` always resolve. An Aug–Jun window
would orphan July fixtures and force an "unassigned" branch into every filter and list.

That was documented but unenforced, and it bit: a hand-entered 2026/27 starting 1 August left all
of July 2026 belonging to no season, so the game dialog answered every July date with "this date
starts a new season" and an empty squad. Three things now hold the invariant up:

- `ValidateAsync` rejects a **gap** as well as an overlap, naming the date the season should
  start or end on.
- `GetOrCreateForDateAsync` **clamps** an auto-created season to its neighbours, so filling a
  gap narrower than a full season cannot produce an overlapping window.
- `CloseSeasonGapsAsync` is an idempotent startup repair for databases written before the check
  existed. It only ever moves a start date *earlier*, and never moves a game between seasons —
  `Game.SeasonId` is stored on the game itself.

Helpers on the model: `Contains(date)` (date-only), `ShortName` ("25/26", for the app bar),
`StartYearFor(date)`, `NameForStartYear(year)`, and `CreateFor(date)` for a fresh unsaved season.

## SeasonSquadMember
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| SeasonId | int | FK → Season, **Cascade** delete |
| PlayerId | int | FK → Player, **Cascade** delete |
| IsGuest | bool | Guest **for this season only** |
| IsInjured | bool | Generally injured **for this season only** |

Unique index on `(SeasonId, PlayerId)` — one row per player per season.

The squad is **authoritative**: it decides who can be picked for that season's games and who appears
in its stats. This is what stops a past season showing today's squad. Guest and injury status both
live here rather than on `Player`, and for the same reason: someone can be a guest in 2025/26 and a
full squad member in 2026/27, or injured this season and not the next.

Cascade on both sides is the exception to the Restrict rule below: a membership row carries no
history, so it must never block deleting a person or an (already game-free) season.

New seasons start with an **empty** squad; they are populated by `SeasonSquadService.CopyFromAsync`
("copy squad from {previous season}" on `/players`), which preserves guest flags and is idempotent.
Injury status is deliberately **not** carried forward — every copied row starts fit, since an injury
is expected to have healed by the time next season's squad is set up, unlike guest status, which is
a standing arrangement. `RemoveMemberAsync` refuses once the player has minutes or goals that season.

### SeasonSquad / SeasonSquads
Two immutable value objects in `Models/SeasonSquad.cs`, not entities:

- **`SeasonSquad`** — one season's members as a lookup: `Contains(id)`, `IsGuest(id)`,
  `IsFullMember(id)`, `IsInjured(id)`, `Players` / `FullMembers` / `Guests` / `Injured`, plus
  `SeasonSquad.Empty`. Its constructor owns the guests-last ordering that `PlayerService.GetAllAsync`
  used to provide.
- **`SeasonSquads`** — several seasons keyed by id, for reports spanning them: `For(seasonId)`,
  `AllPlayers`, `IsFullMemberAnywhere(playerId)`, `Of(squad)`, `SeasonSquads.Empty`.

`IsGuest` returns true for anyone **outside** the squad as well as for actual guests. Both mean
"not a regular", which collapses three membership states back into the two branches the roster rule
always had, and keeps games referencing a since-departed player rendering sensibly.

