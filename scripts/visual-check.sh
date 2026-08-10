#!/bin/bash
# Boots the app against a throwaway database and screenshots every page. See docs/testing.md.
#
#   scripts/visual-check.sh            # screenshots into artifacts/visual/
#   VISUAL_PORT=5300 scripts/visual-check.sh
#   VISUAL_APP_DLL=out/FootballFormation.Web.dll scripts/visual-check.sh   # skip the build
#
# VISUAL_APP_DLL is how CI runs it: the app is built once, in its own job, and this harness starts
# that published copy instead of compiling a second one. Unset locally, where building from the
# sources is the point — it is how an edit shows up in the screenshots.
#
# The database is a fresh temporary file every run, so the captures are deterministic — a first-run
# install with one seeded season — and nothing here can touch a real one.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${VISUAL_PORT:-5228}"
OUT="${VISUAL_OUT_DIR:-$REPO/artifacts/visual}"
DATA_DIR="$(mktemp -d)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

cleanup() {
  [ -n "${APP_PID:-}" ] && kill "$APP_PID" 2>/dev/null || true
  rm -rf "$DATA_DIR"
}
trap cleanup EXIT

# Playwright drives the Chromium already in the image; PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD stops the
# install from fetching a second copy it cannot reach anyway.
if [ ! -d "$REPO/scripts/node_modules/playwright" ]; then
  echo "Installing playwright..."
  (cd "$REPO/scripts" && PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm install --silent --no-fund --no-audit)
fi

if [ -n "${VISUAL_APP_DLL:-}" ]; then
  echo "Using the app already built at $VISUAL_APP_DLL"
  # Started from its own directory, the way the Dockerfile's WORKDIR does it: a published app takes
  # its content root from the working directory, and from anywhere else MapStaticAssets answers 200
  # with an empty body for every file — blazor.web.js included, so the page renders and then never
  # becomes interactive, and every wait in blazor.mjs times out.
  APP_CWD="$(cd "$(dirname "$VISUAL_APP_DLL")" && pwd)"
  APP_COMMAND=(dotnet "$(basename "$VISUAL_APP_DLL")")
else
  echo "Building..."
  dotnet build "$REPO/src/FootballFormation.Web/FootballFormation.Web.csproj" -c Release
  APP_CWD="$REPO"
  APP_COMMAND=(dotnet run --project "$REPO/src/FootballFormation.Web/FootballFormation.Web.csproj"
    -c Release --no-build)
fi

echo "Starting the app on port $PORT..."
# Development, because /dev/login — the route that signs the browser in without a password — is only
# mapped outside Production, and only for loopback callers.
(
  cd "$APP_CWD"
  ASPNETCORE_ENVIRONMENT=Development APP_DATA_DIR="$DATA_DIR" \
    exec "${APP_COMMAND[@]}" --urls "http://127.0.0.1:$PORT" > "$DATA_DIR/app.log" 2>&1
) &
APP_PID=$!

for _ in $(seq 1 60); do
  if curl -fsS --noproxy '*' --max-time 5 "http://127.0.0.1:$PORT/" -o /dev/null 2>/dev/null; then
    break
  fi
  # A crash on boot (a bad migration, a port already taken) would otherwise burn the full minute.
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "The app exited during startup:" >&2
    tail -30 "$DATA_DIR/app.log" >&2
    exit 1
  fi
  sleep 1
done

VISUAL_BASE_URL="http://127.0.0.1:$PORT" VISUAL_OUT_DIR="$OUT" \
  node "$REPO/scripts/visual-check.mjs"
