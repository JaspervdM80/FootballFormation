# Running Tests in Claude Code on the Web

## Running it in Claude Code on the web

Those containers are rebuilt per session and ship no .NET SDK, so without setup an agent can read
the code but cannot compile it, run the tests, or open a page. `.claude/hooks/session-start.sh`
installs it on session start.

It takes the SDK from **Ubuntu 24.04's own archive** (`dotnet-sdk-10.0` in `noble-updates/main`),
not from `dotnet-install.sh` — the container's egress policy blocks
`builds.dotnet.microsoft.com`, so the usual installer 403s before it downloads anything.
`api.nuget.org` is reachable, so `dotnet restore` works normally.

Chromium is already in the image at `/opt/pw-browsers/chromium`; `scripts/visual-check.sh`
installs the Playwright npm package on first use and drives that binary rather than downloading
its own.

## What the hook reports, and why it always exits zero

The hook writes a single JSON object to stdout and sends every progress line to stderr. That object
is the only channel it has: `hookSpecificOutput.additionalContext` reaches the agent, `systemMessage`
reaches the person. **A non-zero exit with a message on stderr reaches neither** — which is how a
session once spent its first twenty minutes rediscovering, from a raw `SDK not found`, a broken pin
this script had already diagnosed and explained at startup.

So it exits zero even when the SDK is unusable. The session is still worth having — code can be read
and reasoned about — and failing the hook is precisely what threw the explanation away. It reports
three outcomes: the SDK could not be installed, the installed SDK does not satisfy `global.json`, or
everything is ready. Two of those tell the agent to say plainly in its summary that nothing was
compiled or run.

The `global.json` case is the one worth reading twice, because the tempting workaround is wrong:
`ci.yml` installs whatever that file names, so editing the pin for one build and putting it back
makes a green local run that CI does not share. `rollForward` is `latestPatch`, so a patch bump in
Ubuntu's archive is absorbed silently and anything that still reaches this branch is a *feature
band* difference — which is exactly what the pin exists to refuse. See
[known_issues](../known_issues/blazor-components.md).

## The other hooks

`.claude/settings.json` wires these on every machine, not only the web containers:

- **Session start** also fetches `origin/main` and says how many commits behind it the checkout is,
  because a worktree handed to a session can be several merges old.
- **`release-gate.sh`**, before any `git push`: runs `dotnet build -c Release` on the working tree and
  refuses the push with the errors if it fails. The tree it built is remembered, so a second push of
  the same one is instant.
- **`resx-duplicates.sh`**, after an edit to a `.resx`: reports keys that differ only in case, which
  MSB3568 fails the build on but only when the resources are regenerated.
- **`skill-gate.sh`** and **`comment-rule-reminder.sh`**, before an edit: the skill for the area, and
  the commenting rule.
- Bare `git stash` and `git stash pop` are denied: worktrees share one stash, so either can take
  another session's work. `git stash push -u -m <tag>` and `git stash apply <sha>` stay allowed.
