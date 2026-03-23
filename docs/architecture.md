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
Layout/
  MainLayout.razor(.cs)       — MudBlazor layout with dark theme, drawer, providers
  NavMenu.razor               — Navigation: Home, Players, Games, Settings
```

## Web (`src/FootballFormation.Web/`)
```
Program.cs                — Entry point: Serilog, EF Core, service registration, auto-migration
Components/
  App.razor               — Root component (InteractiveServer on Routes + HeadOutlet)
  Routes.razor             — Router discovering pages from both Web and UI assemblies
```

## Database
- SQLite at `%LOCALAPPDATA%\FootballFormation\footballformation.db`
- Auto-migrates on startup
- `List<PlayerPosition>` stored as comma-separated ints
- `List<int>` (UnavailablePlayerIds) stored as comma-separated values
