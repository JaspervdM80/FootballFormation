# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

A Blazor Server app for planning youth football formations, managing per-season squads, running a
match live from the touchline, and reporting on minutes and results. It runs as a single Fly.io
container on **https://gjs-meiden.nl** with SQLite on one persistent volume, and it **auto-migrates
on boot** — that fact drives most of the caution below.

Reading is public; every change requires an admin sign-in, enforced at the service boundary as well
as in the markup. The UI is Dutch by default with English available.

## Commands

```bash
dotnet build -c Release        # what CI builds — warnings are errors here (see below)
dotnet test                    # 389 tests, xUnit v3, real SQLite
cd src/FootballFormation.Web && dotnet run     # http://localhost:5228
cd tests/ui && npm test        # 37 Playwright tests in a browser, ~1 min (npm install first)
scripts/visual-check.sh        # screenshots every page, then measures every touch target
scripts/coverage.sh            # coverage of the lines this branch changed, 80% floor
```

`Directory.Build.props` sets `TreatWarningsAsErrors` in **Release only**. A Debug build that looks
clean can still fail CI — **build Release before pushing**. The one exception is `MSB3568`
(duplicate resource name), promoted to an error in every configuration via
`MSBuildWarningsAsErrors` because it silently changes what the app says.

EF Core migrations are run from `Core` alone; `DesignTimeDbContextFactory` means no
`--startup-project` is needed:

```bash
dotnet ef migrations add <Name> --project src/FootballFormation.Core
dotnet ef database update    --project src/FootballFormation.Core
```

## Layout

```
src/FootballFormation.Core/   Models, Data (EF Core), Reporting, Services, Result — no UI references
src/FootballFormation.UI/     Razor Class Library: pages, components, navigation, state, theming
src/FootballFormation.Web/    Host: Program.cs, App.razor, Routes.razor, wwwroot (CSS, JS, PWA)
tests/FootballFormation.Core.Tests/   xUnit v3 — the CI gate the deploy depends on
docs/                         The detailed documentation (see the index below)
scripts/                      visual-check.sh + its Playwright driver
```

Dependencies point one way: `Web → UI → Core`. **UI is a separate RCL for future MAUI Blazor Hybrid
reuse** — that is why report builders live in `Core/Reporting/` as pure static functions rather than
in the pages. Keep new domain and reporting logic out of the Razor project.

Solution file is `FootballFormation.slnx` (the new XML format). Package versions are centralized in
`Directory.Packages.props`; csproj files list names only.

## Read the right doc first

The `docs/` folder is maintained, detailed and current — it is the primary reference, and this file
only routes to it. Before changing an area, read its doc:

| Doing this | Read |
|---|---|
| Anything at all, first time | [docs/project_overview.md](docs/project_overview.md) |
| Finding a file or a type | [docs/architecture.md](docs/architecture.md) — annotated file map of all three projects |
| Touching an entity or the schema | [docs/models.md](docs/models.md) |
| Writing a service, a migration, UI state, or navigation | [docs/patterns.md](docs/patterns.md) |
| Touching a page, dialog, the pitch, or the live screen | [docs/ui_components.md](docs/ui_components.md) |
| Touching colors, CSS, or breakpoints | [docs/theming.md](docs/theming.md) |
| Adding or changing tests | [docs/testing.md](docs/testing.md) |
| Anything that ships or migrates | [docs/deployment.md](docs/deployment.md) |
| **Before debugging anything that feels weird** | [docs/known_issues.md](docs/known_issues.md) |

`docs/known_issues.md` is not a changelog — it is a list of traps that already cost someone hours
(MudBlazor 9.x quirks, SQLite date sorting, touch drag-and-drop, CSS scoping). Check it before
diagnosing a strange bug, and add to it when you find a new one.

## Conventions that are not optional

**Services return `Result` / `Result<T>`, never throw and never write their own try/catch.**
Wrap the body in `ServiceOperation.RunAsync` for a read or `RunAdminAsync` for a write — the admin
check is a property of the shape, not something each method remembers. Failure messages are
templates, and **the English template is the resource key**: `Result.Failure("Season {0} still has
{1} games", name, count)`, never an interpolated string. Reading `Result<T>.Value` on a failure
throws by design.

**Every write goes through `RunAdminAsync`.** Hiding a control behind `<AuthorizeView
Roles="@AppRoles.Admin">` is the first line of defence, not the only one. Reads stay open — the
squad, fixtures and statistics are public. The single exception is
`GameService.GetCommentsAsync`, which re-confirms its `includePrivate` argument against
`ICurrentUser` rather than trusting the caller.

