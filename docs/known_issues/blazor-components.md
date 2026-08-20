# Blazor components

- **`section` is a reserved word in a `.razor` file.** `@foreach (var section in Sections())` then
  `@section.Title` is parsed as the `@section` *directive*, not as a member access, and the build
  fails with `RZ2005: The 'section' directive must appear at the start of the line`. Name the
  variable anything else (`gameList` on `/games`), or parenthesise as `@(section.Title)`.
  **This one is SDK-dependent, which is the real trap:** it fails on the 10.0.110 SDK from Ubuntu's
  archive — the one `.claude/hooks/session-start.sh` installs — and compiled clean on the SDK
  `actions/setup-dotnet` resolved for `10.0.x` in CI, so a green check was not proof it built in a
  web session. `global.json` now pins 10.0.110 and `ci.yml` installs from that file, so the two
  agree. The gap was wider than a patch: `10.0.x` resolved to **10.0.302**, a different feature
  band. `rollForward` is `disable` on purpose — anything looser picks the *highest* installed 10.x,
  so a runner that preinstalls a newer SDK would quietly ignore the pin.
- **The SDK the pin cannot reach.** The pinned 10.0.110 exists only as a package: Microsoft
  publishes no container image for it (the newest 1xx tag on MCR is `10.0.103`), and
  `packages.microsoft.com` carries no .NET 10 for noble at all — on Ubuntu 24.04 it defers to
  Ubuntu's archive. So the three environments cannot all be locked to one build, and
  `.dockerignore` keeps `global.json` out of the image: copying it in would leave `sdk:10.0` unable
  to satisfy the pin and **every deploy would stop**. The deploy image therefore still publishes on
  whatever `sdk:10.0` currently is, as it always has — CI, not the image build, is the gate that
  ran the tests. Moving the pin means checking that MCR has an image for the new band first.
  A container build cannot be rehearsed from a web session: the Docker CLI is installed but no
  daemon runs.
- **A base class for a page goes in the `.razor`, not the code-behind**: putting
  `: SeasonAwarePage` on the `public partial class` gives *CS0263: Partial declarations must not
  specify different base classes*, because the generated Razor partial already declares
  `: ComponentBase`. Use `@inherits SeasonAwarePage` in the markup file.
- **A generic dialog result can't tell `default` from "cancelled"**: `PromptAsync<TDialog, TResult>`
  is constrained to `class` for that reason. A dialog closing with `0` is otherwise
  indistinguishable from the user pressing Cancel, so one returning a value type needs its own
  helper handing back `TValue?` — there was a `PromptValueAsync` doing exactly that until its last
  caller went, and adding another value-typed dialog means writing it again.

