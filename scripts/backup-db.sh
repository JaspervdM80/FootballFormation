#!/bin/bash
# Copies the live database to /data/backups/manual-<timestamp>.db, on the volume, with its -wal and
# -shm beside it when they exist. Nothing is downloaded and nothing leaves Fly.
#
#   scripts/backup-db.sh                          # no downtime once the image carries sqlite3
#   SKIP_RESTART=1 scripts/backup-db.sh           # only affects the fallback below
#
# Needs flyctl signed in to the app.
#
# Two routes, and which one runs depends on the image that is deployed. The image built from this
# repository carries sqlite3 and /usr/local/bin/backup-db, which folds the write-ahead log into the
# .db and verifies the copy without touching the running app. Until that image is live there is no
# sqlite3 on the far side, and the only way to get the newest writes into the .db is a clean
# shutdown — so the fallback restarts the app. Delete the fallback once the image has shipped.
#
# Named manual-* because DatabaseSafety.Prune globs pre-migration-*.db; these are nobody's to delete.
set -euo pipefail

APP_NAME="${FLY_APP:-gjs-meiden}"
DB=/data/footballformation.db

remote() { flyctl ssh console -a "$APP_NAME" -C "$*"; }

if remote "test -x /usr/local/bin/backup-db" > /dev/null 2>&1; then
  echo "Backing up on the volume, no restart needed..."
  # stderr folded in: without it a failure on the far side reports nothing but an exit code.
  OUTPUT="$(remote /usr/local/bin/backup-db 2>&1)" || true
  echo "$OUTPUT"

  # flyctl does not reliably surface the remote exit code, so the marker the script prints on success
  # is what decides, not $?.
  if ! printf '%s' "$OUTPUT" | grep -q "BACKUP OK"; then
    echo "The backup did not report success. Nothing here is a restore point."
    exit 1
  fi

  echo
  remote "ls -la /data/backups"
  exit 0
fi

echo "This image has no /usr/local/bin/backup-db yet — falling back to the restart route."

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

TARGET="/data/backups/manual-$(date +%Y%m%d-%H%M%S).db"
remote "mkdir -p /data/backups"
remote "cp $DB $TARGET"

# The log is copied too even though the restart above should have emptied it: a shutdown that ran
# out of kill_timeout, or SKIP_RESTART=1, leaves writes in it that the .db does not have.
copy_if_present() {
  if remote "test -f $1" > /dev/null 2>&1; then
    remote "cp $1 $2"
    echo "  $(basename "$2")"
  else
    echo "  (no $(basename "$1") — nothing to copy)"
  fi
}

echo "Copied:"
echo "  $(basename "$TARGET")"
copy_if_present "$DB-wal" "$TARGET-wal"
copy_if_present "$DB-shm" "$TARGET-shm"

echo
echo "Backed up to $TARGET"
remote "ls -la /data/backups"
echo
echo "Restore it with: scripts/restore-db.sh $(basename "$TARGET")"
