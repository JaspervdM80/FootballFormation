# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

A Blazor Server app for planning youth football formations, managing per-season squads, running a
match live from the touchline, and reporting on minutes and results. It runs as a single Fly.io
container on **https://gjs-meiden.nl** with SQLite on one persistent volume, and it **auto-migrates
on boot** — that fact drives most of the caution elsewhere.

One deployment serves every team of every club on it: a season and everything under it belongs to a
team, and every read is filtered to the team in scope. Reading is public; every change requires an
admin of that team, enforced at the service boundary as well as in the markup. Two things a public
read holds back: **playing-minute figures** are hidden in the render — a visitor sees the counts and
the position split, not the minutes or the utilisation behind them — and **the training register**
is refused at the service. The UI is Dutch by default with English available.

## Commands

```bash
dotnet build -c Release        # what CI builds — warnings are errors here
dotnet test                    # xUnit v3, real SQLite
cd src/FootballFormation.Web && dotnet run     # http://localhost:5228
cd tests/ui && npm test        # Playwright, ~4 min (npm ci first)
scripts/visual-check.sh        # screenshots every page, then measures every touch target
scripts/verify-matrix.sh       # every route × desktop/phone × visitor/admin: errors, overflow, admin-only controls
scripts/dev-db.sh              # replace the local database with a copy of the live one
scripts/test-db.sh             # load gjs-meiden-test.fly.dev with an anonymised copy of the live one
scripts/coverage.sh            # coverage of the lines this branch changed, 80% floor
```

`Directory.Build.props` sets `TreatWarningsAsErrors` in **Release only** — a Debug build that looks
clean can still fail CI.

## Layout

```
src/FootballFormation.Core/   Models, Data (EF Core), Reporting, Services, Security, Push, Result — no UI references
src/FootballFormation.UI/     Razor Class Library: pages, components, navigation, state, theming,
                              wwwroot (app.css, theme.css, the fonts and the components' own JS)
src/FootballFormation.Web/    Host: Program.cs, App.razor, Routes.razor, wwwroot (the PWA and icons)
tests/FootballFormation.Core.Tests/   xUnit v3
tests/ui/                     Playwright browser tests
docs/                         Detailed reference and the incident record
scripts/                      The shell entry points above and the Playwright drivers behind them
```

Dependencies point one way: `Web → UI → Core`. **UI is a separate RCL for future MAUI Blazor Hybrid
reuse** — that is why report builders live in `Core/Reporting/` as pure static functions rather than
in the pages. Keep new domain and reporting logic out of the Razor project, and put an asset a
component needs in `UI/wwwroot/` (served at `_content/FootballFormation.UI/`) rather than the host's.
The host's `wwwroot` is for what is a web concern alone: the service worker, `pwa.js`, `push.js` and
the icons.

Solution file is `FootballFormation.slnx`. Package versions are centralized in
`Directory.Packages.props`; csproj files list names only.

## Where the rules live

`.claude/skills/` holds the working rules, one skill per area — services and `Result`, EF Core and
queries, migrations, the domain model, Razor pages and the circuit, the live match, push and the PWA,
styling, touch and breakpoints, localization, testing, UI testing, verifying a UI change, build and
release, and `pre-pr` for getting a finished change ready. **Load the skill for the area you are
touching before changing it**; each one ends with a pointer into `docs/` for the full story. `.claude/hooks/skill-gate.sh` refuses the first edit in a mapped area until
its skill has been loaded that session, and lets a retry through.
**`comment-rule` applies to every change**, whatever else it touches: default to no comments, write
one only for a non-obvious *why*, and never a paragraph. The one in `.claude/skills/` is the rule
here — a plugin or marketplace skill of the same name is not this repository's, so don't load it.

**Skills state rules, not inventories.** No counts and no exhaustive lists the code can grow — "four
exist", "all eight columns" and hand-kept route lists are what went stale first; give the `grep` that
produces the list instead. `InstructionReferenceTests` fails CI on any file or code name quoted in
backticks here, in a skill or in an agent that no longer exists, and on any `@page` route missing
from the verify-ui table or the render-mode split.

`docs/` is the detailed reference and the incident record. `docs/known_issues/` in particular is
not a changelog — it is a list of traps that already cost someone hours. Add to it when you find a
new one, and **update the doc for an area in the same change that alters its behaviour**.

## The seven that must not wait for a skill to load

These fail silently or expensively, so they are here rather than only in a skill:

1. **Every write goes through `ServiceOperation.RunAdminAsync`** (`RunApplicationAdminAsync` for
   clubs and teams). Hiding a control behind `<AuthorizeView Roles="@AppRoles.Admin">` is
   enforcement in the render tree only. Reads stay open — the squad, fixtures and statistics are
   public. The one write that is not an admin's is `PushSubscriptionService`: anonymous, so every
   field is validated and the endpoint is rate-limited.
