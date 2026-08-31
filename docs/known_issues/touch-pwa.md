# Touch / PWA

- **A MudBlazor dialog is 64px narrower than the phone, and that breaks the date picker.**
  `.mud-dialog-width-full` is `calc(100% - 64px)`, so a 360px phone gets 296px of dialog and a
  320px one gets 256px. `MudDatePicker`'s popover has `min-width: 310px` and is anchored to the
  input's left edge, so it hung 9px off a 360px screen and **49px off a 320px one — the whole
  Saturday column was untappable**. Two rules in app.css fix it: `.dialog-sheet` makes a long form
  dialog full-screen below 600px, and the `.mud-picker-popover` block centres the calendar on the
  viewport instead of anchoring it. Give the popover an **explicit** width, never just a
  `max-width`: `.mud-picker-calendar` is a wrapping flex row whose min-content width is one 40px
  day cell, so a shrink-to-fit popover collapses the month into a single column.
- **Centring the calendar was only half of it — the day cells are 36px, and that is too small.**
  Reported twice: after the popover was centred, picking a day on a phone still misfired. MudBlazor's
  `.mud-day` is a 36px circle on a 40px column pitch, under Apple's 44px *and* Android's 48dp, and
  the 4px gutters between columns are **dead** — a tap landing there hits `.mud-picker-calendar` and
  does nothing. Worse, the popover stayed pinned to 310px on a sheet that is now the phone's full
  width, leaving live form controls exposed either side: with the picker open, `elementFromPoint`
  14px outside the calendar returned the season `MudSelect` underneath. `--dp-day` spends that width
  instead and drops the side margins so the column pitch *is* the target. The resulting sizes per
  viewport, and the elements that have to scale with them, are tabulated in
  [ui_components](../ui_components/dialogs-and-pickers.md#date-pickers-below-600px-or-560px-tall).
- **The month name between the arrows is a 23px button, and it was the worst target of the lot.**
  Reported as "it's mostly the month selection". It is a real `<button>` — the one that opens the
  month grid — that MudBlazor clamps to `height: 23px` because it doubles as the slide transition's
  viewport, sitting in the 56px row the two 44px arrows set. So there is **17px of dead div above it
  and 16px below**, and a thumb aimed at "augustus 2026" mostly lands in one of those. 44px fits that
  row with no layout change at all.
- **Buttons need clear space above them, not just their own size.** The game dialog's action row
  sat 28px under the last field, and `MudSelect`'s hit box reaches ~10px past its own underline —
  leaving 18px of dead space between "Annuleren" and the unavailable-players dropdown. Mobile
  browsers snap a tap that misses every target to the nearest one *by contact area*, and the
  select is the far bigger target, so a thumb aimed at the button opened the dropdown instead.
  This is invisible to `document.elementFromPoint`, which reports the button as reachable — the
  measurement that finds it is the **gap** to the nearest interactive element above.
- **All of the above is now measured, and the measuring found three more.** Everything in this
  section was CSS that nothing verified — a MudBlazor upgrade or one more global `.mud-*` rule would
  have undone any of it silently, which is how the day cells shipped twice. `scripts/visual-check.sh`
  now reopens the match dialog and its date picker at 320x568, 360x640 and 844x390 and fails on a
  target under 44x44 or a gap that is neither zero nor at least 8px (see
  [testing](../testing/visual-and-touch-checks.md#touch-targets)). Its first run reported: a **landscape phone got 36.5px
  action buttons**, because the `.dialog-sheet` block is width-only and 844px is not below 600px —
  the same geometry that was reported for "Annuleren" in the first place; **6px of dead space** on
  either side of the picker's month name, from MudBlazor's `margin: 6px` on the arrows, in the row
  this file already calls the worst of the picker; and a numeric field's **24x16 steppers**, stacked
  flush so a tap that misses one hits the other and steps the wrong way. All three are fixed in
  `app.css`. The lesson is the ordering: three fixes had been argued for in prose here for months,
  and the first thing that measured them found three more in an afternoon.
- **Widening that guard to `/games` found a card that had already run out of room.** The action
  buttons on a game card were 40px under `(pointer: coarse)`, set deliberately and with a comment
  saying a finger needs more than the 32px a mouse gets — four short of the floor, and separated by
  2px gutters. Size and clearance are the two rules, so both were flagged the moment the page came
  into scope. What the numbers did not say and the screenshot did is that the row had **already
  overflowed**: five 40px buttons and their gaps left 42px of a 320px card for the opponent's name,
  which wrapped underneath them and was drawn straight through them. And five is not the count —
  the Live button joins on the day of the match, and six 44px buttons are 264px, more than any
  phone can put beside anything at all. So below 600px the card is two lines: the match, then the
  actions on a line of their own, flush, at a fixed width and pushed to the card's right-hand edge
  (`Games.razor.css`). 44px each, which is why the card's horizontal padding drops from 16px to 12px
  there — 16px leaves 260px and six buttons need 264, while 12px leaves 268px.
- **Widening it again — to the chrome, the squad, the formation builder and the live screen — found
  five things sized by their own content rather than by `--action-btn-size`:** the app-bar title link
  (28px), the drawer's nav entries (40.5px), the sign-out button (28px, and styled inline so nothing
  could reach it), the top bar's own nav links in landscape (38.4px), and `.btn-ghost`, left out of
  the `min-height` rule its two sibling button classes were already in. All five are `app.css` now.
  The squad's action rows were the one part that was already right, which is `--action-btn-size`
  doing its job.
- **The scenes added for the pitch chips measured no pitch chip, and passed.** Both new scenes were
  written to measure `.pitch-player`, and `grep -c pitch-player` on the first report they produced
  was **zero**: the pitch is below the fold on all three viewports, and a target clipped out of the
  viewport is skipped rather than reported. Four targets per scene were measured — a back button, a
  button or two, an input control — the run printed "audited 13 screens", and the summary written off
  the back of it said the chips cleared 44px everywhere. They do not. Two fixes, and the second is
  the one that generalises: audit those pages from **both ends** the way the match dialog already
  was, and let `audit()` take the classes a scene must actually have measured, so a scene that
  measures nothing it exists for fails instead of passing. That guard immediately earned itself —
  it caught the live screen's Goal buttons sitting below the fold in landscape too.
- **The chips themselves are 28–41px, and that is geometry rather than an oversight.** Once measured:
  40.9px on the builder at 320, 34px on the live screen at 320, 38.1px at 360, and 28px in landscape,
  where `.pitch-constrained` caps the pitch at `65dvh` — a 190px-wide pitch, in which five chips
  across a back line at 44px would need 220px. `--chip-size` is `clamp(34px, 13cqw, 52px)` on a
  regular pitch and `clamp(28px, 15cqw, 44px)` on a compact one, and the positions are placed by
  percentage with the widest already at `left: 8%`, so raising the minimum overlaps them. Recorded
  floors, one per viewport, with those numbers — **and a recorded floor is still a floor**, so the
  run now fails if a chip drops below what the geometry achieves today. Whether the live screen
  should give up something else to make its chips thumb-sized is a design question this harness can
  now put numbers behind, which it could not before.
- **Splitting that row evenly made a button's width a function of the row's length.** It was
  `flex: 1` at first, so nothing was narrower than it had to be. But the row is four buttons on a
  fixture, five on a played match, six on match day and *one* on a card an anonymous visitor is
  looking at — so the same action sat somewhere different on every card in the list, and the
  visitor's lone Overview button rendered as a 312px bar that reads as a call to action rather than
  an icon. Fixed width and right-aligned instead: Delete is under the same thumb position on every
  card, and every other button counts in from the same edge.
  **The audit only ever sees six because `visual-check.mjs` seeds its game dated *today*.** Move
  that date and the widest row the app has quietly stops being measured, with nothing failing to
  say so.
- **Splitting it onto its own line then made the whole line swallow taps.** Reported from a phone:
  tapping a match card does nothing, "only when you tap directly on the name". The action row
  carried one `@onclick:stopPropagation` for all its buttons, which is free while the row is only as
  wide as they are — and below 600px it is the full width of the card. So every tap in the stretch
  to the left of the buttons hit that div and was stopped there: a fifth of the card for an admin on
  match day, and **most of it for a visitor**, who sees one button on a line 300px wide. Nothing is
  measurable about it — `elementFromPoint` reports the row, the buttons all clear 44px, and the
  screenshot shows empty space. Each button stops its own click now (`Games.razor`), so the empty
  stretch belongs to the card again. `/trainings` never had it: its action row is content-sized and
  right-aligned with `margin-left: auto`, and a margin is not part of the box that swallows a tap.
- **iOS centres a time field's value where `text-align` cannot reach it.** "Aanvangst" reads centred
  on a phone and left-aligned everywhere else, because WebKit draws the value inside
  `::-webkit-date-and-time-value` and centres it there — the rule on the input styles a box whose
  content it does not own. Chromium has no such pseudo-element, so the phone this was reported from
  is the only place it shows and the desktop capture proves nothing. Both rules are in app.css: the
  input's own for Firefox, the pseudo-element's for WebKit.
- **A width-only media query does not cover a phone.** Turned sideways, a 390px-tall phone is 844px
  wide and every `max-width: 599.98px` rule stops applying — while the thumb does not change size.
  The picker block keys off `(max-width: 599.98px), (max-height: 559.98px)` for exactly this reason,
  and anything about touch rather than layout belongs in that query, not the sheet's.
- **Do not leave two nested scroll containers in a dialog.** MudBlazor makes both `.mud-dialog`
  and `.mud-dialog-content` scrollable. A flick can move either one, and on iOS a tap landing
  during momentum scrolling resolves against wherever the other has since moved to. `.dialog-sheet`
  sets `overflow: hidden` on the dialog so the content is the only scroller.
- **`DialogOptions` has no `CssClass` in MudBlazor 9.7** — but `MudDialog`'s own `Class` lands
  straight on the `.mud-dialog` element, which is how `.dialog-sheet` gets there. No plumbing
  through `DialogPrompts` is needed.
- **Blazor silently drops drag events with null `dataTransfer`**: dispatching
  `new DragEvent('dragstart', {bubbles: true})` reaches DOM listeners but never the Blazor
  handler — its DragEventArgs serializer reads `dataTransfer.files/items/types` and gives up
  on null. Always attach `new DataTransfer()` (or a stub with those fields). Plain `Event`
  objects with a drag type name are ignored entirely. Cost hours; see `js/drag-drop-touch.js`.
- **HTML5 drag events never fire from touch input**: iOS Safari and Android Chrome require the
  shim in `wwwroot/js/drag-drop-touch.js`, plus `touch-action: none` on `[draggable="true"]`
  (in app.css) so the browser doesn't claim the gesture for scrolling.

- **White page after switching apps**: a suspended PWA loses its SignalR circuit. Two
  causes, both fixed: the page background came only from the MudBlazor theme (now also
  set statically on `html, body` in app.css via `var(--surface-page)`, plus
  `color-scheme: light`), and Blazor's stock reconnect overlay is light (now themed via
  `#components-reconnect-modal`, and `js/pwa.js` reloads the page once reconnection fails
  or on return to a dead tab).
  **The reload needs its own guard, or it is the next bug.** A page that serves while the circuit
  never connects — a blocked WebSocket, a dead network — would reload forever. `pwa.js` stamps
  `sessionStorage` and refuses to reload twice inside ten seconds, leaving the overlay up instead.
- **"Reconnecting..." for ten seconds on a circuit that was never gone.** Reported from the
  touchline: start a match, switch to another app, come back, and the overlay sits there. Nothing in
  this app was waiting — it is Blazor's default retry schedule meeting a phone that has just woken.
  The defaults (read them in `blazor.web.js`, they are not in the docs) are `maxRetries: 30` and
  `attempt < 10 ? 0 : attempt < 20 ? 5000 : 30000`: **the first ten attempts have no delay between
  them at all**. A phone coming out of suspension has no network for the first moment, so all ten
  are spent and failed in a fraction of a second, and every real reconnect therefore starts from
  the five-second bucket. One or two of those is the ten seconds. Blazor does cut a pending wait
  short on `visibilitychange`, which does not help here — the retries only start once you are
  already looking at the page. Fixed by the `Blazor.start` call in `App.razor` — one immediate
  attempt, then one a second — which is why that file loads `blazor.web.js` with
  `autostart="false"`: the schedule cannot be set any other way. **Keep that call inline.** In a
  file under `js/` it becomes a second thing the app's whole interactivity depends on arriving,
  and one that fails silently, exactly like the empty `blazor.web.js` two entries down.
  While you are in there: the retry callback is documented as receiving `maxRetries` as a second
  argument and is called with one, so a guard reading it is dead code — the retry loop applies the
  cap. And whatever the schedule is, keep its total near the retention period below. Blazor giving
  up is what marks the dialog failed, and a failed dialog is `pwa.js` reloading a page whose
  network may still be down — onto the browser's error page, where nothing retries at all.
- **Past the retention period there is no reconnect at all — it is a reload.** `ConnectCircuit`
  returns false for a circuit the server has already evicted, and with no persisted circuit state
  configured the client goes straight to the rejected state, i.e. a fresh page. The window is
  `CircuitOptions.DisconnectedCircuitRetentionPeriod`, three minutes by default and ten here, which
  is the difference between rejoining a running match screen and reloading it. What falls outside it
  whatever the setting is a restarted process, which has no circuits at all: a deploy, or a crash.
  **Going idle is no longer one of them.** `fly.toml` sets `auto_stop_machines = "suspend"`, and a
  suspended machine resumes from its saved memory with its retained circuits intact — where the
  `"stop"` this used to be threw them away and made every return from a backgrounded phone a
  rejected reconnect and a forced reload. Retention is bounded by
  `DisconnectedCircuitMaxRetained` as well as by time, and **lowering that to buy back the memory a
  longer window costs is a trap**: a slot given up is a circuit evicted early, and the coach's is as
  likely as anyone's. It stays at the stock 100. Little occupies it in practice — a tab closed
  properly sends a disconnect beacon and gives its circuit up immediately, so only unclean
  disconnects park at all.
- **Chromium's emulated `(pointer: coarse)` is easy to lose, and then a "phone" capture is the
  pointer layout at phone width.** Two ways it goes, both measured while screenshotting `/players`:
  opening the mobile context in a browser that already holds a desktop one, and taking a `fullPage`
  screenshot — after which `matchMedia('(pointer: coarse)')` reads false and every touch-sized rule
  is off, so 44px targets measure 32. Give the phone pass its own `chromium.launch`, and give it a
  viewport tall enough that the capture needs no `fullPage`. `scripts/touch-targets.mjs` is safe on
  both counts (its own contexts, no screenshots), which is why this only shows up in ad-hoc scripts.

