# Visual and Touch Checks

## Visual checks

`scripts/visual-check.sh` boots the app and screenshots every page into `artifacts/visual/`
(ignored by git). It builds, starts the app on a **throwaway database** in a temp directory, signs
in, seeds a small squad through the real dialogs, and captures each page at 1440×900. It exits
non-zero if the browser logged an error, which is where a Blazor render failure shows up, or if a
touch target is under its floor.

Setting `VISUAL_APP_DLL` to a published `FootballFormation.Web.dll` skips the build and runs that
copy instead — which is how the `Visual check` job does it, against what `Build and test` published.

It runs on every pull request as the `visual` job in `ci.yml` — required, like the Playwright job
beside it, so a page that stops rendering stops the merge. That job uploads `artifacts/visual/`
whether it passed or not: the
measurements are the part that can fail, but the screenshots are worth a look on a pull request that
changed a page, and nothing else in CI produces one. Locally the harness drives the Chromium in a
Claude Code web container; everywhere else `visual-check.mjs` lets Playwright resolve its own, which
is what makes the job possible at all.

Two things it has to work around, both of them the app behaving correctly:

- A fresh admin still holds the password it was seeded with, and that **locks every route to
  `/settings`** until it changes. Without that step every screenshot is the same page.
- Changing a password **rotates the security stamp**, which invalidates the cookie issued before
  it. The script signs in again afterwards, or it would browse as an anonymous visitor.

It signs in through `/dev/login` — mapped only outside Production and only for loopback callers —
so no password is typed into the login form.

## Touch targets

The same run then stops looking and starts measuring. `scripts/touch-targets.mjs` reopens the app
in three phone-sized touch contexts — **320×568**, **360×640** and **844×390** landscape, the sizes
[known_issues](../known_issues/index.md) argues from — and walks the games list, the new-match dialog and
its date picker: `/games` as it renders, then the form at the top and scrolled to the bottom, then
the picker's day, month and year views. Six screens per size, screenshotted into
`artifacts/visual/touch/` with every measurement written to `report.md` beside them.

It exists because `../known_issues/touch-pwa.md` is the longest section in `docs/known_issues/`, every
entry in it was reported from a touchline — twice — and all of them are held in place by CSS that
nothing verified. Two rules:

- **Size.** Every hit-testable element is at least **44×44** CSS px. That is what a 36px day cell,
  a 23px month name, a 40px year button and a 36.5px "Annuleren" each failed.
- **Clearance.** The gap between a target and its nearest neighbour above or beside it is either
  nothing — they meet, so there is no hole — or at least **8px**. Anything between is a dead
  gutter: too narrow to see or aim around, wide enough to swallow a tap, and awarded by the browser
  to whichever neighbour has the larger contact area. This is the measurement `elementFromPoint`
  cannot make; it reports both neighbours as perfectly reachable. The 4px gutters between
  MudBlazor's day cells were exactly this.

Together they are the column-pitch guarantee: with no dead gutter left, the distance between two
column centres *is* the cell's own width.

Where the geometry provably cannot reach 44px the number is written down in `RECORDED_FLOORS` with
the reason — a 320px phone has 308px of usable width for seven columns, and a landscape phone is
too short for six 44px rows. **A recorded floor is still a floor**: the run fails if the element
drops below the number recorded for it, so an allowance cannot quietly become a regression.

Two things the measurement deliberately skips. A target only half inside its scroll container is
not a small target — scroll it back and it is the size it always was — so the size check waits
until it is whole. And two targets in different scroll containers are however far apart the scroll
position leaves them, which is not a fact about the layout, so no clearance is reported across that
boundary.

Adding a screen is a few lines in `auditTouchTargets`. The dialog and the picker were covered first
because every entry in that doc section was found in one of them; `/games` came next because it is
the page a phone opens most and the action row on a game card is the densest cluster of targets in
the app. Everything else a thumb touches — the live screen, the formation builder, the app bar and
drawer, `/players` — is still unmeasured, and is worth adding a scene at a time so each finding is
argued on its own.

A scene is only as good as what is on the page when it runs, and that is the seeding's job, not the
audit's. The `/games` scene needs a game card to measure, so `visual-check.mjs` creates one through
the dialog before the screenshots — **dated today**, because the Live button appears on match day
only and that is the day the action row carries six buttons rather than five. Each scene is scoped
to a root selector (`.app-main` for `/games`, the dialog and the popover for the rest), which is
how a page's own targets are measured without the app bar and the install banner arriving with
them.

