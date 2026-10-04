#!/bin/bash
# Boots the app against a throwaway database and screenshots every page. See docs/testing/visual-and-touch-checks.md.
#
#   scripts/visual-check.sh            # screenshots into artifacts/visual/
#   VISUAL_PORT=5300 scripts/visual-check.sh
#   VISUAL_APP_DLL=out/FootballFormation.Web.dll scripts/visual-check.sh   # skip the build
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${VISUAL_PORT:-5228}"
OUT="${VISUAL_OUT_DIR:-$REPO/artifacts/visual}"

source "$REPO/scripts/app-boot.sh"
ensure_playwright
boot_app

VISUAL_BASE_URL="http://127.0.0.1:$PORT" VISUAL_OUT_DIR="$OUT"   node "$REPO/scripts/visual-check.mjs"
