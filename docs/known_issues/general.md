# General

- **A published app started from the wrong directory serves every static file as 200 with an empty
  body.** The content root of a published app is the *working directory*, which is why the
  Dockerfile sets `WORKDIR /app` before its entry point. Run
  `dotnet path/to/publish/FootballFormation.Web.dll` from anywhere else and it boots, `/health`
  answers healthy, the page renders complete and correct — and `blazor.web.js` comes back
  `Content-Length: 0`, so `window.Blazor` is never defined, no circuit connects, and nothing is
  interactive. There is no error anywhere: not in the app log, not in the browser console, not in
  the network panel, where every request is a green 200. It surfaces only as every `_bl_*` wait in
  the UI harness timing out. The place that starts a published app (`ci.yml`'s `Playwright` job, via
  `UI_TEST_APP_DLL`) `cd`s into the artifact first.
- **`.count()` is the one Playwright query that does not wait, and it fails open.** Every other
  locator call in `tests/ui` retries until its timeout; `count()` answers from the DOM as it stands
  right now. `if (await dialog.count()) await confirmDialog(...)` therefore read zero before a
  MudBlazor dialog had rendered, skipped the confirmation entirely, and let the test carry on
  against a player who was never archived — green locally for months, red on a loaded runner. The
  guard was also unnecessary: `ToggleArchived` only skips the confirm when *restoring*. Prefer
  `openDialog()`, which asserts visibility and waits; reach for `count()` only to assert that
  something is *absent*, and even then `toHaveCount(0)` is the waiting version.
- **A retry does not get a clean database, so one flake can look like a hard failure.** `run.mjs`
  builds a single throwaway database per run, and Playwright's CI retry re-runs the test against
  whatever the failed attempt left behind. A test that creates a player and then fails will, on its
  retry, be adding that player a second time — `playerRow(...).first()` may match the wrong row, and
  the report says `1 failed` rather than `1 flaky`. Read a two-attempt failure as "flaked, then hit
  dirty state", not as proof the behaviour is genuinely broken.
