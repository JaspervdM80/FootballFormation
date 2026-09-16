#!/bin/bash
# Puts a backup from /data/backups back as the live database, then restarts the app.
#
#   scripts/restore-db.sh                          # lists what is there
#   scripts/restore-db.sh manual-20260916-2110.db
#
# Needs flyctl signed in to the app.
#
# You cannot ssh into a stopped machine, so the file is swapped while the app is still up and the
# restart immediately after is what makes it take. The app holds the old file open for those few
# seconds; that is survivable because it is idle during a restore and because Program.cs runs
# PRAGMA integrity_check on every boot and refuses to serve a damaged database.
set -euo pipefail

APP_NAME="${FLY_APP:-gjs-meiden}"
DB=/data/footballformation.db
CHOSEN="${1:-}"

remote() { flyctl ssh console -a "$APP_NAME" -C "$*"; }

if [ -z "$CHOSEN" ]; then
  echo "Backups on the volume:"
  remote "ls -la /data/backups"
  echo
  echo "Re-run with one of these filenames: scripts/restore-db.sh <filename>"
  exit 0
fi

SOURCE="/data/backups/$CHOSEN"
if ! remote "test -f $SOURCE" > /dev/null 2>&1; then
  echo "No backup called $CHOSEN on the volume. Nothing was changed."
  echo "Run scripts/restore-db.sh with no argument to see what is there."
  exit 1
fi

echo "About to replace the live database of $APP_NAME with $SOURCE."
remote "ls -la $SOURCE"
echo
echo "Everything recorded since that backup will be gone — goals and substitutions included."
read -r -p "Type the filename again to confirm: " CONFIRM
if [ "$CONFIRM" != "$CHOSEN" ]; then
  echo "Names did not match. Nothing was changed."
  exit 1
fi

# The database being replaced is itself worth keeping: a restore aimed at the wrong backup is only
# recoverable if what it overwrote still exists.
SAFETY="/data/backups/before-restore-$(date +%Y%m%d-%H%M%S).db"
echo "Keeping what is there now as $SAFETY"
remote "cp $DB $SAFETY"

# Copy then rename: the rename is atomic, so the live path is never a half-written file.
echo "Restoring..."
remote "cp $SOURCE $DB.restoring"
remote "mv $DB.restoring $DB"

# The old log and shared-memory file belong to the database just replaced. Left in place, SQLite
# replays them over the restored file on the next open and quietly undoes the whole restore.
remote "rm -f $DB-wal $DB-shm"

echo "Restarting $APP_NAME..."
flyctl apps restart "$APP_NAME"

echo
echo "Checking it serves..."
for attempt in 1 2 3 4 5; do
  CODE="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 https://gjs-meiden.nl/health)" || CODE=000
  if [ "$CODE" = "200" ]; then
    echo "Restored from $CHOSEN, and /health is answering."
    exit 0
  fi
  echo "Attempt $attempt: HTTP $CODE"
  [ "$attempt" -lt 5 ] && sleep $((attempt * 5))
done

echo
echo "/health never answered. The boot integrity check may have refused the restored database."
echo "Look at the log, and $SAFETY is what was there before this ran:"
echo "  flyctl logs -a $APP_NAME"
exit 1
