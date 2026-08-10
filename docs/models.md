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
| MatchType | MatchType | Competition / Cup / Practice. Descriptive only — every type counts towards statistics |
| FormationType | FormationType | |
| SplitType | GameSplitType | Halves or Quarters |
| GameDurationMinutes | int | Default 60 |
| IsHomeGame | bool | Default true. Venue only — score fields are unaffected |
| ScoreHome / ScoreAway | int? | **Our** score / **opponent** score, regardless of venue |
| Periods | List\<GamePeriod\> | Auto-created on game creation |
| Goals | List\<GameGoal\> | Cascade delete |
| UnavailablePlayerIds | List\<int\> | Squad players opted **out**. Comma-separated |
| GuestPlayerIds | List\<int\> | Guests **of this game's season**, opted in. Comma-separated |
| MatchState | MatchState | NotStarted / InProgress / Finished. Driven by the live match screen |
| ClockRunningSince | DateTime? | UTC anchor; null whenever the clock is stopped |
| ClockAccumulatedSeconds | int | Seconds banked from earlier running stretches |
| LivePeriodId | int? | The period on the pitch. Null before kick-off, at the break and after full time |
| Substitutions | List\<GameSubstitution\> | Cascade delete |
| Comments | List\<GameComment\> | Cascade delete. Never eager-loaded — see GameComment |

The match clock is stored as an **anchor plus a banked total**, never as a ticking value:
`ElapsedSecondsAt(utcNow)` adds the time since `ClockRunningSince` to `ClockAccumulatedSeconds`.
Every viewer therefore derives the same clock from one row without the server pushing each second,
and a page refresh or a second device picks it up exactly where it is.

`Game.CountOurGoals(goals)` / `Game.CountTheirGoals(goals)` are the one place the scoreline rule
lives: an own goal counts for the opponent, so it is excluded from ours and included in theirs.

**`Game.IsComplete` decides whether a game counts towards statistics at all**: the final whistle
went on the live screen, or the game was never run live and has a final score on file. A match in
progress is never complete however many goals are logged, or the season table and the scorer lists
would shift while it is still being played. Five more computed members support the reports:

| Member | Answers |
|---|---|
| `HasLineup` | Does any period have someone on the pitch? Needs `PlayerPositions` loaded |
| `HasActualTimings` | Was any period actually kicked off, i.e. are there real timings to prefer over the plan? |
| `PlayedDurationSeconds` | The same sum in seconds, without the fallback — the denominator for a share of one game's playing time, where truncating to minutes would let an ever-present player round past 100% |
| `PlayedDurationMinutes` | How long the match really lasted, summed over the periods played out; falls back to `GameDurationMinutes`. The denominator for utilisation, so a match that over-ran cannot push anyone past 100% |
| `CurrentOrLastPeriod()` | The period the match is *about*: the live one, else the last played, else the first — so the live screen is never blank |

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
| Minute | int? | Free-typed on `/result`; stamped from the clock on `/live` |
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
| Minute | int | Computed: `AtSeconds / 60 + 1` — a timeline's first minute is 1', not 0' |
| RecordedAt | DateTime | UTC entry time — orders events that share a minute |

`RecordedAt` exists on both `GameGoal` and `GameSubstitution` because the minute alone cannot order
a timeline: a goal and the substitution that followed it routinely share one, and several events in
the opening minute is the normal case, not the edge case. The live timeline sorts by minute then by
`RecordedAt`, both descending. Rows written before the column existed default to `0001-01-01`, so
historic events in the same minute keep an arbitrary (but stable) order.

The lineup stays the source of truth for *who stands where*; this records **when** the swap
happened, which the period lineup alone cannot express. `MatchSubstitutionService.SubstituteAsync` writes
both in one `SaveChangesAsync`, so they cannot diverge — and it updates the lineup **in place**
rather than going through `GameService.SavePeriodLineupAsync`, which is delete-and-reinsert.

Both player legs are `Restrict`, not `Cascade`: two cascading paths from `Players` to the same row
is the shape SQLite rejects, and neither leg is nullable, so deleting a player who was substituted
fails loudly instead of silently rewriting match history.

Only the **most recent** substitution of a period can be undone (`RemoveSubstitutionAsync`);
reversing an older swap would fight every change made on that slot since.

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
The `AddMatchTypeAndComments` migration carries every non-empty `Notes` value over as one
admin-only comment dated to the match.

**Visibility is enforced in the query, not in the markup.** `GameService.GetCommentsAsync(gameId,
includePrivate)` is the only read path, and it is deliberately *not* an `.Include` on
`GetByIdAsync`: `/games/{id}/result` prerenders server-side, so a private body filtered out only in
the razor would still ship in a visitor's HTML. The page passes `includePrivate: IsAdmin`, read from
the same cascading auth state that decides what it renders.

**And the service does not take that flag on trust.** `GetCommentsAsync` re-confirms it against
`ICurrentUser`, so a caller passing `true` without being an admin gets the public comments and
nothing else. This is the one read in the app with something to hide, which makes it the wrong
place for a boolean argument nobody checks — see [patterns.md](patterns.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).

Indexed on `(GameId, CreatedAt)` — every read is "this game's comments, newest first". The author
leg is `SetNull` like `GameGoal.Scorer`: a comment is part of the match record and outlives the
account that wrote it.

## MatchPreferences (one row per season)
| Property | Type | Default |
|---|---|---|
| Id | int | PK |
| SeasonId | int | FK -> Season, **Cascade** delete. Unique index |
| GameDurationMinutes | int | 60 |
| DefaultSplitType | GameSplitType | Halves |
| DefaultFormation | FormationType | F442 |
| MatchDay | DayOfWeek | Saturday |

