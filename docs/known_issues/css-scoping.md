# CSS scoping

- **A class used on a page that doesn't own its `.razor.css` silently does nothing.** Scoped CSS
  compiles to `.foo[b-<ownerHash>]`, so `.action-btn` defined in `Games.razor.css` never matched
  the identical markup on `/settings` — those buttons rendered as native browser chrome for as
  long as nobody looked. There is no warning. Anything more than one page uses goes in `app.css`;
  `.action-btn`, `.badge-*`, `.stat-tile*` and `.stacked-table` are there for this reason.
- **The same trap catches a rule that never leaves its own page: a child component's root element
  has no scope attribute either.** `.live-control-row > *` sat in `LiveMatch.razor.css` and matched
  nothing, because every child of that row is a `MudButton` and the `<button>` MudBlazor renders
  carries no `b-<hash>`. The row looked deliberate and read as flex — the buttons simply never took
  the width or the height it asked for, which is what "the buttons don't fill the box" turned out
  to be. The tell: the *container* is styled and the *children* are not. Anything selecting past a
  MudBlazor component's root goes in `app.css`, next to `.live-scoreboard` and `.live-action-btn`,
  which are there for the same reason.
- **A global rule and a page's own class sharing a name is the same silence from the other side.**
  `.overview-capture` named both the block being screenshotted on `/games/{id}/overview` and, in
  `app.css`, the plain `<button>`s under it — so the button rule's `display: inline-flex` landed on
  the capture block too and laid its header out *beside* the pitches instead of above them. The
  scoped rule could not win: it never declared `display` at all. A class in `app.css` is global;
  read it as one before reusing a name a page already uses.
- **html2canvas 1.4.1 throws on `color-mix()`.** Chrome resolves a mix to `color(srgb r g b / a)`,
  and the parser rejects any colour function it does not know rather than skipping the value — so
  one derived shade anywhere under the captured element is enough to fail the whole export, which
  is what "save as image" stopped doing when the light theme spread `color-mix` across the app.
  `js/screenshot.js` flattens those to `rgba()` on the live DOM before the capture and puts the
  styles back afterwards; the clone html2canvas offers is built too late to fix them in.
