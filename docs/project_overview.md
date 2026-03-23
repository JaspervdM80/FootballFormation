# Football Formation Planner

Youth football team formation planner. Single user, no auth. Manages players, games, formations with drag-and-drop, and substitution planning across halves or quarters.

## Tech Stack
- **.NET 10**, Blazor Web App (Interactive Server rendering)
- **MudBlazor 9.2.0** — dark theme, green primary (#66BB6A)
- **EF Core + SQLite** — DB at `%LOCALAPPDATA%\FootballFormation\footballformation.db`
- **Serilog** — console + rolling file logs at `%LOCALAPPDATA%\FootballFormation\logs\`
- **.slnx** solution format

## Solution Structure (`FootballFormation.slnx`)
```
src/
├── FootballFormation.Core   — Models, Data (DbContext), Services, Result type
├── FootballFormation.UI     — Razor Class Library: pages, components, helpers, layout
└── FootballFormation.Web    — Blazor host: Program.cs, App.razor, Routes.razor
```

UI is a separate RCL for future **MAUI Blazor Hybrid** reuse.

## Key Features
- Player management (name, shirt number, preferred + alternative positions)
- Game management (opponent, date, formation, split type, duration, unavailable players)
- Formation builder with drag-and-drop onto visual pitch
- Per-period lineups (halves or quarters)
- Playing time overview (shows % of game time per player with position-fit colors)
- Match preferences (default formation, split, duration, match day)
- Auto-calculated next match date
