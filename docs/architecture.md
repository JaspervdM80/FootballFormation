# Architecture & File Map

## Core (`src/FootballFormation.Core/`)
```
Models/
  Player.cs              — Player entity (FirstName, Surname, ShirtNumber, PreferredPosition, AlternativePositions)
  Position.cs            — PlayerPosition enum (16 values), PositionCategory enum, extensions
  FormationType.cs       — FormationType enum (13 formations), DisplayName(), DefaultPositions()
  Season.cs              — Season entity (1 Jul – 30 Jun windows), Contains/ShortName/CreateFor helpers
  SeasonSquadMember.cs   — Per-season squad membership, with the per-season IsGuest flag
  SeasonSquad.cs         — SeasonSquad + SeasonSquads value objects (immutable membership lookups)
  Game.cs                — Game entity (incl. SeasonId + live match clock/state), GameSplitType and MatchState enums
  GamePeriod.cs          — GamePeriod entity, PeriodType enum, PeriodTypeExtensions
  GamePlayerPosition.cs  — Links player to position in a period (IsSubstitute flag)
  GameGoal.cs            — A goal: scorer (null for the opponent), assister, the half + clock reading it was
                           scored at, own/opponent flags
  GameSubstitution.cs    — A timestamped change made during a live match
  MatchPreferences.cs    — Per-season game defaults (duration, split, formation, match day, training days
                           and the training period the last of those is walked inside)
  Training.cs            — A training session: date, who was unavailable, whether it went ahead, one
                           note, and whether the schedule wrote it. Plus TrainingOrdering
  TrainingSchedule.cs    — The dates a season's training period implies, as a pure function
  GameComment.cs         — An admin's note on a game: body, public/private, author, edited marker
  MatchType.cs           — MatchType enum (Competition / Cup / Practice) + DisplayName()
  AppUser.cs             — An account that can sign in: name, login, hash, role, security stamp
  UserRole.cs            — UserRole enum (Admin, ApplicationAdmin); the member name is the role claim value
  FormationSlots.cs      — Formation slots and lineup→slot assignment, shared by every pitch
Security/
  AppRoles.cs            — AppRoles.Admin/ApplicationAdmin (role claim constants) and AppClaims (uid,
                           display_name, security_stamp, team_id, must_change_password) — the claim
                           names Program.cs mints and the UI reads
  ICurrentUser.cs        — Who is asking, as far as a Core service is concerned. The seam that lets
                           every write path refuse a non-admin (ServiceOperation.RunAdminAsync)
  ICurrentTeam.cs        — Which team is being asked about, which is the other half of that question
  CurrentTeam.cs         — Answers it: the ff.team cookie while it names a team, else the first team
  TeamAuthority.cs       — The rule itself, as one testable function
Data/
  AppDbContext.cs         — EF Core context; DbSets only, mapping lives in Configurations/
  Configurations/         — One IEntityTypeConfiguration per entity, applied by assembly scan
    CsvListConverters.cs  — List<int>/List<TEnum> ↔ comma-separated text, with the ValueComparer
  DatabasePathHelper.cs   — Resolves the SQLite path: APP_DATA_DIR, then WEBSITE_INSTANCE_ID, then LOCALAPPDATA
  DatabaseSafety.cs       — What runs around MigrateAsync on boot: the pre-migration snapshot (one
                           per schema state, newest 5 kept) and the post-migration integrity and
                           foreign-key checks. See deployment.md
  DesignTimeDbContextFactory.cs — Lets dotnet-ef build the context from Core alone, so migration
                           commands need no --startup-project
  GameQueries.cs          — The include chains a Game is loaded with, named once as IQueryable
                           extensions and composed at the call site. Not a repository: tracking,
                           filtering and tagging stay the caller's
  QueryTags.cs            — TagWith markers. Holds ComparesDatesInSql, the only way past the test
                           suite's DateInSqlInterceptor
Reporting/
  GameMinutesReport.cs    — Playing time for one game: real timings when run live, plan otherwise
  PlayingTimeReport.cs    — The playing-time table (PlayingTimeRow, PeriodDetail, PeriodPlayStatus).
                           Minutes from GameMinutesReport once a game has been run live, the planned
                           periods × period length estimate before that; PlayingTimeRow.IsActual says
                           which. The per-period cells always come from the line-up being edited
  LiveMinutesReport.cs    — Exact minutes on the pitch during a live match
  SeasonStatsReport.cs    — Team totals + form for /stats (SeasonStats, GameResult)
  PlayerStatsReport.cs    — Per-player aggregates (PlayerStats, PositionStat, PlayerGameStat)
  PositionDevelopmentReport.cs — Pivots PlayerStats.Positions into a players × positions grid for
                           /stats/positions (PositionDevelopment, PositionDevelopmentRow), no new query
  PositionFitHelper.cs    — 5-tier position fit: Preferred, NaturalFit, Alternative, Compatible, OutOfPosition
  MatchClockReport.cs     — Derives the live clock and the half's reading from the stored anchor +
                           banked total, the MatchMinute an event is written down against, and the
                           half it belongs to
  PlannedChangesReport.cs — What the plan for the middle of a half changes versus the line-up on the
                           pitch, minus the swaps play has already overtaken, for
                           UI/Components/PlannedChangesList
  ScoreProgressionReport.cs — The score after each goal (MatchScore), for the live timeline —
                           counted forwards because that list runs newest first
  HealthReport.cs         — Whether a booted container is actually serving: the /health payload and
                           the rule that pending migrations mean unhealthy. Pure, so it is tested
Services/
  ServiceOperation.cs     — Shared try/catch + error logging wrapper for all service methods, and
                            where a cancelled call stops being an exception: OperationCanceledException
                            is caught ahead of the general handler and answered with Result.Cancelled()
  PlayerService.cs        — CRUD + SetArchived; delete refuses for anyone who has played, so a
                            person is retired rather than taken out of past seasons
  SeasonService.cs        — CRUD + SetCurrent/FindForDate/GetOrCreateForDate/EnsureCurrentSeason/CloseSeasonGaps
  SeasonSquadService.cs   — Squad membership: get/add/remove/set-guest/copy-forward, with guards
  GameService.cs          — CRUD + SavePeriodLineupAsync, optional seasonId filter, returns Result<T>
  TrainingService.cs      — CRUD over training sessions. The one service whose *reads* are
                            RunAdminAsync too: who missed a training is the only thing in this app
                            that is not a public read. See docs/models/training.md
  LiveMatchService.cs     — Reads a match being played: GetLiveAsync for everything the live screen
                            renders, GetTodaysMatchAsync for the home-page banner (in-progress
                            first, else today's fixture, upcoming or finished). Writing to one is
                            the three services below, split by what happens on the touchline
  MatchClockService.cs    — The clock and the run of play: kick-off, half time, starting the next
                            half, the final whistle. A match is two halves whatever its line-ups
                            were planned in. The arithmetic a season's statistics are built from.
                            There is no pause — the clock runs from kick-off to the whistle and
                            only half time stops it
  MatchGoalService.cs     — Goals logged live: the live minute added here, storage and the
                            recounted scoreline delegated to GameService, which writes the two in
                            one save (see patterns/transactions-and-writes.md, "When two rows have to agree")
  MatchSubstitutionService.cs — The slot swap and the record of it, in one SaveChanges, plus undoing
                            the most recent one of a half, plus SwapPositionsAsync — two players
                            already on trading slots, which writes no substitution row (so the undo
                            reads the slot back off the pitch, not off the row)
  LiveMatchOperation.cs   — The write shape those three share: RunAdminAsync plus, on success, one
                            LiveMatchNotifier call naming the game that changed
  LiveMatchQueries.cs     — The tracked load they all start from (the game with its planned
                            line-ups, via GameQueries) and the one "game not found" message
  LiveMatchNotifier.cs    — Singleton: fans live match changes out to every open circuit
  MatchPreferencesService.cs — Per-season prefs: GetAsync(seasonId)/SaveAsync,
                            GetNextMatchDateAsync/GetNextTrainingDateAsync(seasonId). SaveAsync also
                            writes the sessions the training period implies — see
                            docs/models/training.md
  UserService.cs          — Accounts + credentials: CRUD returning Result<T>, plus
                            ValidateCredentialsAsync/FindForSessionAsync/ChangePasswordAsync, which
                            return raw values rather than Result so a failed login says nothing
                            about why. Refuses to leave a team without an admin.
Result.cs                — Result and Result<T>: success/failure with a translatable error key, plus
                           IsCancelled — a failure carrying no message, for a caller who went away
```

