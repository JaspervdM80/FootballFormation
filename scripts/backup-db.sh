#!/bin/bash
# Takes a timestamped, verified copy of the live database, to restore from if a change goes wrong.
# Read-only against production: it fetches over `fly ssh sftp get` and never writes back.
#
#   scripts/backup-db.sh                          # into <data dir>/production-backups
#   BACKUP_DIR=/d/backups scripts/backup-db.sh
#
# Needs flyctl signed in to the app. Unlike scripts/dev-db.sh this leaves the development database
# alone — nothing here writes outside the backup directory. Nothing is ever pruned: a backup script
# that deletes backups is the wrong surprise to spring on someone mid-restore.
#
# The copy carries real player names, so it belongs on a development machine and nowhere else.
set -euo pipefail

APP_NAME="${FLY_APP:-gjs-meiden}"
STAMP="$(date +%Y%m%d-%H%M%S)"

if [ -n "${BACKUP_DIR:-}" ]; then
  DEST="$BACKUP_DIR"
elif [ -n "${APP_DATA_DIR:-}" ]; then
  DEST="$APP_DATA_DIR/production-backups"
elif [ -n "${LOCALAPPDATA:-}" ]; then
  DEST="$LOCALAPPDATA/FootballFormation/production-backups"
else
  DEST="${XDG_DATA_HOME:-$HOME/.local/share}/FootballFormation/production-backups"
fi

mkdir -p "$DEST"
TARGET="$DEST/production-$STAMP.db"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Why the path juggling on Windows: see scripts/dev-db.sh.
if command -v cygpath > /dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  local_path() { cygpath -w "$1"; }
else
  local_path() { printf '%s' "$1"; }
fi

echo "Fetching from $APP_NAME..."
flyctl ssh sftp get /data/footballformation.db "$(local_path "$WORK/db")" -a "$APP_NAME"

# A missing -wal means the database was checkpointed, not that the fetch failed — see dev-db.sh.
if ! flyctl ssh sftp get /data/footballformation.db-wal "$(local_path "$WORK/db-wal")" -a "$APP_NAME"
then
  echo "No -wal on the volume; the database was checkpointed."
  rm -f "$WORK/db-wal"
fi

# Two files fetched one after the other while the app is serving can be torn against each other, and
# the aspnet image carries no sqlite3 to snapshot them as one over there — so the checks catch it here.
if command -v sqlite3 > /dev/null 2>&1; then
  echo "Folding in the write-ahead log..."
  # Every sqlite3 call here is allowed to fail: a torn copy makes them exit non-zero, and under
  # `set -e` that would kill the script at the one moment its checks are the thing worth reaching.
  CHECKPOINTED=yes
  sqlite3 "$WORK/db" "PRAGMA wal_checkpoint(TRUNCATE);" > /dev/null 2>&1 || CHECKPOINTED=no

  echo "Verifying..."
  INTEGRITY="$(sqlite3 "$WORK/db" "PRAGMA integrity_check;" 2>&1)" || INTEGRITY="${INTEGRITY:-unreadable}"
  # An empty foreign_key_check means "no violations", so a failure that printed nothing has to be
  # given a value of its own or it reads as a pass.
  FOREIGN_KEYS="$(sqlite3 "$WORK/db" "PRAGMA foreign_key_check;" 2>&1)" || FOREIGN_KEYS="${FOREIGN_KEYS:-unreadable}"

  if [ "$INTEGRITY" != "ok" ] || [ -n "$FOREIGN_KEYS" ]; then
    mv "$WORK/db" "$TARGET.failed"
    echo "integrity_check: $INTEGRITY"
    [ -n "$FOREIGN_KEYS" ] && echo "foreign_key_check: $FOREIGN_KEYS"
    echo "This copy is damaged and is NOT a restore point. Kept as $TARGET.failed to look at."
    echo "Re-run — a copy taken while the app was mid-write can fail this and the next one pass."
    exit 1
  fi

  # Only a checkpoint that reported success has folded the log in; deleting it otherwise would throw
  # away every write since the last one while the file still looks like a complete backup.
  if [ "$CHECKPOINTED" = "no" ] && [ -f "$WORK/db-wal" ]; then
    mv "$WORK/db" "$TARGET"
    mv "$WORK/db-wal" "$TARGET-wal"
    echo "Verified backup at $TARGET, with its log at $TARGET-wal"
    echo "The log could not be folded in, so restore BOTH together."
  else
    rm -f "$WORK/db-wal"
    mv "$WORK/db" "$TARGET"
    echo "Verified backup at $TARGET"
    echo "Restore with: stop the machine, then put this file at /data/footballformation.db"
    echo "and delete /data/footballformation.db-wal and -shm beside it."
  fi
else
  mv "$WORK/db" "$TARGET"
  if [ -f "$WORK/db-wal" ]; then
    mv "$WORK/db-wal" "$TARGET-wal"
    echo "UNVERIFIED backup at $TARGET, with its log at $TARGET-wal"
    echo "Restore BOTH together, or the copy is missing whatever was not checkpointed."
  else
    echo "UNVERIFIED backup at $TARGET"
  fi
  echo "No sqlite3 on PATH, so the log could not be folded in and nothing checked the copy is sound."
  echo "Install sqlite3 and re-run to get a single verified file."
fi

echo
echo "Real player names are in this file — keep it off shared storage."
echo "For a restore point that survives losing the volume, snapshot the volume instead:"
echo "  flyctl volumes list -a $APP_NAME"
echo "  flyctl volumes snapshots create <vol-id> -a $APP_NAME"
