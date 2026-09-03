#!/bin/bash
# Replaces the local development database with a copy of the live one, keeping the old file.
# See docs/known_issues/ef-core.md, "a history that claims more than the file holds".
#
#   scripts/dev-db.sh                      # into %LOCALAPPDATA%\FootballFormation (or $APP_DATA_DIR)
#   DEV_DB_DIR=/tmp/scratch scripts/dev-db.sh
#
# Needs flyctl signed in to the app. Read-only against production: it fetches over
# `fly ssh sftp get` and never writes back. The copy carries real player names, so it belongs on a
# development machine and nowhere else — which is why only the newest few are kept.
set -euo pipefail

APP_NAME="${FLY_APP:-gjs-meiden}"
KEEP=3

if [ -n "${DEV_DB_DIR:-}" ]; then
  DATA_DIR="$DEV_DB_DIR"
elif [ -n "${APP_DATA_DIR:-}" ]; then
  DATA_DIR="$APP_DATA_DIR"
elif [ -n "${LOCALAPPDATA:-}" ]; then
  DATA_DIR="$LOCALAPPDATA/FootballFormation"
else
  DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/FootballFormation"
fi

DB="$DATA_DIR/footballformation.db"
BACKUPS="$DATA_DIR/backups"
mkdir -p "$BACKUPS"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Git Bash rewrites a leading-slash argument into a Windows path before flyctl ever sees it, so
# /data/... arrives as C:/Program Files/Git/data/... and fails as "file does not exist". Turning
# that off applies to every argument, which is why the local side is spelled out for Windows here.
if command -v cygpath > /dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  local_path() { cygpath -w "$1"; }
else
  local_path() { printf '%s' "$1"; }
fi

echo "Fetching from $APP_NAME..."
flyctl ssh sftp get /data/footballformation.db "$(local_path "$WORK/db")" -a "$APP_NAME"

# The write-ahead log holds everything committed since the last checkpoint, so the .db on its own is
# a copy missing the newest matches. SQLite deletes it when the last connection closes, so a machine
# that shut down cleanly has none — which is a complete database, not a failure.
if ! flyctl ssh sftp get /data/footballformation.db-wal "$(local_path "$WORK/db-wal")" -a "$APP_NAME"
then
  echo "No -wal on the volume; the database was checkpointed."
  rm -f "$WORK/db-wal"
fi

# Fetching first means a failure above leaves the working database alone rather than backed up and
# gone. The log goes with it: without it the copy is missing whatever was not checkpointed.
if [ -f "$DB" ]; then
  KEPT="$BACKUPS/replaced-$(date +%Y%m%d-%H%M%S).db"
  mv "$DB" "$KEPT"
  [ -f "$DB-wal" ] && mv "$DB-wal" "$KEPT-wal"
  echo "Kept the database that was there as $KEPT"
fi

# A leftover -shm belongs to the file just moved aside; SQLite would read it against the new one.
rm -f "$DB-wal" "$DB-shm"
mv "$WORK/db" "$DB"
[ -f "$WORK/db-wal" ] && mv "$WORK/db-wal" "$DB-wal"

# DatabaseSafety.Prune only globs pre-migration-*.db, so these are the script's own to clear out.
# Read into an array rather than piped: with pipefail, `ls` finding nothing would end the script.
mapfile -t copies < <(ls -1t "$BACKUPS"/replaced-*.db 2> /dev/null || true)
if [ "${#copies[@]}" -gt "$KEEP" ]; then
  for stale in "${copies[@]:KEEP}"; do
    rm -f "$stale" "$stale-wal"
    echo "Pruned $(basename "$stale")"
  done
fi

echo "Development database refreshed at $DB"
echo "The app checkpoints and verifies it on the next boot — watch for 'Schema matches the model'."
