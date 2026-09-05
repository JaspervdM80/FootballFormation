# Key Enums and Relationships

## Key Enums
- **UserRole** (2): Admin, ApplicationAdmin. See AppUser above — the member name *is* the claim
  value, and `ApplicationAdmin` implies `Admin` because `PrincipalFor` mints both claims
- **MatchType** (3): Competition (0), Cup, Practice. Descriptive only — nothing in the reports
  branches on it. `DisplayName()` returns the English name, which is also the resx key
- **PlayerPosition** (16 values): GK, LB, CB, RB, DEF, CDM, CM, LM, RM, CAM, MID, LW, RW, W, ST, ATT
- **FormationType** (13): F442, F433, F4231, F352, F343, F4141, F4411, F532, F541, F4321, F3421, F3511,
  F442Diamond ("4-4-2 diamond", the only shape whose label is localized — "4-4-2 ruit")
- **Duplicate positions in a formation are normal.** `F442.DefaultPositions()` returns two CBs and
  two STs, and that is fine: which slot a player occupies comes from
  `GamePlayerPosition.SlotIndex` (ordered by `FormationSlots.OrdinalOf`), not from the enum member.
  The side-specific members that used to exist for this — LCB, RCB, LWB, RWB, LCDM, RCDM, LCM, RCM,
  LCAM, RCAM, LF, RF, CF, LST, RST — were deleted by the `ConsolidatePlayerPositions` and
  `ConsolidatePositionsRound2` migrations. Do not reintroduce them.

## Relationships
```
Team 1──* Season 1──* Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player *──1 Club
Season 1──* SeasonSquadMember *──1 Player
Season 1──* Training (Restrict — no navigation in either direction)
Game 1──* GameGoal *──1 Player (scorer, assister — both SetNull)
GamePeriod 1──* GameGoal (the half it was scored in — nullable, cascade)
GamePeriod 1──* GameSubstitution (the half it was made in — cascade)
Game 1──* GameSubstitution *──1 Player (off, on — both Restrict)
GamePeriod 1──* GameInjury (the half it happened in — cascade)
Game 1──* GameInjury *──1 Player (Restrict; unique on GameId + PlayerId)
Game 1──* GameComment *──1 AppUser (author — SetNull)
```

**Everything under a season carries a `TeamId`, and a `Player` a `ClubId`** (id-only FKs, all
`Restrict`, like `Club → Team` and `Team → Users`). `Season` is the source of truth; `Game`,
`Training`, `MatchPreferences` and `SeasonSquadMember` hold a denormalised copy set from the season
at creation, so `AppDbContext`'s global query filters scope a read by one column without a join. A
player belongs to the club rather than a team, so a season's squad draws from the club pool and a
move between the club's teams keeps one history. **`Season.IsCurrent` is now one row per team**, and
the season gap/overlap rules in `SeasonService` run within a team. See
[authorization-and-auth](../patterns/authorization-and-auth.md) for the read-side scoping and the
`FindAsync` trap.
Cascading deletes throughout, **except Season → Game and Season → Training, which are `Restrict`**:
deleting a season must never take a year of games, lineups, goals or training attendance with it.
`SeasonService.DeleteAsync` refuses with a readable message when a season still has games or
trainings that record something, or when it is the current one, rather than letting the caller hit a
raw `DbUpdateException`. Generated sessions nobody has written on go with the season — see
[training](training.md#restrict-and-the-guard-in-front-of-it).

`Training` names no players by foreign key at all — who was absent is a list of ids in a text column,
like `Game.UnavailablePlayerIds`. See [training](training.md).

`SeasonSquadMember` cascades from *both* parents — it is pure membership with no history of its own,
so it must not make a person or a game-free season undeletable. Deleting a season therefore takes
its squad rows with it; deleting a **person** removes them from every season's squad and cascades
their lineup and goal rows (see [known_issues](../known_issues/index.md)).

`Season 1--1 MatchPreferences` — cascade, like `SeasonSquadMember`: a preferences row is pure
configuration with no history, so it must never make an otherwise game-free season undeletable.
