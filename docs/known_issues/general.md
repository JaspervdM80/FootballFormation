# General

- **A published app started from the wrong directory serves every static file as 200 with an empty
  body.** The content root of a published app is the *working directory*, which is why the
  Dockerfile sets `WORKDIR /app` before its entry point. Run
  `dotnet path/to/publish/FootballFormation.Web.dll` from anywhere else and it boots, `/health`
  answers healthy, the page renders complete and correct — and `blazor.web.js` comes back
  `Content-Length: 0`, so `window.Blazor` is never defined, no circuit connects, and nothing is
  interactive. There is no error anywhere: not in the app log, not in the browser console, not in
  the network panel, where every request is a green 200. It surfaces only as every `_bl_*` wait in
  the UI harnesses timing out. Both places that start a published app (`ci.yml`'s browser jobs, via
  `UI_TEST_APP_DLL` and `VISUAL_APP_DLL`) `cd` into the artifact first.
- **Editing a file in the publish output does not change what the app serves.** `MapStaticAssets`
  answers from the manifest baked at publish time — content length and ETag included — and reads the
  bytes off disk against it. Overwrite `publish/wwwroot/service-worker.js` with a shorter file and
  the next request dies with `System.ArgumentOutOfRangeException: (Parameter 'count')`; the
  behaviour for any other edit is equally undefined, because the response was described before the
  file was touched. This bites whenever a published app is the thing under test — `ci.yml`'s browser
  jobs run one, via `UI_TEST_APP_DLL` and `VISUAL_APP_DLL` — and it is tempting precisely because
  editing the output *looks* like the fast way to try a one-line change to a script or a stylesheet.
  It is not a shortcut, it is a different app. Republish.
- **`.count()` is the one Playwright query that does not wait, and it fails open.** Every other
  locator call in `tests/ui` retries until its timeout; `count()` answers from the DOM as it stands
  right now. `if (await dialog.count()) await confirmDialog(...)` therefore read zero before a
  MudBlazor dialog had rendered, skipped the confirmation entirely, and let the test carry on
  against a player who was never archived — green locally for months, red on a loaded runner. The
  guard was also unnecessary: `ToggleArchived` only skips the confirm when *restoring*. Prefer
  `openDialog()`, which asserts visibility and waits; reach for `count()` only to assert that
  something is *absent*, and even then `toHaveCount(0)` is the waiting version.
- **Waiting for a consequence is not waiting for the navigation it causes.** Changing the seeded
  admin's password rotates the security stamp, `OnValidatePrincipal` rejects the cookie issued
  before it, and the circuit navigates to `/login`. `visual-check.mjs` waited for the *notice* to
  clear, which happens when the component re-renders — earlier than the drop. Signing in on that
  signal starts a navigation while the circuit's own is still in flight, and Playwright abandons the
  new one: `Navigation to "/dev/login" is interrupted by another navigation to "/login"`, killing the
  run before its first screenshot. Wait on `page.waitForURL` for the landing. Any Blazor flow that
  ends in a server-driven redirect has this shape — the redirect is the thing to wait for, not the
  re-render that precedes it.
- **A retry does not get a clean database, so one flake can look like a hard failure.** `run.mjs`
  builds a single throwaway database per run, and Playwright's CI retry re-runs the test against
  whatever the failed attempt left behind. A test that creates a player and then fails will, on its
  retry, be adding that player a second time — `playerRow(...).first()` may match the wrong row, and
  the report says `1 failed` rather than `1 flaky`. Read a two-attempt failure as "flaked, then hit
  dirty state", not as proof the behaviour is genuinely broken.
- **`scripts/coverage.sh` passed on Windows no matter what the change did.** The Cobertura report
  the collector writes carries Windows separators — `<source>D:\...\src\FootballFormation.Core\</source>`
  and `filename="Services\GameService.cs"` — and `coverage.mjs` derived its `sourceRoot` from that
  `<source>` with `path.relative`, which keeps them. The paths it compares against come from
  `git diff`, which is always forward-slashed, so `path.startsWith(sourceRoot)` matched nothing:
  every changed file fell into "Not measured here", the script printed *Changed lines: none
  measurable in Core* and exited PASS. A green local run that judged nothing, and invisible in CI
  because that runs on Linux, where the two spellings happen to agree. Both the `sourceRoot` and the
  `filename` keys are normalised to `/` now. Any script that compares a path from a .NET tool with a
  path from git has this shape — normalise before comparing.
- **The clipboard hands text back with the platform's line endings, not the app's.**
  `MatchSummaryTextBuilder.Build` joins on `'\n'`, but on Windows the clipboard stores the text as
  CRLF and `navigator.clipboard.readText()` returns what the platform stored. `match-summary.spec.js`
  split the copied summary on `'\n'` and looked the half-time break up with an exact-match
  `indexOf`, so every line carried a trailing `\r`, the break was on file as `'———————————\r'`, and
  the search found nothing — a red run on a developer's Windows box against text that was correct,
  green on the `ubuntu-latest` runner where the round-trip is LF in and LF out. The same `\r` also
  defeated the `line.length > 0` filter, since a blank line arrives as `'\r'`. Split on `/\r?\n/`.
  Substring assertions (`toContain`) are unaffected; only a line-by-line read of clipboard text has
  this shape.
