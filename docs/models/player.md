# Player

## Player
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| FirstName | string | Required, max 50 |
| Surname | string? | Optional, max 50 |
| ShirtNumber | int? | Optional |
| PreferredPosition | PlayerPosition | Single preferred |
| AlternativePositions | List\<PlayerPosition\> | Stored as comma-separated ints |
| IsArchived | bool | Default false. Has left the club — see below |
| DisplayName | string | Computed: "First Last" or "First" |
| ShortName | string | Computed: "F. Last" or "First" |

`Player` is a season-agnostic **person** record, deliberately with no guest flag and no membership
navigation. Whether someone is in the squad, and whether they are a guest, belongs to a season —
see `SeasonSquadMember`.

### Archiving, and why deleting is guarded
`GamePlayerPosition` and `SeasonSquadMember` cascade from this row and `GameGoal` nulls out, so
deleting a person edits **every season they played** — last season's top scorer vanishing from last
season's table, from a click that only said "are you sure". `PlayerService.DeleteAsync` therefore
refuses once the player has any lineup or goal rows, counted across all seasons because the cascade
is across all seasons too. Delete is still there for the case it is for: someone entered by mistake,
with nothing behind them yet.

`IsArchived` is what an admin reaches for instead. It changes nothing that already exists — the
archived player keeps their squad memberships, minutes, goals and statistics, exactly as they were —
and only takes them out of the two places that decide **future** seasons:

| Filters on `IsArchived` | Does not |
|---|---|
| `SeasonSquadService.GetNonMembersAsync` — the "add existing player" picker | `PlayerService.GetAllAsync`, the id → name lookup every page resolves against |
| `SeasonSquadService.CopyFromAsync` — copying a squad into a new season | `GetSquadAsync` / `GetSquadsAsync`, and so every report built from them |
|  | `Game.IsInRoster`, which must judge a past game the way it was played |

Restoring is the same call with `archived: false`. An archived player still shows in the squads of
the seasons they played, badged `ARCHIVED` on `/players`, which is where the restore lives.

