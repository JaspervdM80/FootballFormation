---
name: build-and-release
description: Building, the CI checks, and how a merge reaches production. Covers Release-only warnings-as-errors, the MSB3568 promotion, the global.json SDK pin, the three required checks, and merge-is-release. Use before pushing, when CI is red, or when touching a workflow, Dockerfile or global.json.
---

# Build and release

```bash
dotnet build -c Release        # what CI builds — warnings are errors here
dotnet test                    # xUnit v3, real SQLite
cd src/FootballFormation.Web && dotnet run     # http://localhost:5228
```

## Build Release before pushing

`Directory.Build.props` sets `TreatWarningsAsErrors` in **Release only** — a Debug build that looks
clean can still fail CI. Local Debug stays quick to iterate on; Release is what stops a warning
landing.

The one exception is **`MSB3568`** (duplicate resource name), promoted via `MSBuildWarningsAsErrors`
in *every* configuration. `TreatWarningsAsErrors` is a compiler property and does not cover
`MSB####` codes from the MSBuild engine, so a colliding resx key warned and built green even in
Release — and it silently changes what the app says.

## The SDK is pinned, and the pin follows Ubuntu

`global.json` pins **10.0.110 with `rollForward: disable`**, and `ci.yml` installs from that file, so
a green check means the same SDK compiled it. The pin is what Ubuntu's archive ships — the one thing
here that cannot be chosen — so bumping it follows the archive rather than leading it.
`.dockerignore` keeps `global.json` out of the image on purpose: no container image exists for the
pinned build, so the deploy builds on `sdk:10.0`.

Claude Code web containers ship no .NET SDK, so `.claude/hooks/session-start.sh` installs
`dotnet-sdk-10.0` from **Ubuntu's own archive** — it has to be Ubuntu's, because the container's
egress policy blocks `builds.dotnet.microsoft.com` and `dotnet-install.sh` 403s. Chromium is already
at `/opt/pw-browsers/chromium`.

## Three checks, and the merge waits for all of them

`main` takes pull requests only. The merge button stays disabled until **Build and test**,
**Coverage** and **Playwright** are all green, the branch is up to date with `main`, and every
review thread is resolved. `.github/rulesets/main-every-check-green.json` grants no bypass to
anyone.

`ci.yml` deliberately carries **no `paths:` filter** — a required check that never reports leaves a
pull request pending forever rather than mergeable, so "skip CI for docs" would wedge every docs-only
PR. If a job is renamed, the ruleset's `context` must be renamed in the same change.

A flaky browser job is **re-run, not merged past**.

## Merging to `main` releases

`fly-deploy.yml` starts on the merge commit with no gate job and no approval, then smoke-checks
`/health` until it reports the commit that was just built. There is no staging environment and
nothing re-runs on `main`, so the three checks on the pull request are the last look.

The app **auto-migrates against the live volume on boot**, so a merge is also a schema change. See the
`migrations` skill before writing one.

## Conventions

- Commit messages are plain imperative sentences describing intent, not conventional-commit prefixes:
  *"Let a deploy recognise its own release, not just a live one"*.
- Package versions live in `Directory.Packages.props`; csproj files list names only. A `Version=` in a
  csproj is a mistake.
- `.editorconfig` codifies the style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase` private
  fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- The solution file is `FootballFormation.slnx` (the new XML format).

Detail: [docs/deployment.md](../../../docs/deployment.md)
