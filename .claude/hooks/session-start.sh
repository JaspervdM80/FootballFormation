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

# .NET 10 ships in Ubuntu 24.04's own archive (noble-updates/main), so this needs no extra apt
# source. That matters: the usual dotnet-install.sh route downloads from builds.dotnet.microsoft.com,
# which the container's egress policy blocks — the install would 403 before it fetched anything.
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Installing the .NET SDK..."
  # Unrelated third-party PPAs in the base image are also blocked by that policy and fail here.
  # Their failure says nothing about the Ubuntu archive, so it must not abort the install.
  apt-get update -o Acquire::Retries=3 || true
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0
fi

dotnet --version

# Warms the NuGet cache into the cached container image, so the first build of the session is a
# build rather than a download.
echo "Restoring packages..."
dotnet restore "$REPO/FootballFormation.slnx"

echo "Ready: dotnet build / dotnet test / scripts/visual-check.sh"
