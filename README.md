# ⚽ Football Formation Planner

A web app for planning football (soccer) formations, managing your youth team squad, and tracking match results.

## Features

### Squad and games
- **Seasons** — Everything is scoped to a season (1 Jul – 30 Jun). A picker in the app bar filters
  the squad, the games and the statistics; season windows are gapless, so every date belongs to
  exactly one
- **Season squads** — Squad membership is per season, with a guest flag, and can be copied forward
  from last season in one action
- **Squad Management** — Add players with shirt numbers, preferred positions, and alternative positions
- **Game Planning** — Create matches with opponent, date, formation type, and split type (halves/quarters)
- **Unavailable Players** — Mark players as unavailable per game

### Match day
- **Formation Builder** — Drag-and-drop players onto a pitch with real-time position-fit feedback (5-tier color system)
- **Substitute Bench** — Drag-and-drop substitutes per period
- **Playing Time Overview** — See how many minutes each player is assigned across all periods
- **Copy to Next Period** — Quickly duplicate a lineup to the next half/quarter
- **Live match mode** — A sideline screen with a running clock, period transitions, timestamped
  substitutions and goals as they happen. The admin drives it; everyone else watches the same
  clock, derived from one stored anchor rather than pushed each second
- **Match Results** — Record final scores, goal scorers, assists, and own goals
- **Formation Overview** — Shareable screenshot of all periods (via html2canvas)

### Reporting and admin
- **Season statistics** — Record, goals, form guide, top scorers, and a playing-time fairness view
- **Player statistics** — Per-player minutes, utilisation, goals, assists and position share.
  Minutes are reconstructed from the substitutions when a match was tracked live
- **Match Preferences** — Default game duration, split type, formation and match day, **per season**
  and inherited from the previous one
- **Accounts and roles** — Cookie authentication with an `Admin` role. Reading is public; every
  change requires signing in, enforced both in the UI and at the service boundary. Account
  management lives on `/users`, self-service password change on `/settings`
- **Dutch by default**, with English available from the language switcher. Every user-facing
  string goes through `IStringLocalizer`; the English text is the resource key

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 10, Blazor Server (Interactive) |
| UI Library | MudBlazor 9.7.0 |
| Database | SQLite via EF Core |
| Auth | Cookie authentication, PBKDF2 via `PasswordHasher<T>`, security stamps |
| Localization | `IStringLocalizer` — Dutch default, English fallback |
| Logging | Serilog (file + console) |
| Screenshots | html2canvas (bundled, not CDN) |
| Tests | xUnit v3 against real SQLite |

## Solution Structure

