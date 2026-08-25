# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

A Blazor Server app for planning youth football formations, managing per-season squads, running a
match live from the touchline, and reporting on minutes and results. It runs as a single Fly.io
container on **https://gjs-meiden.nl** with SQLite on one persistent volume, and it **auto-migrates
on boot** — that fact drives most of the caution elsewhere.

Reading is public; every change requires an admin sign-in, enforced at the service boundary as well
as in the markup. One thing a public read holds back: **playing-minute figures are admin-only** — a
visitor sees the counts and the position split, not the minutes or the utilisation behind them. The
UI is Dutch by default with English available.

## Commands

```bash
dotnet build -c Release        # what CI builds — warnings are errors here
dotnet test                    # xUnit v3, real SQLite
cd src/FootballFormation.Web && dotnet run     # http://localhost:5228
cd tests/ui && npm test        # Playwright, ~1 min (npm install first)
scripts/visual-check.sh        # screenshots every page, then measures every touch target
scripts/coverage.sh            # coverage of the lines this branch changed, 80% floor
```

`Directory.Build.props` sets `TreatWarningsAsErrors` in **Release only** — a Debug build that looks
clean can still fail CI.

## Layout

```
src/FootballFormation.Core/   Models, Data (EF Core), Reporting, Services, Result — no UI references
src/FootballFormation.UI/     Razor Class Library: pages, components, navigation, state, theming
src/FootballFormation.Web/    Host: Program.cs, App.razor, Routes.razor, wwwroot (CSS, JS, PWA)
tests/FootballFormation.Core.Tests/   xUnit v3
docs/                         Detailed reference and the incident record
scripts/                      visual-check.sh + its Playwright driver
```

Dependencies point one way: `Web → UI → Core`. **UI is a separate RCL for future MAUI Blazor Hybrid
reuse** — that is why report builders live in `Core/Reporting/` as pure static functions rather than
in the pages. Keep new domain and reporting logic out of the Razor project.

Solution file is `FootballFormation.slnx`. Package versions are centralized in
`Directory.Packages.props`; csproj files list names only.

## Where the rules live

`.claude/skills/` holds the working rules, one skill per area — services and `Result`, EF Core and
queries, migrations, the domain model, Razor pages and the circuit, the live match, styling, touch
and breakpoints, localization, testing, UI testing, build and release. **Load the skill for the area
you are touching before changing it**; each one ends with a pointer into `docs/` for the full story.
**`comment-rule` applies to every change**, whatever else it touches: default to no comments, write
one only for a non-obvious *why*, and never a paragraph. `comments` holds the few repository facts
that change how it lands here.

`docs/` is the detailed reference and the incident record. `docs/known_issues/` in particular is
not a changelog — it is a list of traps that already cost someone hours. Add to it when you find a
new one, and **update the doc for an area in the same change that alters its behaviour**.

## The six that must not wait for a skill to load

These fail silently or expensively, so they are here rather than only in a skill:

1. **Every write goes through `ServiceOperation.RunAdminAsync`.** Hiding a control behind
   `<AuthorizeView Roles="@AppRoles.Admin">` is enforcement in the render tree only. Reads stay
   open — the squad, fixtures and statistics are public.
2. **Never order or compare a `DateTime` inside a query.** SQLite stores dates as TEXT, so
   `ORDER BY Date` sorts the string the value happened to be written as. Materialise first, then use
   `GameOrdering` / `SeasonOrdering`.
3. **Take the clock from the injected `TimeProvider`**, never `DateTime.UtcNow` or `DateTime.Today` —
   in services and in pages alike. Tests drive `FakeTimeProvider`.
4. **Every user-facing string goes through `IStringLocalizer<Strings>` (`L`), with the English text
   as the key.** A missing `Strings.nl.resx` entry renders English with no warning, and resx keys are
   case-insensitive, so a lowercase service action phrase can collide with a button label.
5. **Most pages have no circuit, and the layout never has one.** `@rendermode InteractiveServer` is
   per page; `/stats`, `/stats/positions`, `/players/{id}/stats`, `/games/{id}/overview` and the
   login and error pages are plain server HTML. On those, `ISnackbar` reports into nothing and
   `OnAfterRenderAsync` never runs — use `PageNotice` + `<InlineNotice>`, and give JS work to a
   plain `onclick`. A page that *does* declare a render mode opens with `<InteractiveShell />`,
   because `MainLayout` renders statically even for it.
6. **Build Release before pushing.** Warnings are errors only there.

## Workflow

- Work on a feature branch. `main` takes pull requests only, and the merge button stays disabled
  until **Build and test**, **Coverage**, **Playwright** and **Visual check** are all green, the
  branch is up to date with `main`, and every review thread is resolved.
- **Merging to `main` releases**, straight onto the live volume, with no staging environment and
  nothing re-running on `main`. The four checks on the pull request are the last look — which is why
  a flaky browser job is re-run rather than merged past.
- Commit messages are plain imperative sentences describing the intent, not conventional-commit
  prefixes: *"Split the games list on the scoreline, not the calendar"*.
- `.editorconfig` codifies the existing style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase`
  private fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- Before opening a pull request, run the **`code-reviewer`** agent over the change.

## Environment notes

Claude Code web containers are rebuilt every session and ship no .NET SDK, so
`.claude/hooks/session-start.sh` installs `dotnet-sdk-10.0` from **Ubuntu's own archive** — it has to
be Ubuntu's, because the container's egress policy blocks `builds.dotnet.microsoft.com`. Chromium is
already at `/opt/pw-browsers/chromium`. `global.json` pins 10.0.110 with `rollForward: disable`; see
`docs/known_issues/blazor-components.md`, "the SDK the pin cannot reach", before changing any of it.

Locally the database and logs live under `%LOCALAPPDATA%\FootballFormation\`; set `APP_DATA_DIR` to
put them elsewhere (it is `/data` in the container).