## UI (`src/FootballFormation.UI/`) — Razor Class Library
```
Pages/
  Players.razor(.cs)          — /players — Season-scoped squad management (add/remove, edit, copy forward)
  PlayerDialog.razor(.cs)     — Dialog: first name, surname, shirt #, positions (no guest switch — that's per season)
  SquadMemberDialog.razor(.cs)— Dialog: add someone already on file to this season's squad
  Games.razor(.cs)            — /games — Game list with formation builder link
  Trainings.razor(.cs)(.css)  — /trainings — Admin-only: the season's training sessions, grouped by ISO week
  TrainingDialog.razor(.cs)   — Dialog: date, unavailable players, note, did-not-take-place
  GameDialog.razor(.cs)       — Dialog: opponent, date, season, formation, split, duration, unavailable players
  FormationBuilder.razor(.cs) — /games/{id}/formation — Pitch + player list + subs + playing time overview
  SeasonStats.razor(.cs)(.css)— /stats — Season dashboard: record, goals, form, scorers, playing time
  PlayerStats.razor(.cs)(.css)— /players/{id}/stats — Per-player figures for the selected season
  PositionDevelopment.razor(.cs) — /stats/positions — Admin-only: squad-wide players × positions grid
  MatchResult.razor(.cs)(.css)— /games/{id}/result — Score and goal entry
  LiveMatch.razor(.cs)(.css)  — /games/{id}/live — Sideline screen: clock, subs, goals; admin drives, others watch
  LiveGoalDialog.razor(.cs)   — Dialog: scorer, assister, own-goal toggle
  LiveSubDialog.razor(.cs)(.css) — Dialog: for a player tapped on the pitch, either a replacement
                                from the bench or a position swap with someone already on
  PlannedChangesDialog.razor  — Dialog: the changes still planned for the middle of this half, as a
                                reference to work through by tapping the pitch. Writes nothing
  SeasonDialog.razor(.cs)     — Dialog: season name, start date, end date
  Settings.razor(.cs)         — /settings — Match preferences, own password, season management
  Users.razor(.cs)            — /users — Accounts: add, edit, reset password, delete (Admin only)
  UserDialog.razor(.cs)       — Dialog: name, login, role, password — also the reset-password form
                                (PasswordOnly), since the fields are the same
  Home.razor(.cs)(.css)       — / — Landing page, plus the live-match banner when one is in progress
  FormationOverview.razor(.cs)(.css) — /games/{id}/overview — Read-only per-period pitches, shareable
                                and screenshottable (html2canvas)
Components/
  Pitch.razor(.cs)(.css)            — The pitch. Read-only by default; Draggable for the builder,
                                      OnPlayerClicked for the live screen, Size for chip scale
  PlayerLabel.razor                 — A player as one line of text: "#7 Jasper"
  PlannedChangesList.razor(.css)    — What the next line-up does, as a team sheet, for the live
                                      screen's PlannedChangesDialog
  CancellableComponent.cs           — Base for any component that reads: owns the CancellationToken its
                                      service reads take, tripped when the component is disposed
  SeasonAwarePage.cs                — Base for pages that follow the season picker (a CancellableComponent)
  PlayerList.razor(.cs)(.css)       — Draggable player cards (HTML5 drag API)
  SubstituteBench.razor(.cs)(.css)  — Substitute drop zone with remove buttons
  SeasonPicker.razor(.cs)           — Global season filter; rendered in both the app bar and the drawer
  VenueBadge.razor                  — THUIS/UIT, in the colours the games list stripes its cards with
  NavItems.razor(.cs)               — The main menu, from AppNav.Menu; rendered in both, ShowIcons for the drawer
  PageHeader.razor(.cs)             — Every page's title block: heading, subtitle, back arrow, actions
  BackButton.razor(.cs)             — The back arrow; follows the trail, names its destination
  ConfirmDialog.razor(.cs)          — Reusable yes/no confirmation dialog
  InstallBanner.razor(.cs)          — The "add to home screen" prompt, rendered by MainLayout
  RedirectToLogin.razor             — Routes NotAuthorized to /login (see Routes.razor)
Navigation/
  AppRoutes.cs                — Every route: constants and builders. Never interpolate a URL at a call site
  AppNav.cs                   — What each route is called, the menu, and which routes the season filters
  NavigationTrail.cs          — Scoped: where the visitor has been, so back returns there
State/
  SeasonState.cs              — Scoped: the selected season, shared by the layout and the pages
  SeasonPreference.cs         — That choice in a cookie for 8h, so a deploy's dropped circuit
                                does not reset it. Writes it; App.razor/Routes read it
  TeamState.cs                — Scoped: the club and team the app says it is, for the chrome
  TeamPreference.cs           — The team in a cookie for a year. /team/set writes it on a choice;
                                Program.cs stamps the resolved team on every page served
  RequestContext.cs           — The three cookies a scope was created with, so the static render and
                                the circuit cannot disagree about them
  NavigationTrailCookie.cs    — The last two pages served, in a cookie because enhanced navigation
                                sends the destination as the Referer. Program.cs writes it
Helpers/
  PitchPositionHelper.cs      — Maps PlayerPosition → (left%, top%) coordinates
  UiFeedback.cs               — Snackbar.Report()/ReportFailure() over Result (translates the error,
                                stays silent about a cancelled one), shared LockedDialog options
  DialogPrompts.cs            — ConfirmAsync()/ConfirmDeleteAsync(), and PromptAsync() for an
                                editing dialog that returns a value
  LineupDragState.cs          — In-flight drag on the formation builder
  PrincipalExtensions.cs      — ClaimsPrincipal.IsAdmin()/DisplayName()/UserId()/AdminTeamId(). Use
                                IsAdmin(), never Identity.IsAuthenticated — being signed in is not a role
Theming/
  ClubTheme.cs                — The club palette: emits the CSS custom properties AND the MudTheme
Layout/
  MainLayout.razor(.cs)       — MudBlazor layout, club light theme, app-bar nav + drawer, providers.
                                Both nav renderings are <NavItems />, so a menu change is one edit
                                in AppNav.Menu.
Security/
  CircuitCurrentUser.cs       — Core's ICurrentUser, answered from the circuit's auth state and the
                                team in scope. The implementation every RunAdminAsync depends on; it
                                answers false for an account still on its seeded password
Strings.cs                    — Marker type for IStringLocalizer<Strings>. No English resx: the
                                English text is the key
Strings.nl.resx               — The Dutch translations, the app's default culture
GlobalUsings.cs               — Aliases our MatchType over System.IO's, which implicit usings pull in
```

