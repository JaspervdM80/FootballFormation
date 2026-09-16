#!/bin/bash
# Copies the live database to /data/backups/manual-<timestamp>.db, on the volume. Nothing is
# downloaded and nothing leaves Fly.
#
#   scripts/backup-db.sh              # restart first, so the copy is complete
#   SKIP_RESTART=1 scripts/backup-db.sh
#
# Needs flyctl signed in to the app.
#
# The restart is the whole reason this produces one file instead of three. SQLite folds the -wal
# into the .db and deletes it on a clean shutdown, so after a restart the .db alone is the complete
# database. Without it, auto-checkpointing only fires about every 1000 pages, and on an app this
# quiet the log can hold every write since the last boot — a .db-only copy would open cleanly and
# silently lack all of it.
#
# Named manual-* because DatabaseSafety.Prune globs pre-migration-*.db; these are nobody's to delete.
set -euo pipefail

APP_NAME="${FLY_APP:-gjs-meiden}"
DB=/data/footballformation.db
TARGET="/data/backups/manual-$(date +%Y%m%d-%H%M%S).db"

remote() { flyctl ssh console -a "$APP_NAME" -C "$*"; }

# `stat -c %s` on a missing file exits non-zero, which is the answer "no -wal", not a failure.
wal_size() { remote "stat -c %s $DB-wal" 2> /dev/null || echo 0; }

if [ "${SKIP_RESTART:-}" = "1" ]; then
  echo "Skipping the restart — this copy may be missing everything still in the -wal."
else
  BEFORE="$(wal_size | tr -d '\r')"
  echo "Write-ahead log before the restart: ${BEFORE:-0} bytes"
  echo "Restarting $APP_NAME so the log is folded into the database file..."
  flyctl apps restart "$APP_NAME"

  AFTER="$(wal_size | tr -d '\r')"
  echo "Write-ahead log after the restart: ${AFTER:-0} bytes"
  # A log that survived the restart at its old size means the shutdown was killed before it
  # checkpointed — fly.toml sets no kill_timeout, so the default is 5 seconds.
  if [ "${AFTER:-0}" -gt 0 ] && [ "${AFTER:-0}" -ge "${BEFORE:-0}" ] && [ "${BEFORE:-0}" -gt 100000 ]; then
    echo "WARNING: the log did not shrink. The copy below may be incomplete — check kill_timeout."
  fi
fi

remote "mkdir -p /data/backups"
remote "cp $DB $TARGET"

echo
echo "Backed up to $TARGET"
remote "ls -la /data/backups"
echo
echo "Restore it with: scripts/restore-db.sh $(basename "$TARGET")"
