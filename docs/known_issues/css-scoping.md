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
- **It happened again, on the notification opt-in — and this time nothing measured the page.** The
  `min-height: 44px` and the phone-width `width: 100%` for the "Aan/Uit" button were written in
  `Home.razor.css`, so they compiled to `.home-notify-button[b-mskvn728zw]` and never reached the
  `<button>` MudBlazor renders. The button sat at MudBlazor's own ~36.5px, under the touch floor, and
  stayed beside the text on a phone instead of taking its own row. What let it through is the second
  half of the lesson: **`scripts/touch-targets.mjs` had no home-page scene at all**, so the harness
  that exists to catch exactly this reported "every touch target clears its floor" while measuring a
  page the button is not on. A `home` scene was added with the fix; it now measures 260.4x44 at
  320px. When a rule lands on a MudBlazor root, move it to `app.css` **and** check the page is
  actually audited.
