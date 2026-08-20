#!/bin/bash
# Runs the test suite with coverage collection, then judges the lines this branch changed. See
# docs/testing/unit-testing.md.
#
#   scripts/coverage.sh                       # against origin/main, 80% floor
#   COVERAGE_BASE=HEAD~1 scripts/coverage.sh  # against the previous commit
#   COVERAGE_THRESHOLD=90 scripts/coverage.sh
#   COVERAGE_SKIP_TEST=1 scripts/coverage.sh  # re-judge the last run without re-running the suite
#
# Exits non-zero when the changed lines fall under the floor, so it can be a pipeline step as it
# stands. The Cobertura report is left in artifacts/coverage/ for a reporter to pick up.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${COVERAGE_DIR:-$REPO/artifacts/coverage}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [ -z "${COVERAGE_SKIP_TEST:-}" ]; then
  # Cleared first: the collector writes into a fresh GUID directory every run, and coverage.mjs
  # reads the first report it finds. A stale one would be judged instead of this run's.
  rm -rf "$OUT"
  # Release, like CI — warnings are errors there, so a coverage run that passes in Debug and fails
  # the build in CI would be the worst of both.
  # coverage.runsettings is what decides which files the collector counts, and CI's test step
  # passes the same file — a local number and a pipeline number mean the same thing.
  dotnet test "$REPO" -c Release --collect:"XPlat Code Coverage" \
    --settings "$REPO/coverage.runsettings" --results-directory "$OUT"
fi

COVERAGE_DIR="$OUT" node "$REPO/scripts/coverage.mjs"