Report builders live in **Core** (`Core/Reporting/`), not the UI: minutes played, utilisation and
position share are domain answers, and keeping them out of the Razor project is what makes them
testable. They are pure static functions taking their scope as parameters — the game list, and
(since per-season squads) a `SeasonSquads`. Season filtering therefore happens at the call site, and
the builders never touch the database. The squads parameter is not optional: guest status is per
season, and a report may walk games spanning several of them.

## Render modes: most pages have no circuit
`@rendermode InteractiveServer` is declared **per page**, never on `<Routes>` or `<HeadOutlet>`.
Nine pages carry it — the start page, the games list, the squad and the trainings list, the four
game screens, and settings and users. Everything else is plain server HTML: `/stats`, `/stats/positions`,
`/players/{id}/stats`, `/games/{id}/overview`, `/login`, `/Error` and `/not-found`.

The reason is a phone. Backgrounding an installed PWA suspends the tab and kills the circuit's
WebSocket; coming back puts up a blocking overlay and, past the retention window, forces a reload
over whatever signal there is. A page with no circuit has none of that to lose, and the pages a
parent actually opens are exactly the read-only ones. `tests/ui/specs/rendermode.spec.js` asserts it.

**The layout is static for every page, interactive ones included.** `RouteView` applies it outside
the page's island, and a render mode cannot be put on a layout at all — `@Body` is a
`RenderFragment` and Blazor cannot serialise one as a root component parameter. That is why the
chrome is written to work without a circuit (a checkbox drawer, `<details>` pickers, links to
`/culture/set` and `/season/set`) and why `<InteractiveShell />` carries the MudBlazor providers and
the revocation gate down into each interactive page. See `docs/known_issues/blazor-mudblazor.md`.

