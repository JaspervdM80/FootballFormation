---
name: pre-pr
description: Get a finished change ready for a pull request — up to date with origin/main, Release build, tests, changed-line coverage, the verify matrix for UI changes, the code-reviewer agent, the docs and skills it touches, and a PR body in the repository's template. Use when a change is done and before asking to commit, push or open a pull request; also as /pre-pr.
---

# Before the pull request

Merging releases straight onto the live volume, and the four checks on the pull request are the last
look. This is everything that can be checked before it gets there, in the order that fails cheapest.
**It stops before committing**: a commit, a push or a pull request happens only when asked for.

## 1. Start from main

```bash
git fetch origin main
git rev-list --count HEAD..origin/main      # 0, or rebase onto origin/main first
git branch --show-current                   # feature/…, bug/… or ci/… — rename anything else
```

## 2. Build, test, coverage

```bash
dotnet build -c Release        # warnings are errors; the push hook runs this too
dotnet test
scripts/coverage.sh            # 80% of the changed lines, per file
```

Quote the changed-line coverage number, and only one you measured. A file under the floor needs the
test for the behaviour it lacks, not a test written to move the number.

## 3. UI changes

For a change to a `.razor` file or any CSS, run `scripts/verify-matrix.sh` over the routes it can
reach, and read the admin-only list (the `verify-ui` skill). For a change to `tests/ui` or what a
spec covers, run the specs it touches: `cd tests/ui && npm test -- <name>`.

## 4. Review

Run the **`code-reviewer`** agent over the branch and fix every Blocking finding. A Should-fix you
leave is said out loud in the pull request, with why.

## 5. The record

- Behaviour changed → the `docs/` page for that area changed in the same branch, and the skill too if
  it states the rule. A non-obvious bug's cause → a `docs/known_issues/` entry.
- A skill or `CLAUDE.md` edited → `dotnet test --filter InstructionReferenceTests` is green.
- A migration → rehearsed on a copy of the database (the `migrations` skill), and said in the body.

## 6. The pull request body

Follow `.github/pull_request_template.md`: prose about what changed, why, and how it was checked, as
long as the change deserves. Say out loud when it applies:

- **a migration** — it runs unattended against the live volume on the next deploy;
- **anything an anonymous visitor can now see or do differently.**

List what was run and its result — build, tests, coverage number, matrix, review — and what was not,
with why. End with the attribution line the session gives.

## 7. Stop

Report what is ready and what is not. Commit, push and open the pull request only when asked;
never push to `main`, and merging is a person's call.
