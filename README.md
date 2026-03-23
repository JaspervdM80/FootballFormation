# ⚽ Football Formation Planner

A web app for planning football (soccer) formations, managing your youth team squad, and tracking match results.

## Features

- **Squad Management** — Add players with shirt numbers, preferred positions, and alternative positions
- **Game Planning** — Create matches with opponent, date, formation type, and split type (halves/quarters)
- **Formation Builder** — Drag-and-drop players onto a pitch with real-time position-fit feedback (5-tier color system)
- **Substitute Bench** — Drag-and-drop substitutes per period
- **Playing Time Overview** — See how many minutes each player is assigned across all periods
- **Copy to Next Period** — Quickly duplicate a lineup to the next half/quarter
- **Unavailable Players** — Mark players as unavailable per game
- **Match Results** — Record final scores, goal scorers, assists, and own goals
- **Formation Overview** — Shareable screenshot of all periods (via html2canvas)
- **Match Preferences** — Default game duration, split type, formation, and match day

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 10, Blazor Server (Interactive) |
| UI Library | MudBlazor 9.2.0 |
| Database | SQLite via EF Core |
| Logging | Serilog (file + console) |
| Screenshots | html2canvas (CDN) |

## Solution Structure

```
FootballFormation/
├── src/
│   ├── FootballFormation.Core/    # Domain models, EF Core DbContext, services
│   ├── FootballFormation.UI/      # Blazor components, pages, helpers, layout
│   └── FootballFormation.Web/     # Host project, Program.cs, wwwroot
├── docs/                          # Architecture & project documentation
│   ├── project_overview.md
│   ├── architecture.md
│   ├── models.md
│   ├── patterns.md
│   ├── ui_components.md
│   └── known_issues.md
└── FootballFormation.slnx
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run

```bash
cd src/FootballFormation.Web
dotnet run
```

The app will be available at `http://localhost:5228`.

The SQLite database is created automatically on first run at:
```
%LOCALAPPDATA%\FootballFormation\footballformation.db
```

Logs are written to:
```
%LOCALAPPDATA%\FootballFormation\logs\
```

## Design

Premium dark theme with gold/amber accent colors, top-bar navigation, and card-based layouts.

### Position Fit System

Players on the pitch are color-coded by how well they fit their assigned position:

| Color | Tier | Meaning |
|-------|------|---------|
| 🟢 Dark green | Preferred | Exact preferred position |
| 🟢 Light green | Natural fit | Same position family (e.g. W → LW) |
| 🔵 Blue | Alternative | Listed as alternative position |
| 🟠 Orange | Compatible | Alternative position's natural family |
| 🔴 Red | Out of position | No match at all |

## Architecture

- **Result pattern** — Service methods return `Result` or `Result<T>` instead of throwing exceptions
- **Code-behind** — Razor pages use `.razor.cs` partial classes
- **Auto-migration** — EF Core migrations run on startup
- **Split queries** — Configured globally to avoid N+1 issues with multiple includes

See the `docs/` folder for detailed architectural documentation.
