#!/bin/bash
# The verify-ui skill's matrix, measured: each route at desktop and phone width, as a visitor and as an admin.
#
#   scripts/verify-matrix.sh                       # every route without an id, on a throwaway database
#   scripts/verify-matrix.sh /players /games       # just these
#   VERIFY_BASE_URL=http://localhost:5228 scripts/verify-matrix.sh /games/12/live   # an app you already run, with its data
#
# Prints a line per cell and the controls an admin sees that a visitor does not, writes a screenshot per cell to
# artifacts/verify/, and exits non-zero on a console error, horizontal overflow, or an admin turned away.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$REPO/scripts/app-boot.sh"
ensure_playwright

if [ -z "${VERIFY_BASE_URL:-}" ]; then
  PORT="${VERIFY_PORT:-5229}"
  boot_app
  export VERIFY_BASE_URL="http://127.0.0.1:$PORT" VERIFY_FRESH_DATABASE=1
fi

MATRIX="$REPO/scripts/verify-matrix.mjs"
OUT="${VERIFY_OUT_DIR:-$REPO/artifacts/verify}"
# Git Bash on Windows rewrites an argument like /players into a path under its own install; keep the routes as typed.
if command -v cygpath >/dev/null 2>&1; then
  MATRIX="$(cygpath -m "$MATRIX")"
  OUT="$(cygpath -m "$OUT")"
fi

MSYS_NO_PATHCONV=1 VERIFY_OUT_DIR="$OUT" node "$MATRIX" "$@"
