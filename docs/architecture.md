# Architecture & File Map

## Core (`src/FootballFormation.Core/`)
```
Models/
  Player.cs              — Player entity (FirstName, Surname, ShirtNumber, PreferredPosition, AlternativePositions)
  Position.cs            — PlayerPosition enum (32 values), PositionCategory enum, extensions
  FormationType.cs       — FormationType enum (12 formations), DisplayName(), DefaultPositions()
  Season.cs              — Season entity (1 Jul – 30 Jun windows), Contains/ShortName/CreateFor helpers
  Game.cs                — Game entity (incl. SeasonId), GameSplitType enum
  GamePeriod.cs          — GamePeriod entity, PeriodType enum, PeriodTypeExtensions
  GamePlayerPosition.cs  — Links player to position in a period (IsSubstitute flag)
  MatchPreferences.cs    — Singleton preferences (duration, split, formation, match day)
Data/
  AppDbContext.cs         — EF Core context, value converters for List<PlayerPosition> and List<int>
Services/
  ServiceOperation.cs     — Shared try/catch + error logging wrapper for all service methods
  PlayerService.cs        — CRUD, returns Result<T>
  SeasonService.cs        — CRUD + GetCurrent/SetCurrent/GetOrCreateForDate/EnsureCurrentSeason
  GameService.cs          — CRUD + SavePeriodLineupAsync, optional seasonId filter, returns Result<T>
  MatchPreferencesService.cs — Get/Save prefs, GetNextMatchDateAsync
Result.cs                — Result and Result<T> base types
```

## UI (`src/FootballFormation.UI/`) — Razor Class Library
```
Pages/
  Players.razor(.cs)          — /players — Squad management with add/edit/delete dialogs
  PlayerDialog.razor(.cs)     — Dialog: first name, surname, shirt #, positions
  Games.razor(.cs)            — /games — Game list with formation builder link
  GameDialog.razor(.cs)       — Dialog: opponent, date, season, formation, split, duration, unavailable players
  FormationBuilder.razor(.cs) — /games/{id}/formation — Pitch + player list + subs + playing time overview
  SeasonStats.razor(.cs)(.css)— /stats — Season dashboard: record, goals, form, scorers, playing time
  PlayerStats.razor(.cs)(.css)— /players/{id}/stats — Per-player figures for the selected season
  MatchResult.razor(.cs)(.css)— /games/{id}/result — Score and goal entry
  SeasonDialog.razor(.cs)     — Dialog: season name, start date, end date
  Settings.razor(.cs)         — /settings — Match preferences, password, season management
  Home.razor                  — / — Landing page
Components/
  PitchView.razor(.cs)(.css)        — Visual pitch with position circles, drag-drop, fit colors
  PlayerList.razor(.cs)(.css)       — Draggable player cards (HTML5 drag API)
  SubstituteBench.razor(.cs)(.css)  — Substitute drop zone with remove buttons
  SeasonPicker.razor(.cs)           — Global season filter; rendered in both the app bar and the drawer
  ConfirmDialog.razor(.cs)          — Reusable yes/no confirmation dialog
State/
  SeasonState.cs              — Scoped: the selected season, shared by the layout and the pages
Helpers/
  PitchPositionHelper.cs      — Maps PlayerPosition → (left%, top%) coordinates
  PositionFitHelper.cs        — 5-tier position fit: Preferred, NaturalFit, Alternative, Compatible, OutOfPosition
  UiFeedback.cs               — Snackbar.Report()/ReportFailure() over Result, shared LockedDialog options
  DialogPrompts.cs            — DialogService.ConfirmDeleteAsync() wrapper over ConfirmDialog
  PlayingTimeReport.cs        — Builds the playing-time table (PlayingTimeRow, PeriodDetail, PeriodPlayStatus)
  SeasonStatsReport.cs        — Team totals + form for /stats (SeasonStats, GameResult)
  PlayerStatsReport.cs        — Per-player aggregates (PlayerStats, PositionStat, PlayerGameStat)
  LineupDragState.cs          — In-flight drag on the formation builder
Layout/
  MainLayout.razor(.cs)       — MudBlazor layout, club light theme, app-bar nav + drawer, providers.
                                Nav entries and the season picker must be edited in BOTH places.
  NavMenu.razor               — Unused legacy; MainLayout inlines its own MudNavMenu
```

Report builders in `Helpers/` are pure static functions taking the game list as a parameter, which
is why season filtering happens at the call site and their signatures never changed.

## Web (`src/FootballFormation.Web/`)
```
Program.cs                — Entry point: Serilog, EF Core, service registration, auto-migration
Components/
  App.razor               — Root component (InteractiveServer on Routes + HeadOutlet), PWA meta tags
  Routes.razor             — Router discovering pages from both Web and UI assemblies
wwwroot/
  manifest.webmanifest    — PWA manifest (installable on iOS/Android via Add to Home Screen)
  service-worker.js       — Pass-through SW required for Android installability (no offline caching)
  icons/                  — GJS club logo as app icons: 180 (apple-touch) / 192 / 512 / 512-maskable
  js/pwa.js               — Service worker registration
  js/drag-drop-touch.js   — Touch → HTML5 drag event shim for the formation builder on phones
```

## Deployment (repo root)
```
Dockerfile     — Multi-stage image build; sets APP_DATA_DIR=/data, listens on 8080
fly.toml       — Fly.io app "gjs-meiden" (ams), volume at /data, scale-to-zero
docs/deployment.md — Full setup, DNS for gjs-meiden.nl, redeploy & backup commands
```

## Database
- SQLite at `%LOCALAPPDATA%\FootballFormation\footballformation.db` (or `$APP_DATA_DIR` when set — `/data` volume on Fly.io)
- Auto-migrates on startup
- `List<PlayerPosition>` stored as comma-separated ints
- `List<int>` (UnavailablePlayerIds) stored as comma-separated values
- `Games.SeasonId` is a required FK with `ON DELETE RESTRICT`; the `AddSeasons` migration backfills
  existing rows (see the EF Core conventions in [patterns.md](patterns.md))
