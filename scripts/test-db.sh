#!/bin/bash
# Loads the test site with a copy of the live database, with every name and free-text field replaced.
# See docs/deployment.md, "Test site".
#
#   scripts/test-db.sh
#
# Needs flyctl signed in and sqlite3 on the PATH. Reads production through dev-db.sh and writes only
# to the test app, which it refuses to do unless the app's name ends in -test.
set -euo pipefail

TEST_APP="${FLY_TEST_APP:-gjs-meiden-test}"
case "$TEST_APP" in
  *-test) ;;
  *) echo "Refusing to write to $TEST_APP: the target must be a -test app." >&2; exit 1 ;;
esac

command -v sqlite3 > /dev/null || { echo "sqlite3 is not on the PATH." >&2; exit 1; }

SCRIPTS="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

DEV_DB_DIR="$WORK/raw" "$SCRIPTS/dev-db.sh"
RAW="$WORK/raw/footballformation.db"
OUT="$WORK/incoming.db"

sqlite3 -bail "$RAW" << 'SQL'
PRAGMA foreign_keys = ON;
BEGIN;

CREATE TEMP TABLE Placeholder (Rank INTEGER PRIMARY KEY, Name TEXT NOT NULL);
INSERT INTO Placeholder (Name) VALUES
  ('Aaltje'), ('Bente'), ('Carmen'), ('Daphne'), ('Esmee'), ('Floor'), ('Gwen'), ('Hanna'),
  ('Iris'), ('Jolien'), ('Kiki'), ('Lysanne'), ('Maud'), ('Noor'), ('Olivia'), ('Pien'),
  ('Quinty'), ('Rinske'), ('Selma'), ('Tess'), ('Uma'), ('Vera'), ('Wende'), ('Xenia'),
  ('Yara'), ('Zara'), ('Amber'), ('Britt'), ('Cato'), ('Demi');

CREATE TEMP TABLE Renamed AS
SELECT p.Id, COALESCE(n.Name, 'Speler ' || p.Id) AS Name
FROM (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS Rank FROM Players) p
LEFT JOIN Placeholder n ON n.Rank = p.Rank;

UPDATE Players SET FirstName = (SELECT Name FROM Renamed r WHERE r.Id = Players.Id), Surname = NULL;

-- The duty rotas are typed names; any player's placeholder will do.
UPDATE Games SET
  DressingRoomDuty = CASE WHEN DressingRoomDuty IS NULL THEN NULL ELSE (SELECT Name FROM Renamed ORDER BY (Id * 31 + Games.Id * 17) % 97 LIMIT 1) END,
  FlagDuty         = CASE WHEN FlagDuty IS NULL THEN NULL ELSE (SELECT Name FROM Renamed ORDER BY (Id * 37 + Games.Id * 13) % 97 LIMIT 1) END,
  WashDuty         = CASE WHEN WashDuty IS NULL THEN NULL ELSE (SELECT Name FROM Renamed ORDER BY (Id * 41 + Games.Id * 11) % 97 LIMIT 1) END;

UPDATE GameComments SET Body = 'Opmerking ' || Id;
UPDATE Trainings SET Notes = NULL;

-- Real password hashes stay on production; the boot seeds admin/admin into an empty table.
DELETE FROM Users;
DELETE FROM PushSubscriptions;

COMMIT;
SQL

sqlite3 -bail "$RAW" "VACUUM INTO '$(cygpath -m "$OUT" 2> /dev/null || printf '%s' "$OUT")'"

check() { sqlite3 "$OUT" "$1"; }
[ "$(check 'PRAGMA integrity_check')" = "ok" ] || { echo "The anonymised copy failed its integrity check." >&2; exit 1; }
[ "$(check 'SELECT COUNT(*) FROM Players WHERE Surname IS NOT NULL')" = "0" ] || { echo "Surnames survived." >&2; exit 1; }
[ "$(check 'SELECT COUNT(*) FROM Users') $(check 'SELECT COUNT(*) FROM PushSubscriptions')" = "0 0" ] \
  || { echo "Accounts or push subscriptions survived." >&2; exit 1; }
echo "Anonymised $(check 'SELECT COUNT(*) FROM Players') players."

if command -v cygpath > /dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  local_path() { cygpath -w "$1"; }
else
  local_path() { printf '%s' "$1"; }
fi

echo "Waking $TEST_APP..."
curl -sS -o /dev/null --max-time 60 "https://$TEST_APP.fly.dev/health" || true

# sftp put will not overwrite, so a copy left by a run that failed after uploading goes first.
flyctl ssh console -a "$TEST_APP" -C "rm -f /data/incoming.db" || true
flyctl ssh sftp put "$(local_path "$OUT")" /data/incoming.db -a "$TEST_APP"

# fly.test.toml's entrypoint swaps incoming.db in on the way up.
flyctl apps restart "$TEST_APP"

echo "Loaded. Sign in at https://$TEST_APP.fly.dev with admin / admin and set a new password straight away."
