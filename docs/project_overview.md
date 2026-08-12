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
└── FootballFormation.Core.Tests — xUnit; one of the four checks a merge waits for (see testing.md)
```

UI is a separate RCL for future **MAUI Blazor Hybrid** reuse. Statistics and playing-time
calculation live in Core, not the UI, so they are testable and would come along by design.

## Key Features

**Squad and fixtures**
- Seasons (1 Jul – 30 Jun) grouping games, with an app-bar picker filtering the games list, the
  squad and all stats. The windows are gapless, so every date belongs to exactly one season
- Per-season squads: who is in the squad, and who is only a guest, is decided per season — a guest
  one year can be a full squad player the next. Squads copy forward from the previous season
- Player management (name, shirt number, preferred + alternative positions)
- Game management (opponent, date, home/away, match type, formation, split type, duration,
  unavailable players, guest call-ups)
- `/games` is two lists: fixtures still to play, then results, split on whether a final score exists
- Match preferences (default formation, split, duration, match day) **per season**, inherited from
  the previous one, and an auto-calculated next match date

**Match day**
- Formation builder with drag-and-drop onto a visual pitch, per-period lineups (halves or quarters)
- Playing time overview (% of game time per player, with position-fit colours)
- Live match mode (`/games/{id}/live`): a running clock, period transitions, timestamped
  substitutions and goals. The admin drives it; everyone else watches the same URL read-only, with
  the clock derived from one stored anchor rather than pushed each second
- Match results: final score, scorers, assists, own and opponent goals
- A shareable, screenshottable formation overview per period

**Reporting and admin**
- Season statistics (`/stats`): record, goals, form guide, top scorers, playing-time fairness
- Player statistics (`/players/{id}/stats`): minutes, utilisation, goals, assists, position share —
  minutes reconstructed from the substitutions when the match was run live
- Match comments, private by default and published deliberately; the filter is in the query
- Accounts and roles on `/users`, self-service password change on `/settings`
- Dutch by default with English available; installable as a PWA on iOS and Android
