#!/bin/bash
# Installs what a Claude Code on the web container needs to build, test and look at this app.
# The container is rebuilt for every session, so without this there is no `dotnet` on PATH and the
# agent can only read the code — it cannot compile it, run the tests, or open a page.
set -euo pipefail

# Local machines already have their own SDK; only the ephemeral web containers need this.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

REPO="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

# Telemetry and the first-run banner add nothing here, and both write to the output the agent reads.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# One JSON object on stdout and nothing else: additionalContext is the only channel into the model's
# context, and a non-zero exit with a message on stderr reaches nobody. Progress goes to stderr, or
# a stray line makes the object unparseable.
say() { echo "$@" >&2; }

emit() {
  jq -n --arg summary "$1" --arg context "$2" \
    '{
       systemMessage: $summary,
       hookSpecificOutput: {
         hookEventName: "SessionStart",
         additionalContext: $context
       }
     }'
}

# Always zero: a non-zero exit is what throws the explanation away.
emit_and_exit() { emit "$1" "$2"; exit 0; }

# .NET 10 ships in Ubuntu 24.04's own archive (noble-updates/main), so this needs no extra apt
# source. That matters: the usual dotnet-install.sh route downloads from builds.dotnet.microsoft.com,
# which the container's egress policy blocks — the install would 403 before it fetched anything.
if ! command -v dotnet >/dev/null 2>&1; then
  say "Installing the .NET SDK..."
  # Unrelated third-party PPAs in the base image are also blocked by that policy and fail here.
  # Their failure says nothing about the Ubuntu archive, so it must not abort the install.
  apt-get update -o Acquire::Retries=3 >&2 || true

  if ! DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0 >&2; then
    emit_and_exit \
      "The .NET SDK could not be installed — this session cannot build or test." \
      "The .NET SDK is not installed and apt could not install it, so dotnet build, dotnet test
and the Playwright suite are all unavailable this session. Ubuntu's archive is the only source
this container can reach (builds.dotnet.microsoft.com is blocked by the egress policy), so there
is no second route to try. Read and reason about the code, but say plainly in your summary that
nothing was compiled or run."
  fi
fi

# latestPatch absorbs a newer Ubuntu patch silently, so anything reaching here is a feature-band
# move — the divergence that once let an RZ2005 error fail here while CI stayed green.
if ! (cd "$REPO" && dotnet --version >/dev/null 2>&1); then
  emit_and_exit \
    "The installed .NET SDK does not satisfy global.json — nothing will build until that is settled." \
    "dotnet build, dotnet test and the Playwright suite will all fail immediately this session:
the SDK installed here does not satisfy $REPO/global.json.

  pinned:    $(jq -r '.sdk.version + " (rollForward: " + .sdk.rollForward + ")"' "$REPO/global.json" 2>/dev/null || echo "unreadable")
  installed: $(dotnet --list-sdks 2>/dev/null | tr '\n' ' ')

Ubuntu's archive is the only SDK source this container can reach, so the installed one cannot be
changed — builds.dotnet.microsoft.com is blocked by the egress policy. rollForward is latestPatch,
so this is a *feature band* difference, not a patch: the band is the digit group in 10.0.1xx, and
crossing it is what the pin exists to catch.

**Do not work around this by editing global.json for one build and putting it back.** CI installs
whatever that file names, so the pin and the archive have to be reconciled deliberately: raise it
with the user, and check docs/known_issues/blazor-components.md before moving the band. Until then,
report honestly that nothing was compiled or run."
fi

# Warms the NuGet cache into the cached container image, so the first build of the session is a
# build rather than a download.
say "Restoring packages..."
if ! dotnet restore "$REPO/FootballFormation.slnx" >&2; then
  emit_and_exit \
    "dotnet restore failed at startup — the first build may be slow or broken." \
    "The SDK is fine ($(cd "$REPO" && dotnet --version)) but dotnet restore failed at session start.
api.nuget.org is normally reachable from this container, so treat a repeated failure as a real
problem rather than a warm-up step. Try the build anyway — it restores again — and if that fails
too, report the restore error rather than working around it."
fi

emit \
  "Ready: dotnet build / dotnet test / scripts/visual-check.sh" \
  "The .NET SDK $(cd "$REPO" && dotnet --version) is installed and satisfies global.json, and the
NuGet cache is warm. dotnet build -c Release, dotnet test and the browser harnesses in tests/ui and
scripts/ are all available. Chromium is already at /opt/pw-browsers/chromium — never run
'playwright install'.

Before writing or editing any code, read .claude/skills/comment-rule/SKILL.md. It is the
commenting rule for this repository and it applies to every change: default to no comments, write
one only for a non-obvious *why*, one line and never a paragraph. Repository-specific exceptions
are in .claude/skills/comments/SKILL.md."
