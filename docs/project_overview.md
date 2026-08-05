# Football Formation Planner

Youth football team formation planner. Cookie-based auth with role claims (`UserRole`, currently
Admin only) and anonymous read-only access; accounts are managed on `/users`. Manages players, games, formations with drag-and-drop, and substitution planning across halves or quarters.

## Tech Stack
- **.NET 10**, Blazor Web App (Interactive Server rendering)
- **MudBlazor 9.7.0** — light club theme, driven by `ClubTheme` (see theming.md)
- **EF Core + SQLite** — DB at `%LOCALAPPDATA%\FootballFormation\footballformation.db`
- **Serilog** — console + rolling file logs at `%LOCALAPPDATA%\FootballFormation\logs\`
- **.slnx** solution format

## Solution Structure (`FootballFormation.slnx`)
```
src/
├── FootballFormation.Core   — Models, Data (DbContext), Reporting, Services, Result type
├── FootballFormation.UI     — Razor Class Library: pages, components, theming, layout
└── FootballFormation.Web    — Blazor host: Program.cs, App.razor, Routes.razor
tests/
└── FootballFormation.Core.Tests — xUnit; runs as a CI gate before deploy (see testing.md)
```

UI is a separate RCL for future **MAUI Blazor Hybrid** reuse. Statistics and playing-time
calculation live in Core, not the UI, so they are testable and would come along by design.

## Key Features
- Seasons (1 Jul – 30 Jun) grouping games, with an app-bar picker filtering the games list and all stats
- Per-season squads: who is in the squad, and who is only a guest, is decided per season — a guest
  one year can be a full squad player the next. Squads copy forward from the previous season.
- Player management (name, shirt number, preferred + alternative positions)
- Game management (opponent, date, home/away, formation, split type, duration, unavailable players, guest call-ups)
- Formation builder with drag-and-drop onto visual pitch
- Per-period lineups (halves or quarters)
- Playing time overview (shows % of game time per player with position-fit colors)
- Match preferences (default formation, split, duration, match day)
- Auto-calculated next match date