```
FootballFormation/
├── src/
│   ├── FootballFormation.Core/    # Domain models, EF Core DbContext, services, reports
│   ├── FootballFormation.UI/      # Blazor components, pages, helpers, layout
│   └── FootballFormation.Web/     # Host project, Program.cs, wwwroot
├── tests/
│   └── FootballFormation.Core.Tests/   # Service and domain tests (gate the merge)
├── docs/                          # Architecture & project documentation
│   ├── project_overview.md
│   ├── architecture.md
│   ├── models.md
│   ├── patterns.md
│   ├── ui_components.md
│   ├── theming.md
│   ├── testing.md
│   ├── deployment.md
│   └── known_issues.md
└── FootballFormation.slnx
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — `global.json` pins the exact build, so
  install the version it names (`rollForward` is `disable`, and `dotnet` will say so if it is
  missing)

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

### First sign-in

An install with no accounts seeds `admin` / `admin` and flags it: the account can sign in and
nothing else until the password is changed. Every route redirects it to `/settings`, and the
services treat it as unauthorized, so the default credentials are never a working admin login.
Changing the password releases the gate and signs the session out, so you sign back in with the
new one.

In Development only, `GET /dev/login` signs in as admin without credentials — it is mapped only
outside Production *and* only for loopback callers.

### Tests and CI

```bash
dotnet test
```

`.github/workflows/ci.yml` runs `dotnet build -c Release` and `dotnet test` on every pull request,
alongside the coverage and browser jobs below. Note that `Directory.Build.props` sets
`TreatWarningsAsErrors` in **Release only** — a warning that builds fine locally will fail CI. Build
Release before pushing. The exception is `MSB3568` (a duplicate resource name, which quietly changes
what the app says), an error in every configuration.

A ruleset makes those checks binding: `main` takes pull requests only, the branch has to be up to
date with `main`, and the merge button stays disabled until all four — `🔨 Build and test`,
`📊 Coverage`, `🎭 Playwright` and `📸 Visual check` — are green. Merging is what releases: nothing
runs on `main` afterwards, so those four are the last word on what reaches production. The ruleset
lives in `.github/rulesets/main-every-check-green.json` and has to be imported into the repository's
settings once — see [docs/deployment.md](docs/deployment.md#only-a-green-build-can-be-merged).

### UI tests

```bash
cd tests/ui && npm install && npm test
```

Playwright, driving the real app in a browser against a database that exists only for the run: the
public/admin split, the squad and match dialogs, the full match-day journey from dragging a lineup
to blowing the final whistle, both languages, and the phone layout. About a minute, 34 tests. Runs
on every pull request as the `🎭 Playwright` job in `.github/workflows/ci.yml`, against the app that
workflow published — one of the four checks the merge waits for. See [docs/testing.md](docs/testing.md#ui-tests-testsui).

### Visual checks

```bash
scripts/visual-check.sh
```

Boots the app against a throwaway database, seeds a small squad through the real dialogs, and
screenshots every page into `artifacts/visual/`. It fails if the browser logged an error, which is
where a Blazor render failure surfaces. Nothing else in the repo checks that a page renders at all
— the tests cover the domain rules.

It then measures rather than looks: the match dialog and its date picker are reopened at 320, 360
and landscape phone sizes and every touch target is checked for the 44px minimum and for dead space
between it and its neighbours. That is the only thing holding the touch fixes in
[docs/known_issues.md](docs/known_issues.md) in place, so it runs on every pull request too — the
`📸 Visual check` job in `ci.yml`, blocking like the one beside it, and it uploads its screenshots
either way.
See [docs/testing.md](docs/testing.md).

### Claude Code on the web

Those containers ship no .NET SDK and are rebuilt every session, so
`.claude/hooks/session-start.sh` installs it (and warms the NuGet cache) on session start —
without it an agent can read this code but cannot build it, test it, or look at a page. It takes
the SDK from Ubuntu's own archive because the egress policy blocks Microsoft's installer host, and
checks that what apt handed over satisfies `global.json` — CI installs the version that same file
names, so a green check and a web session compile the same code.

## Deployment

The app deploys to Fly.io (Amsterdam) behind **https://gjs-meiden.nl** — a single
container with a persistent volume for the SQLite database, scaled to zero when idle.
See [docs/deployment.md](docs/deployment.md) for setup, DNS records, and the
redeploy command (`fly deploy`).

## Install on iPhone / Android (PWA)

The app is an installable Progressive Web App. Open **https://gjs-meiden.nl** on the phone
(HTTPS is required for installation and the service worker; only `localhost` is exempt), then:

- **iPhone (Safari)**: Share → *Add to Home Screen*
- **Android (Chrome)**: ⋮ menu → *Add to Home screen* (or the automatic install prompt)

It launches full-screen with its own icon like a native app. Touch drag-and-drop on the
formation builder is supported via a built-in shim (`js/drag-drop-touch.js`).

> **Note**: this is a Blazor Server app — it needs a live connection to the server.
> There is deliberately no offline mode; the service worker exists only to satisfy
> installability requirements.

## Design

A light theme in the club's red and green, sampled from the GJS crest, with card-based layouts.
Navigation is a top bar on desktop and a drawer below 700px. The whole palette comes from one
record — `ClubTheme` — which emits the CSS custom properties *and* builds MudBlazor's palette, so
re-skinning for another club is one file. See [docs/theming.md](docs/theming.md).

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

- **Result pattern** — Service methods return `Result` or `Result<T>` instead of throwing exceptions.
  The English message is also the resource key, so failures are translatable
- **Code-behind** — Razor pages use `.razor.cs` partial classes
- **DbContext factory, not a scoped context** — a Blazor Server circuit outlives a request, so each
  service operation opens and disposes its own short-lived context
- **Authorization at the service boundary** — every write goes through `RunAdminAsync`, so hiding a
  control in the UI is the first line of defence, not the only one
- **Auto-migration** — EF Core migrations run on startup
- **Split queries** — Configured globally to avoid N+1 issues with multiple includes

See the `docs/` folder for detailed architectural documentation.