**Each service operation opens its own short-lived `AppDbContext`** from the injected
`IDbContextFactory`. A Blazor Server circuit outlives a request, so a scoped context would be shared
by every component on the page and two concurrent queries throw.

**Never order or compare a `DateTime` inside a query.** SQLite stores dates as TEXT, so `ORDER BY
Date` sorts the string the value happened to be written as. Materialise first, then use
`GameOrdering` / `SeasonOrdering`. `LiveMatchService`'s same-day `Date >= today && Date < tomorrow`
is the one comparison left in SQL, kept there so the home page does not load the games table whole;
everything else compares dates in memory. `DateInSqlInterceptor` enforces this across the whole test
suite, so a query that breaks the rule fails a test rather than sorting almost-right — the exception
opts out by name with `.TagWith(QueryTags.ComparesDatesInSql)`.

**Take the clock from the injected `TimeProvider`**, never `DateTime.UtcNow` or `DateTime.Today` —
in services and in pages alike. Tests drive `FakeTimeProvider`.

**No interfaces for services.** They are injected as concrete types; don't add `IPlayerService`
unless a second implementation actually exists. `ICurrentUser` is the deliberate exception — it is
the seam the write guard needs.

**Domain logic lives on the model**, not in a service or a page (`Game.PeriodCount`,
`Game.IsInRoster`, `Game.CountOurGoals`). When a rule needs data the entity doesn't own, pass a
value object in (`SeasonSquad`) rather than relying on a navigation being `.Include`d.

**Every user-facing string goes through `IStringLocalizer<Strings>` (`L`)**, with the English text
as the key. Only `Strings.nl.resx` exists; a missing key renders as English. Resx keys are
case-insensitive, so watch for collisions with the lowercase service action phrases.

**Razor pages use `.razor.cs` code-behind partial classes.** A page's base class goes in the
`.razor` as `@inherits SeasonAwarePage`, never on the partial class (CS0263).

**Build URLs from `AppRoutes`**, never an interpolated literal (`@page` directives are the one
exception — Razor needs a constant). A page's display name lives once, in `AppNav.PageNameKey`.
Redirect away from a failed page with `Trail.Redirect(...)`, not `NavigateTo`.

**Every page opens with `<PageHeader>`** — don't hand-roll a header row.

**CSS scoping has a silent failure mode.** A class in `Foo.razor.css` compiles to
`.cls[b-<fooHash>]` and will not match identical markup on another page — no warning, it just
renders unstyled. Anything used by more than one page, or targeting a MudBlazor component's own
root element, goes in `Web/wwwroot/app.css`. Colors come from the tokens in `theme.css` and
`ClubTheme.cs`; muted text uses the named ink ramp (`--ink-muted` / `-subtle` / `-faint`), never an
ad-hoc `color-mix` percentage.

**Update the doc in the same change.** Every recent commit that changed behaviour also touched the
doc that describes it. A change to the schema updates `models.md`, a UI change updates
`ui_components.md`, a bug whose cause was non-obvious gets a `known_issues.md` entry.

## Migrations: the part that can lose data

The app migrates itself unattended against the live volume on the next deploy, so a bad migration is
a bad production database. `Program.cs` takes a pre-migration snapshot and refuses to migrate if
that fails, but the snapshot is the last resort, not the plan.

- **Read the generated `Up()` and reorder it.** The scaffolder put a `DropColumn` *before* the
  backfill that had to read it (`AddSeasonSquads`), which would have destroyed the source data.
- Order operations `AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey`.
  Backfills belong in the migration, not in startup code.
- **A SQLite migration is not atomic.** EF emits `PRAGMA foreign_keys = 0` for a table rebuild, and
  that cannot run in a transaction — a half-applied backfill boots silently clean. Verify with
  `PRAGMA foreign_key_check` and a `WHERE <fk> = 0` count.
- **Rehearse a destructive migration on a copy** of the database with `APP_DATA_DIR` pointed at it.

## Testing

`dotnet test` from the repo root. Tests run against **real SQLite** (`Filename=:memory:` held open
by `ServiceTestBase`) — not the in-memory provider, which enforces none of the foreign keys, unique
indexes, cascades or CSV value converters these services lean on. Inherit `ServiceTestBase` for
anything touching the database and arrange with `TestData`; pure domain and `Core/Reporting` logic
needs no fixture.