2. **Never order or compare a `DateTime` inside a query.** SQLite stores dates as TEXT, so
   `ORDER BY Date` sorts the string the value happened to be written as. Materialise first, then use
   `GameOrdering` / `SeasonOrdering`.
3. **Take the clock from the injected `TimeProvider`**, never `DateTime.UtcNow` or `DateTime.Today` —
   in services and in pages alike. Tests drive `FakeTimeProvider`. `BannedSymbols.txt` makes the
   wall clock an RS0030 error in a Release build of `src/`.
4. **Every user-facing string goes through `IStringLocalizer<Strings>` (`L`), with the English text
   as the key.** The app renders a missing `Strings.nl.resx` entry as English without warning;
   `LocalizationTests` catches a literal `L["..."]` key but not one built at runtime. Resx keys are
   case-insensitive, so a lowercase service action phrase can collide with a button label.
5. **Most pages have no circuit, and the layout never has one.** `@rendermode InteractiveServer` is
   per page; `/stats`, `/stats/positions`, `/players/{id}/stats`, `/games/{id}/overview`,
   `/games/duties`, `/styleguide` and the login and error pages are plain server HTML, and so are
   `/games` and `/players` for a visitor — an admin gets their board as an island. On those, `ISnackbar`
   reports into nothing and `OnAfterRenderAsync` never runs — use `PageNotice` + `<InlineNotice>`, and give JS work to a
   plain `onclick`. A page that *does* declare a render mode opens with `<InteractiveShell />`,
   because `MainLayout` renders statically even for it.
6. **Build Release before pushing.** Warnings are errors only there.
7. **The team scope is applied for you — don't step around it.** Services take
   `IDbContextFactory<AppDbContext>`, which stamps the team in scope, and `AppDbContext`'s query
   filters do the rest. A new season-scoped entity needs a `TeamId` and a filter; child rows (goals,
   injuries, comments) carry none, so a write reaching one by its own id gates on `GameInScopeAsync`.
   Get either wrong and another team's rows come back looking completely normal.

## Workflow

- **Start from the latest `main` before writing any code**: `git fetch origin main`, then branch
  from `origin/main` (`git checkout -b feature/… origin/main`). A checkout or worktree handed to you
  can be several merges behind, and the change you are asked about may already be on `main`. If you
  already have commits, rebase them onto `origin/main` instead.
- Work on a branch named **`feature/…`**, **`bug/…`** or **`ci/…`** after what the change is — a new
  or changed behaviour, a fix, or the build, workflows and tooling (e.g. `feature/two-tap-goals`).
  Nothing else; a branch handed to you under another name is renamed before it is pushed.
- `main` takes pull requests only, and the merge button stays disabled
  until **Build and test**, **Coverage**, **Playwright** and **Visual check** are all green, the
  branch is up to date with `main`, and every review thread is resolved.
- **Merging to `main` releases**, straight onto the live volume, with no staging environment and
  nothing re-running on `main`. The four checks on the pull request are the last look — which is why
  a flaky browser job is re-run rather than merged past. `.claude/settings.json` denies pushing to
  `main`, force-pushing and `gh pr merge`; those stay a person's call. Every `fly`/`flyctl`
  command is in `ask`, so it always waits for approval and never runs on its own.
- Commit messages are plain imperative sentences describing the intent, not conventional-commit
  prefixes: *"Split the games list on the scoreline, not the calendar"*.
- `.editorconfig` codifies the existing style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase`
  private fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- Before opening a pull request, work through the **`pre-pr`** skill — it ends with the
  **`code-reviewer`** agent over the change. Adding the `claude-review` label to a pull request runs
  the same agent in CI and posts its report as a comment.
- Hooks hold some of this for you: the session start says how far behind `origin/main` the checkout
  is, a `git push` waits for a green Release build of the working tree, and a resx edit that collides
  with an existing key is reported at once. Bare `git stash` and `git stash pop` are denied, because
  worktrees share one stash.

## Environment notes

Claude Code web containers are rebuilt every session and ship no .NET SDK, so
`.claude/hooks/session-start.sh` installs `dotnet-sdk-10.0` from **Ubuntu's own archive** — it has to
be Ubuntu's, because the container's egress policy blocks `builds.dotnet.microsoft.com`. Chromium is
already at `/opt/pw-browsers/chromium`. `global.json` pins 10.0.111 with `rollForward: latestPatch`; see
`docs/known_issues/blazor-components.md`, "the SDK the pin cannot reach", before changing any of it.

Locally the database and logs live under `%LOCALAPPDATA%\FootballFormation\`; set `APP_DATA_DIR` to
put them elsewhere (it is `/data` in the container).
