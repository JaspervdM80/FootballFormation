# Blazor components

- **`section` is a reserved word in a `.razor` file.** `@foreach (var section in Sections())` then
  `@section.Title` is parsed as the `@section` *directive*, not as a member access, and the build
  fails with `RZ2005: The 'section' directive must appear at the start of the line`. Name the
  variable anything else (`gameList` on `/games`), or parenthesise as `@(section.Title)`.
  **This one is SDK-dependent, which is the real trap:** it fails on the 1xx SDK from Ubuntu's
  archive — the one `.claude/hooks/session-start.sh` installs — and compiled clean on the SDK
  `actions/setup-dotnet` resolved for `10.0.x` in CI, so a green check was not proof it built in a
  web session. `global.json` pins the band and `ci.yml` installs from that file, so the two agree.
  The gap was wider than a patch: `10.0.x` resolved to **10.0.302**, a different *feature band* —
  the digit group in `10.0.1xx`, which is where the incompatibility lived.
- **The pin guards the band, not the patch, and that is deliberate.** `rollForward` is
  `latestPatch`: a newer patch inside the pinned band satisfies it, a different band does not.
  Both halves are load-bearing and both are worth knowing:
  - **It has to tolerate a patch bump.** Ubuntu's archive carries exactly one `dotnet-sdk-10.0` at
    a time and moves it, and a web container can reach no other source — so an exact pin becomes
    unsatisfiable the moment the archive steps forward, and every web session is dead until someone
    edits this file. That is not hypothetical; it is what 10.0.110 → 10.0.111 did.
  - **It must not tolerate a band change.** `disable` was the original setting for a good reason —
    anything looser picks the *highest* installed SDK — but `latestPatch` will not cross a band, so
    a runner preinstalling a 3xx cannot quietly satisfy a 1xx pin. Verified: against an installed
    10.0.111, a pin of `10.0.100` resolves and one of `10.0.200` or `10.0.302` is refused.
- **The SDK the pin cannot reach.** The pinned band exists only as a package: Microsoft publishes
  no container image for it (the newest 1xx tag on MCR is `10.0.103`), and `packages.microsoft.com`
  carries no .NET 10 for noble at all — on Ubuntu 24.04 it defers to Ubuntu's archive. So the three
  environments cannot all be locked to one build, and `.dockerignore` keeps `global.json` out of the
  image: copying it in would leave `sdk:10.0` unable to satisfy the pin and **every deploy would
  stop**. The deploy image therefore still publishes on whatever `sdk:10.0` currently is, as it
  always has — CI, not the image build, is the gate that ran the tests. **So the pin buys
  CI ↔ web-container agreement, never CI ↔ production agreement.** Moving the *band* means checking
  that MCR has an image for the new one first. A container build cannot be rehearsed from a web
  session: the Docker CLI is installed but no daemon runs.
- **A base class for a page goes in the `.razor`, not the code-behind**: putting
  `: SeasonAwarePage` on the `public partial class` gives *CS0263: Partial declarations must not
  specify different base classes*, because the generated Razor partial already declares
  `: ComponentBase`. Use `@inherits SeasonAwarePage` in the markup file.
- **Enhanced navigation sends the destination as the `Referer`, not the page being left.** Blazor
  pushes the new URL into history *before* it fetches it, so the fetch's referrer is the document
  URL it has already changed: request `/players/3/stats` from `/trainings` and the header reads
  `/players/3/stats`. Every in-app link is an enhanced navigation, so **the whole of the back
  arrow's trail was dead** — `NavigationTrail` compared the referrer against the current path, found
  them equal, and fell through to the page's `Fallback` every time. It read as a wrong destination
  rather than as no destination ("training → player → back went to the squad"), and it was invisible
  wherever the fallback happened to be where the visitor came from. Only a link opting out with
  `data-enhance-nav="false"` — or a bookmark, or a refresh — ever sent a true referrer, which is why
  it looked like it worked. The trail is the `ff.trail` cookie now, written by a middleware in
  `Program.cs`; see [patterns](../patterns/ui-state-and-navigation.md#ui-state-services). Anything
  else reaching for `Referer` in this app is wrong for the same reason.
- **A generic dialog result can't tell `default` from "cancelled"**: `PromptAsync<TDialog, TResult>`
  is constrained to `class` for that reason. A dialog closing with `0` is otherwise
  indistinguishable from the user pressing Cancel, so one returning a value type needs its own
  helper handing back `TValue?` — there was a `PromptValueAsync` doing exactly that until its last
  caller went, and adding another value-typed dialog means writing it again.