## Web (`src/FootballFormation.Web/`)
```
Program.cs                — Entry point: Serilog, EF Core, service registration, auto-migration
Components/
  App.razor               — Root component: no render mode, PWA meta tags, the Blazor.start schedule
  Routes.razor            — Router discovering pages from both Web and UI assemblies
  Pages/Login.razor       — The sign-in form (posts to /auth/login)
  Pages/Error.razor       — Unhandled-error page, localized
  Pages/NotFound.razor    — /not-found, localized
Security/
  RevalidatingUserAuthenticationStateProvider.cs
                          — Re-checks a circuit's principal on a timer and signs it out when the
                            account is gone; the circuit-side half of OnValidatePrincipal
KeepAlive/
  KeepAliveTracker.cs     — Thread-safe timestamp of the last real request
  KeepAlivePingService.cs — Pings the public /health endpoint for 30 minutes after the last visitor,
                            so Fly's unconfigurable ~5-minute idle sweep starts later (see
                            docs/deployment.md, "Cost control")
wwwroot/
  theme.css               — Semantic tokens, the muted-ink ramp and the gradients (see docs/theming.md)
  app.css                 — Global styles: MudBlazor overrides, badges, .action-btn, .stacked-table,
                            the responsive table layouts and the nav breakpoints
  fonts/                  — Self-hosted DM Sans (no render-blocking Google Fonts request)
  js/screenshot.js        — Renders the overview to a PNG via the bundled html2canvas, flattening
                            color-mix() in the clone first (see docs/known_issues/general.md)
  js/vendor/html2canvas.min.js
  manifest.webmanifest    — PWA manifest (installable on iOS/Android via Add to Home Screen)
  service-worker.js       — Caches assets the server marks `immutable`; never markup (no offline mode)
  icons/                  — GJS club logo as app icons: 180 (apple-touch) / 192 / 512 / 512-maskable
  js/pwa.js               — Service worker registration, and the reload once a rejoin has failed
  js/drag-drop-touch.js   — Touch → HTML5 drag event shim for the formation builder on phones
  js/season.js            — Writes the season picker's cookie; the server reads it off the request
```