The defaults a new game starts from are **per season**, not per app: a team moving up an age group
plays longer games and often a different shape, and the fixture day can move too. Keeping one row
per season means setting this year's values never rewrites the ones last year's games were created
under.

The row is created on first read by `MatchPreferencesService.GetAsync(seasonId)`, seeded via
`MatchPreferences.CopyFor` from the newest season **before** it that has one — so a new season
inherits last year's settings rather than the hardcoded 4-4-2 / 60 minutes, and per-season storage
costs the user no extra work. There is no "current season" overload — every caller has a season in
hand, from the picker or from the game being edited.

`GetNextMatchDateAsync(seasonId)` uses that season's `MatchDay`, counts only that season's games,
and keeps its answer inside the season window: it measures from the opening day for a season not
started yet, and falls back to the last match day of the window for one already over. Without that
clamp, adding the first fixture of next season proposed a date in the season we are living in.

## AppUser (table `Users`)
| Property | Type | Notes |
|---|---|---|
| Id | int | PK |
| DisplayName | string(100) | The person, shown in the app bar and the user list |
| Username | string(50) | The login. **Unique index** |
| PasswordHash | string | PBKDF2, via `PasswordHasher<AppUser>` — never a plaintext column |
| Role | UserRole | Stored as int. Written into the auth cookie as `Role.ToString()` |
| SecurityStamp | string(64) | Guid "N". Changes whenever the account's authority does |
| MustChangePassword | bool | Set on the account a fresh install seeds, whose password is public knowledge. While true the session can sign in and nothing else — every route sends it to `/settings`, and `ICurrentUser.IsAdminAsync()` answers false, so the services refuse it too. Cleared by `ChangePasswordAsync` |

Nothing an account owns can make it undeletable. The one reference to it — `GameComment.AuthorId` —
is `SetNull`, so deleting a user leaves their comments in place, unattributed.

**The role is the grant.** `[Authorize(Roles = AppRoles.Admin)]` and
`<AuthorizeView Roles="@AppRoles.Admin">` match `Role.ToString()`, which `AppRoles` ties back to the
enum member name — so renaming a `UserRole` member breaks the build rather than quietly
unauthorizing everyone. Anonymous (not signed in) is not a role and needs no member.

**SecurityStamp is what makes a change take effect now.** The cookie lasts eight hours and is
sliding, so without it, deleting an account or changing its role would leave the old session working
until it lapsed. The stamp is copied into the cookie at sign-in and re-checked on every authenticated
request by `OnValidatePrincipal` (Program.cs) via `UserService.FindForSessionAsync`; a mismatch
rejects the principal and signs the browser out. `UserService` regenerates it on password change and
role change — but deliberately **not** on a rename, which changes nothing about what the account may
do. Note that a live Blazor circuit is not re-validated per SignalR message: revocation lands on the
next HTTP request.

`UserService.DeleteAsync` and `UpdateAsync` both refuse to remove or demote the **last** Admin —
the one operation with no way back short of editing the database by hand. `EnsureAdminSeededAsync`
runs on every startup and does nothing once any account exists, so a changed password survives.

## Key Enums
- **UserRole** (1): Admin. See AppUser above — the member name *is* the claim value
- **MatchType** (3): Competition (0), Cup, Practice. Descriptive only — nothing in the reports
  branches on it. `DisplayName()` returns the English name, which is also the resx key
- **PlayerPosition** (16 values): GK, LB, CB, RB, DEF, CDM, CM, LM, RM, CAM, MID, LW, RW, W, ST, ATT
- **FormationType** (12): F442, F433, F4231, F352, F343, F4141, F4411, F532, F541, F4321, F3421, F3511
- **Duplicate positions in a formation are normal.** `F442.DefaultPositions()` returns two CBs and
  two STs, and that is fine: which slot a player occupies comes from
  `GamePlayerPosition.SlotIndex` (ordered by `FormationSlots.OrdinalOf`), not from the enum member.
  The side-specific members that used to exist for this — LCB, RCB, LWB, RWB, LCDM, RCDM, LCM, RCM,
  LCAM, RCAM, LF, RF, CF, LST, RST — were deleted by the `ConsolidatePlayerPositions` and
  `ConsolidatePositionsRound2` migrations. Do not reintroduce them.

## Relationships
```
Season 1──* Game 1──* GamePeriod 1──* GamePlayerPosition *──1 Player
Season 1──* SeasonSquadMember *──1 Player
Game 1──* GameGoal *──1 Player (scorer, assister — both SetNull)
Game 1──* GameSubstitution *──1 Player (off, on — both Restrict)
Game 1──* GameComment *──1 AppUser (author — SetNull)
```
Cascading deletes throughout, **except Season → Game, which is `Restrict`**: deleting a season must
never take a year of games, lineups and goals with it. `SeasonService.DeleteAsync` refuses with a
readable message when a season still has games, or when it is the current one, rather than letting
the caller hit a raw `DbUpdateException`.

`SeasonSquadMember` cascades from *both* parents — it is pure membership with no history of its own,
so it must not make a person or a game-free season undeletable. Deleting a season therefore takes
its squad rows with it; deleting a **person** removes them from every season's squad and cascades
their lineup and goal rows (see [known_issues.md](known_issues.md)).

`Season 1--1 MatchPreferences` — cascade, like `SeasonSquadMember`: a preferences row is pure
configuration with no history, so it must never make an otherwise game-free season undeletable.