Test names are sentences:
`A_match_in_progress_is_never_complete_however_many_goals_are_logged`.

There are **no component tests** (no bUnit). The UI is checked by driving the real app in a real
browser, in two places, both against a throwaway database:

- **`cd tests/ui && npm test`** — Playwright, ~37 tests, about a minute. Runs on every pull request
  as a job in `ci.yml`, advisory rather than the merge gate. Behaviour: the public/admin
  split, the squad and match dialogs, the whole match-day journey from dragging a lineup to blowing
  the final whistle, both languages, and the phone layout. Read
  [docs/testing.md](docs/testing.md#ui-tests-testsui) **before adding one** — a Blazor Server page
  renders twice and the prerender is fully clickable and completely inert, so `goto()` waits for
  Blazor's `_bl_*` attributes and `clickFor()` clicks for an outcome. There are no fixed sleeps in
  that directory; do not introduce one.
- **`scripts/visual-check.sh`** — the `Visual check` job in `ci.yml`, also advisory. Screenshots every
  page, then measures every touch target on three phone viewports against the 44px floor. The only check that a page renders at all, and the only
  thing holding the Touch / PWA fixes in `known_issues.md` in place.

(`docs/testing.md` also references a `verify-ui` skill for the manual desktop/mobile × anonymous/
admin matrix; it is not part of this repository.)

## Workflow

- Work on a feature branch. `main` takes pull requests only, and the merge button stays disabled
  until **Build and test** is green and every review thread is resolved
  (`.github/rulesets/main-build-and-test.json`).
- `ci.yml` runs `dotnet build -c Release` + `dotnet test` on every pull request. `fly-deploy.yml`
  *calls that same workflow* as the gate its deploy job depends on, then smoke-checks `/health`
  until it reports the commit that was just built.
- Merging to `main` *proposes* a deploy; it does not perform one. The deploy job runs in the
  `production` environment, which has a required reviewer, so the run waits at *Waiting* until the
  maintainer approves it. There is no staging environment — that approval is the last look.
- Commit messages are plain imperative sentences describing the intent, not conventional-commit
  prefixes: *"Let a deploy recognise its own release, not just a live one"*, *"Split the games list
  on the scoreline, not the calendar"*.
- The pull request template asks for prose — what changed, why, and how it was checked — and asks
  you to call out a migration or a change to what an anonymous visitor can see.
- `.editorconfig` codifies the existing style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase`
  private fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- Before opening a pull request, run the **`code-reviewer`** agent
  (`.claude/agents/code-reviewer.md`) over the change. It reviews comment hygiene — redundant and
  over-explaining comments out, rationale that changes a future decision kept — plus DRY and SOLID
  as this codebase applies them, the traps in `known_issues.md`, the circuit-lifecycle and
  `Result` call-site rules, the resx keys a new string needs, and coverage of the changed lines
  (`scripts/coverage.sh`, 80% floor). It knows the deliberate exceptions — no interfaces for
  services, the live-match split, no component tests, repetition in test arrangement — so it does
  not argue with them. It reports by default and only edits when asked to apply its findings.

## Environment notes

Claude Code web containers are rebuilt every session and ship no .NET SDK, so
`.claude/hooks/session-start.sh` installs `dotnet-sdk-10.0` from **Ubuntu's own archive** and warms
the NuGet cache. It has to be Ubuntu's: the container's egress policy blocks
`builds.dotnet.microsoft.com`, so `dotnet-install.sh` 403s. Chromium is already at
`/opt/pw-browsers/chromium` and `visual-check.sh` drives it rather than downloading one.

That SDK and CI's used to be different builds — a Razor error once appeared on the former while CI
stayed green — so **`global.json` pins 10.0.110 with `rollForward: disable`** and `ci.yml` installs
from that file. A green check now means the same SDK compiled it.

The pin is what Ubuntu ships, which is the one thing here that cannot be chosen, so bumping it
follows the archive rather than leading it. `.dockerignore` keeps `global.json` out of the image on
purpose — no container image exists for the pinned build — so the deploy still builds on `sdk:10.0`.
See `docs/known_issues.md`, "the SDK the pin cannot reach", before changing any of it.

Locally the database and logs live under `%LOCALAPPDATA%\FootballFormation\`; set `APP_DATA_DIR` to
put them elsewhere (it is `/data` in the container).
