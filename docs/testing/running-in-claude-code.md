# Running Tests in Claude Code on the Web

## Running it in Claude Code on the web

Those containers are rebuilt per session and ship no .NET SDK, so without setup an agent can read
the code but cannot compile it, run the tests, or open a page. `.claude/hooks/session-start.sh`
installs it on session start.

It takes the SDK from **Ubuntu 24.04's own archive** (`dotnet-sdk-10.0` in `noble-updates/main`),
not from `dotnet-install.sh` — the container's egress policy blocks
`builds.dotnet.microsoft.com`, so the usual installer 403s before it downloads anything.
`api.nuget.org` is reachable, so `dotnet restore` works normally.

Chromium is already in the image at `/opt/pw-browsers/chromium`; `tests/ui`'s Playwright config
picks it up automatically rather than downloading its own — see
[docs/testing/ui-testing.md](ui-testing.md).
