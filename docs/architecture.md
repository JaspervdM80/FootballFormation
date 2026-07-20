# Architecture & File Map

## Core (`src/FootballFormation.Core/`)
```
Models/
  Player.cs              — Player entity (FirstName, Surname, ShirtNumber, PreferredPosition, AlternativePositions)
  Position.cs            — PlayerPosition enum (32 values), PositionCategory enum, extensions
  FormationType.cs       — FormationType enum (12 formations), DisplayName(), DefaultPositions()
  Game.cs                — Game entity, GameSplitType enum
  GamePeriod.cs          — GamePeriod entity, PeriodType enum, PeriodTypeExtensions
  GamePlayerPosition.cs  — Links player to position in a period (IsSubstitute flag)
  MatchPreferences.cs    — Singleton preferences (duration, split, formation, match day)
Data/
  AppDbContext.cs         — EF Core context, value converters for List<PlayerPosition> and List<int>
Services/
  ServiceOperation.cs     — Shared try/catch + error logging wrapper for all service methods
  PlayerService.cs        — CRUD, returns Result<T>
  GameService.cs          — CRUD + SavePeriodLineupAsync, returns Result<T>
  MatchPreferencesService.cs — Get/Save prefs, GetNextMatchDateAsync
Result.cs                — Result and Result<T> base types
```

## UI (`src/FootballFormation.UI/`) — Razor Class Library
```
Pages/
  Players.razor(.cs)          — /players — Squad management with add/edit/delete dialogs
  PlayerDialog.razor(.cs)     — Dialog: first name, surname, shirt #, positions
  Games.razor(.cs)            — /games — Game list with formation builder link
  GameDialog.razor(.cs)       — Dialog: opponent, date, formation, split, duration, unavailable players
  FormationBuilder.razor(.cs) — /games/{id}/formation — Pitch + player list + subs + playing time overview
  Settings.razor(.cs)         — /settings — Match preferences
  Home.razor                  — / — Landing page
Components/
  PitchView.razor(.cs)(.css)        — Visual pitch with position circles, drag-drop, fit colors
  PlayerList.razor(.cs)(.css)       — Draggable player cards (HTML5 drag API)
  SubstituteBench.razor(.cs)(.css)  — Substitute drop zone with remove buttons
  ConfirmDialog.razor(.cs)          — Reusable yes/no confirmation dialog
Helpers/
  PitchPositionHelper.cs      — Maps PlayerPosition → (left%, top%) coordinates
  PositionFitHelper.cs        — 5-tier position fit: Preferred, NaturalFit, Alternative, Compatible, OutOfPosition
  UiFeedback.cs               — Snackbar.Report()/ReportFailure() over Result, shared LockedDialog options
  DialogPrompts.cs            — DialogService.ConfirmDeleteAsync() wrapper over ConfirmDialog
  PlayingTimeReport.cs        — Builds the playing-time table (PlayingTimeRow, PeriodDetail, PeriodPlayStatus)
  LineupDragState.cs          — In-flight drag on the formation builder
Layout/
  MainLayout.razor(.cs)       — MudBlazor layout with dark theme, drawer, providers
  NavMenu.razor               — Navigation: Home, Players, Games, Settings
```

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
