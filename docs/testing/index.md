# Testing

`tests/FootballFormation.Core.Tests` — xUnit v3. Run with `dotnet test` from the repo root.

CI runs `dotnet build -c Release` and `dotnet test` as a **gate on the merge**: it is one of the four
checks that have to be green before a pull request can land, and since landing is what deploys, a
commit that does not compile or does not pass never becomes a release. The gate used to sit one step
later — the deploy job depended on a re-run of this workflow — and it moved forward when merging
became the release. What stands between a merge and the volume now is the Docker build, which
compiles the app again and fails the deploy if it cannot, and the `/health` smoke check, which
refuses to call the release good until the new commit is the one answering.

Getting that ordering right matters because the app auto-migrates on boot: a bad migration reaches a
live database on startup, and the only cheap place to catch it is here.

- [Unit Testing](unit-testing.md) — what is and isn't covered, conventions, adding a test, and coverage.
- [UI Testing](ui-testing.md) — the Playwright suite in `tests/ui`, the guard test, and what makes a test stable enough for CI.
- [Visual and Touch Checks](visual-and-touch-checks.md) — `scripts/visual-check.sh` and the touch-target audit.
- [Running Tests in Claude Code on the Web](running-in-claude-code.md) — what the web sandbox can and can't run.