## Deployment (repo root)
```
Dockerfile     — Multi-stage image build; sets APP_DATA_DIR=/data, listens on 8080
fly.toml       — Fly.io app "gjs-meiden" (ams), volume at /data, suspend when idle
docs/deployment.md — Full setup, DNS for gjs-meiden.nl, redeploy & backup commands
```

## Database
- SQLite at `%LOCALAPPDATA%\FootballFormation\footballformation.db` (or `$APP_DATA_DIR` when set — `/data` volume on Fly.io)
- Auto-migrates on startup
- `List<PlayerPosition>` stored as comma-separated ints
- `List<int>` (UnavailablePlayerIds, InjuredPlayerIds, GuestPlayerIds) stored as comma-separated values
- `Games.SeasonId` is a required FK with `ON DELETE RESTRICT`; the `AddSeasons` migration backfilled
  existing rows (see the EF Core conventions in [patterns](patterns/ef-core.md))
- `Trainings` is a plain table: `SeasonId` is a required FK with `ON DELETE RESTRICT`, like `Games`,
  and who was absent is a comma-separated `UnavailablePlayerIds` column rather than a join table
  (see [models](models/training.md))
- `SeasonSquadMembers` holds per-season squad membership, unique on `(SeasonId, PlayerId)`, cascading
  from both parents. The `AddSeasonSquads` migration backfilled it from the old `Players.IsGuest`
  column and then dropped that column — a parent-table rebuild, so verify with `PRAGMA foreign_key_check`
