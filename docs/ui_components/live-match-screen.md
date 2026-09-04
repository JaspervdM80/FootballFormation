# Live Match Screen

## Live match screen (`/games/{id}/live`)
Phone-first single column (`max-width: 560px`), no `[Authorize]`: admin drives it, everyone else
watches the same URL read-only. Every control sits in an `<AuthorizeView Roles="@AppRoles.Admin">`.

- **The heading is the opponent's name alone**, with no `vs`/`@` in front of it: the subtitle under
  it already spells the venue out in words ("Thuis" / "Uit"), and the scoreboard right below puts
  the two sides in the order the ground decides. The prefix said the same thing a third time, in
  punctuation. The other screens that show a fixture (`Home`, `FormationBuilder`,
  `FormationOverview`, `MatchResult`, `PlayerStats`) still carry it.
- **It injects four services, one per thing it does**: `LiveMatchService` to read the match,
  `MatchClockService` for the clock buttons, `MatchGoalService` for the goal dialogs and
  `MatchSubstitutionService` for the pitch taps. That is the intended shape — see
  [patterns](../patterns/service-structure.md#when-a-service-gets-long-split-it-by-use-case--not-into-layers).

- **The clock never round-trips.** A per-circuit 1-second `System.Timers.Timer` re-renders
  `Game.ElapsedSecondsAt(DateTime.UtcNow)` from the anchor the server stored, and it repaints only
  while the clock is running. See [models](../models/game.md#game).
- **Spectators are pushed to via the singleton `LiveMatchNotifier`.** No service raises it by hand:
  `LiveMatchOperation` — the shape every touchline write runs inside — does it after a successful
  one, naming the game that changed. The page filters on its own `GameId`, reloads and
  `InvokeAsync(StateHasChanged)`, and unsubscribes in `Dispose`. In-process only — fine for the
  single Fly.io instance, but it needs a backplane if the app is ever scaled out.
- **`GetLiveAsync` is `AsNoTrackingWithIdentityResolution`, and it has to be.** A spectator's
  circuit keeps one scoped `AppDbContext` for its whole life, so a tracked `Game` keeps returning
  the score, clock and state from its first load while newly inserted goals appear alongside them —
  a live screen stuck at the old scoreline. Identity resolution keeps shared `Player` rows single.
- **This screen knows halves and nothing else.** A quarters game is two halves with two line-ups
  each, and the second line-up of a half is a plan the coach works through by hand on the pitch —
  nothing rolls it on, the clock never stops for it, and it is never a stage of the match here. So
  the controls are the same for both splits: "Half time" (`EndHalfAsync`) while a half is being
  played and one is still to come, then "Start 2nd half" (`StartNextHalfAsync`), and "Finish match"
  throughout. `Game.NextHalf()` is what skips the quarter left behind inside a half already played,
  so the second half opens at Q3 and Q2 is never kicked off — a line-up with no timings costs
  `GameMinutesReport` nothing, which is what leaves the whole half credited to the line-up that
  actually played it plus the substitutions made during it.
- **There is no pause.** The clock runs from kick-off until the half is whistled off, and only half
  time stops it — `PauseClockAsync`/`ResumeClockAsync` are gone from `MatchClockService` too, not
  just from the screen. A youth match is not paused at the touchline, and a clock a stray tap can
  stop is a clock the season's minutes cannot be trusted from. So a half being played always has a
  running clock, which is why the status chip's third state is half time (`.live-status-break`)
  rather than a paused one.
- **The controls are a two-column grid, laid out in `app.css`.** How many buttons the panel holds
  depends on where the match is, so equal columns keep them the same size whichever set is showing,
  and `:last-child:nth-child(odd)` spans the odd one out across the row. It has to be `app.css`:
  the children are `MudButton`s, and the old scoped `.live-control-row > *` rule never matched
  them — the classic CSS-isolation miss, and why they used not to fill the panel.
- **The clock controls sit at the foot of the page, the scoring buttons near the top.** "Half time",
  "Start 2nd half", "Finish match" and "Edit result" are pressed once each; "Goal" and "Goal against"
  are pressed all match, so the order follows how often a thumb reaches for them rather than the
  order the match runs in. Kick-off is the exception and keeps its place under the scoreboard: before
  it there is nothing else on the page to do. On a phone `.live-controls-foot` needs `order: 3` to
  stay last, because `.live-lineup` and `.live-minutes-card` are already reordered past source order.
- **The scoring buttons show only while the match is in progress.** Nothing is scored before kick-off
  or after the final whistle; a goal missed at the time is added back on `/result`, which is where a
  finished match is corrected and where the "Edit result" button leads.
- The pitch shows the half being played; at half time and after full time the last one played, and
  before kick-off the half the match opens with — so it is never blank when a lineup exists. The
  bench strip under it is always drawn.
- **Tapping a player offers two changes, one dropdown each** (`LiveSubDialog`): someone comes on for
  them (`SubstituteAsync`), or they trade positions with a team-mate who stays on
  (`SwapPositionsAsync`). Choosing in either list clears the other, so the single action button
  always has exactly one change to make and says which — "Make substitution" or "Swap positions".
  A third control, the **"Injured" switch**, is not a third change: it says *why* she is going off,
  so the "Comes on" list above still names her replacement and the button reads "Off injured"
  (`MarkInjuredAsync`). It is the one way the dialog closes with nobody named — a bench with nothing
  left on it, and the team plays a player short. Turning it on clears the swap; picking a swap
  clears it.
  A position swap writes no `GameSubstitution`: nobody's minutes changed, and a row there would say
  they did. The price is the *split by position* — `GameMinutesReport` reads the lineup as it finally
  stands, so after a swap the whole half is credited to the position each player moved **into**
  (pinned by `A_position_change_with_no_substitution_credits_the_position_it_ended_in`). Totals are
  unaffected. Undoing a substitution therefore follows the slot rather than the recorded one: a swap
  can have moved it since, and handing the recorded slot back would seat two players in it.
  Each select's `Placeholder` is set **only** when its list is empty — MudSelect shows a
  placeholder whenever nothing is chosen, so a standing "nobody is on the bench" greets a full bench.
- **Every goal on the timeline carries the score it made it** (`ScoreProgressionReport`), in the
  scoreboard's order — home side first. It is counted forwards over the whole match and looked up
  by goal id, because the timeline itself runs newest first and a total accumulated while rendering
  would count down.
- **Events are shown as a `MatchMinute` — 35, or 35+2 in stoppage time — and ordered on the elapsed
  match clock.** The two are different scales and that is the point: the scoreboard reading stops at
  the end of the half, so a goal two minutes into first-half stoppage and one just after the restart
  both read in the thirties, while the elapsed clock runs on across the break and puts them in the
  order they happened without anyone comparing pairs. Neither kind of event stores the minute it
  displays: a goal carries `GamePeriodId` + `AtSeconds` exactly as a substitution does, and
  `MatchClockReport.MinuteOf` derives the reading from the half's own timings. The timeline, the
  result page's goal list and `ScoreProgressionReport` all sort on elapsed seconds
  (`MatchClockReport.ElapsedOf`), then `RecordedAt`, then the id. A goal typed in on `/result` has
  only a scoreboard minute, and `ElapsedOf` converts it back through the half timings rather than
  reading it as elapsed time — the two scales part company by however long a half over-ran, and
  taking one for the other puts a second-half goal under the half-time rule.
- **Half time is a dashed rule across the timeline** (`.live-event-break`), not an event. The list
  runs newest first, so it lands where the second half's entries give way to the first's;
  `MatchClockReport.HalfOf` decides which side an entry is on, from its own line-up's half or —
  for a goal typed in by hand — from which side of the second half's kick-off its clock reading
  falls. `LiveMatch.Timeline` marks the one entry it is drawn above, because the markup renders an
  entry at a time and cannot see its neighbour, and because the substitutions filter decides who
  the neighbours are.
- **An injury is one entry on the timeline, not two** (`.live-event-injury`, a red
  `MedicalServices` cross). A substitution made for one takes the cross instead of the swap arrows,
  and only the injuries nobody came on for get a line of their own — `Game.WasReplaced` is the
  filter. Undo follows the same pairing: undoing the substitution removes the injury with it, and
  undoing a standalone injury puts her back in the slot she left. Injuries are never folded away by
  the substitutions checkbox below — it is the one change on the list that outlives the match.
- A **"Show substitutions" checkbox** (`.live-timeline-toggle`) drops the substitutions from the
  timeline and leaves the goals: a rotated squad buries the goals among swaps nobody is scrolling
  back for. The state is per circuit and deliberately not stored. It rides the card's heading row
  (`.live-card-head`) rather than sitting above the list, at the size of a caption — it is a setting
  for the list, not the first entry in it. The label is sized in `app.css` on
  `.mud-typography`: MudBlazor renders it as its own `body1` element, which inherits no font-size
  from the wrapper.
- Finishing asks for confirmation via `DialogPrompts.ConfirmAsync` (not `ConfirmDeleteAsync`,
  whose button says "Delete").
- **The plan for the middle of a half is a pop-up, not part of the screen.** The line-up card's
  heading carries a `Changes (n)` button (`.live-card-head`, `.live-plan-btn` in `app.css`) that
  opens `PlannedChangesDialog`; the dialog renders what `PlannedChangesReport` makes of the
  difference between the two planned line-ups (`PlannedChangesList`, which owns the `.planned-*`
  styling). Each line is carried out by tapping that player on the pitch — the dialog writes
  nothing. Admin only, like the minutes table.
  <br>A pop-up rather than a card because the plan is not the match: standing beside the live
  line-up it reads as the state of play, and a shared screen invites being asked about a change
  before it is made. Behind a button it is looked up, acted on and dismissed, and the count on the
  button says whether opening it is worth the tap. It is there before kick-off too, as something to
  read; nothing left to change means no button.
- **Only viable changes are listed.** The report is handed the substitutions already made in the
  half so it can rewind to the line-up that kicked off. A swap whose outgoing player has since
  been taken off is dropped: the difference between the line-ups still names their slot, but it now
  proposes withdrawing whoever came on for them, which nobody planned. An injury replacement
  therefore stays on for the rest of the half rather than being listed to come straight back off.
- **No line-up means no line-up card and no minutes card.** Both are left out entirely rather than
  headed over an empty pitch or an empty table — a match nobody has been picked for is sent to the
  formation screens by the buttons on `/games`, and two cards repeating "build one first" only push
  the scoreboard and the timeline down the phone screen the coach is actually reading. The heading
  is the first thing either card can say, so there is nothing to keep on screen once the body is
  empty.
- **Minutes played is admin-only** (`LiveMinutesReport`), and shows exact time on the pitch rather
  than the `periodsPlaying × periodDuration` estimate the planning screens use. It is a computed
  property, so the running player's total climbs with the clock tick. Until the first kick-off
  there is no time played at all and the figures are the planned line-up costed at a full period
  each, so the card is headed **"Planned minutes"** rather than "Minutes played"
  (`Game.HasActualTimings`) — the numbers cannot say which they are, so the heading does.
- **Mobile reorders the column with flex `order`**: what just happened matters more at a touchline
  than where everyone stands, so the line-up card (`.live-lineup`, `order: 1`) and the minutes
  table (`.live-minutes-card`, `order: 2`) drop below the timeline under 600px. Both rules live in
  `app.css` — the classes sit on `MudPaper` roots, which scoped CSS cannot reach.
- Goal and assist selects bind `int?`, not `int`: an `int` binds to 0, which is nobody's id but
  still renders as a chosen value, so the scorer field looked pre-filled.
- **`/games` is two lists, not one** (`Games.Sections()`): fixtures still to play, soonest first,
  then results newest first — each list leads with the match you came to look at. A single list has
  to put one of them at the wrong end, and newest-first throughout put the *most distant* fixture at
  the top. The split is
  on `Game.HasFinalScore`, not on the date: a game stays a fixture until a result is on file. A match
  that was never played therefore sits in the fixture list after its date has passed, which is
  intended — the only thing to do with one is delete it, and the stale row is the prompt. Because
  `HasFinalScore` tests `MatchState` too, a game being played now stays among the fixtures instead
  of crossing over on its first goal. Either block disappears when empty.
- **The Live button leads the action row and is always the crest red** (`.action-live`), which no
  other action on a card wears — the leading position and the one colour are what a coach hits
  without reading. `.action-live-now` adds the pulse, and only a match actually under way carries
  it: **paint and state are separate classes**, because the first version put the red on the
  in-progress class alone and the button everyone actually sees — on the day, before kick-off —
  rendered grey.
- `/games` routes an `InProgress` game to `/live` for **everyone**, whatever the calendar says,
  since a match kicked off before midnight is still being played. For other games the Live action is
  admin-only **and match-day only** (`Games.IsMatchDay`, i.e. `game.Date.Date == Today`): the live
  screen runs a real clock and writes real substitution timings, so opening it on a fixture weeks
  out would bank minutes against a match nobody is playing. It disappears entirely once
  `game.HasFinalScore` — a settled game has nothing left to run, so the Result button is the
  way in and a row click opens `/result`.
- **A fixture in the future carries no Result button** (`Games.IsFuture`, i.e.
  `game.Date.Date > Today`). There is no result to read and none to enter, and a score typed onto a
  match nobody has played turns a fixture into a result — `Sections()` splits on the scoreline. The
  page is not the enforcement: `MatchResult` applies the same rule, so an admin who arrives at
  `/games/{id}/result` by URL gets the score read-only, no **Save Score** and no add-goal form
  (a goal is a scoreline by another route — `AddGoalAsync` recounts it), under a line saying the
  match has not been played yet.
- **The action row is a card of its own below 600px.** A game card carries four `.action-btn` icons
  on a fixture and six on match day when Live joins them, and on a touch screen those are 44px each
  — 264px, which no phone has to spare beside an opponent's name. So `Games.razor.css` wraps the
  row onto its own full-width line under the match, at a fixed width and right-aligned, flush
  against each other: a gap between two touch targets has to be nothing or at least 8px, and there
  is nowhere to find five 8px gaps. Fixed rather than split evenly because the row's length varies
  with both the game's state and who is looking — see [known_issues](../known_issues/index.md). The
  card's horizontal padding drops to 12px there so the six still clear 44px on a 320px phone.
  `scripts/touch-targets.mjs` measures all of it — see [testing](../testing/visual-and-touch-checks.md#touch-targets).
- **Each of those buttons stops its own click.** One `@onclick:stopPropagation` on the row around
  them costs nothing while that row hugs its buttons — and at phone width it is the whole card, so
  it swallowed every tap in the empty stretch beside them. The card's own click, which opens the
  match, is what those taps were meant for. See [known_issues](../known_issues/touch-pwa.md).
- **The venue is a word, at every width.** A `<VenueBadge Inline="true" />` trails the opponent's
  name and spells out *THUIS*/*UIT*, in the same green and blue the card's edge stripe uses. The
  stripe had been saying it alone, which is a convention nobody reads off a stripe; now the colour
  and the word are one signal. See [the badge](#venue-badge-componentsvenuebadgerazor) below.
- The page reads "today" from the injected `TimeProvider`, not `DateTime.Today`, the same way the
  services do — that is also what `IsIncomplete` (the missing-lineup flag) compares against.
- `Game.HasFinalScore` checks `MatchState` **as well as** the score fields, and must:
  `MatchGoalService` writes `ScoreHome`/`ScoreAway` on every goal, so a score alone only means the
  game has started. Testing the score by itself would hide the Live button on the very match being
  played. It lives on `Game` rather than as a page-local helper because `MatchResult` and
  `FormationOverview` need the same test to decide whether there is a result worth copying.
- **The copyable match summary** (`MatchSummaryReport` in `Core/Reporting`, composed into text by
  `MatchSummaryTextBuilder` in `UI/Helpers`) is offered on both `/result` and `/overview` once
  `game.HasFinalScore` — a 📆 date line, the scoreline in venue order, our own goals with their
  assist on one line, and any **public** comment. No half-time score: the paste is a group-chat
  message, not a report. Where two consecutive goals cross half time, a plain-character dashed
  break stands in for the live timeline's own rule — see `MatchSummaryGoal.Half` and
  `MatchSummaryTextBuilder.GoalLines` — but a break with no goal on one side of it stays silent.
  Never gated on admin-ness: sharing the result is the point for whoever just watched the match, and the anonymous
  overview page already shows the same public comments as text — `includePrivate: false` is passed
  unconditionally there, same as everywhere else a visitor reads a comment. Both pages render the
  composed text into a hidden `<pre>` and copy it from a plain `onclick` into `js/clipboard.js`
  rather than a Blazor click handler, even on `/result` which has a circuit:
  `navigator.clipboard.writeText` only runs inside the task the user's click gesture produced, and a
  round trip through server interop loses that gesture on iOS Safari and Firefox.
- **The copyable match-day message** (`MatchInfoTextBuilder` in `UI/Helpers`) is the other half of
  that button, and the two never appear together: `/overview` offers this one while
  `game.HasFinalScore` is false and the summary above once it is true — before the match what the
  group chat needs is where to be, afterwards it is the score. It composes the fixture, a 📅 date, the
  meet/warm-up/kick-off times, the field, dressing room, sports park and town, and a **Duties:** block
  naming who has the dressing room (🧹), the flags (🚩) and the kit wash (🧺). Every one of those is
  optional, and `AddGroup` drops a whole group's blank separator with it when nothing in it was filled
  in, so a fixture carrying only a departure time is three lines rather than a form with holes in it.
  **The field and the dressing room are stored as the designation alone** and the message writes the
  word in front — `L["field {0}"]` and `L["dressing room {0}"]` — because the dialog already labels
  both, and a coach typing "Veld 3" under a field marked *Veld* got it twice. The dialog's
  placeholders are what ask for the designation alone; a row filled in before they existed reads
  "veld Veld 3" until someone retypes it. Our own side is named from `TeamState.Current?.FullName`,
  falling back to `L["Us"]` before a club is seeded — `TeamState` because the chrome on this page has
  already loaded it in this scope, and it is the one place a failure to name our own side is
  swallowed rather than read off `Result.Value`, which throws. Same hidden `<pre>` plus
  `js/clipboard.js` mechanism as the summary, and public for the same reason — the arrangements are
  for whoever is coming to the match.
- **A kick-off time is optional and lives in `Date`'s time component**, not a separate column —
  `Game.HasStartTime` is the test, `GameDialog`'s "Kick-off Time" field is how it is set, and
  `Game.DateLine(format)` is the one place the result page, the overview and the copyable summary
  compose the date-plus-time line.
- **All three time fields are plain `MudTextField`s on a 24-hour clock**, and neither a picker nor an
  `InputType.Time`. Not a picker for the reason the date one nearly was not — see the
  responsive-and-touch skill on `MudDatePicker`'s popover traps. Not a native time input because the
  browser draws that one in **the browser's own UI language**, not the page's: the `lang` attribute
  and the app's culture are both ignored, so a club member on an English phone got "10:45 AM" out of
  a Dutch app. Owning the format means owning the parsing too, and that lives in **`ClockText`, in
  `Core/Models`** rather than in the dialog — it is pure string logic, the Razor project is not
  measured by `scripts/coverage.sh`, and this is the code that shipped the `01:04` bug below.
  `Normalize` settles the text on blur (`1045`, `930`, `9:30` and `10:45` all land on the `HH:mm`
  form), `Parse` reads it back with `TimeOnly.TryParseExact` so `25:00` is refused rather than taken
  as a duration the way `TimeSpan.TryParse` would, and text neither can read is left exactly as
  typed for the field's validation to report. **Only a run of bare digits is reshaped**: a half-typed
  `10:4` already carries its separator, and reading a shape off its digits alone would settle it
  silently on `01:04` — a different time, where what the reader wanted was the error.
  The placeholder reads `L["e.g. {0}"]` around `ClockText.Example` rather than the bare example: a
  grey `13:45` sitting in an empty field reads as a time already filled in.
  `InputMode.numeric` is what puts a keypad under a thumb now that the native widget is gone, and an
  invalid field reports through the snackbar on Save as well as inline, because on a phone the field
  at fault is usually scrolled well out of sight from the button.

## Live banner on the home page
`Home.razor` calls `LiveMatchService.GetTodaysMatchAsync`, which returns a match in progress if
there is one and otherwise today's fixture — so the banner has three forms: `.home-live-banner`
for a match being played (opponent, live score, tap through to `/games/{id}/live`),
`home-banner-upcoming` before kick-off and `home-banner-done` after full time. It is visible to
everyone, since the people most likely to land on the home page on match day are spectators.

It subscribes to **every** `LiveMatchNotifier.Changed` event rather than filtering by game id, the
way the live screen does: the banner has no game of its own until it loads one, so a match being
started is exactly the event it must not miss. That is what makes it appear on an already-open home
page without a refresh.


