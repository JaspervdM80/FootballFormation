# Live match

- **A change to the pitch that writes no row is invisible to everything that reads the rows.**
  Position swaps used to be exactly that: `SwapPositionsAsync` moved two players between slots and
  wrote nothing — right as far as minutes go, nobody left the pitch — but two readers assumed the
  rows were the whole story. `RemoveSubstitutionAsync` handed back the slot the *substitution*
  recorded, so subbing into slot 5, swapping that player to slot 0 and then undoing seated two
  players in slot 5 and emptied slot 0; it now reads the slot off the player coming off instead. And
  `GameMinutesReport` could only credit **the position moved into** for the whole half. A swap now
  writes a `GamePositionSwap` at its second and `HalfLineupWalk` splits the half there, but
  **matches from before still carry swaps with no minute**, baked into the final line-up. Rewinding a
  substitution follows the slot the incoming player ended in, the rule undoing one already used, so
  the player who left is now credited that spot rather than the one the substitution recorded — the
  position split of such a match changed for her when this shipped; totals did not. A test pins it
  (`A_swap_recorded_before_swaps_had_a_minute_credits_the_spot_moved_into_for_the_whole_half`).
- **A substitute moved straight after coming on was never really in the spot she came on in.** The
  live screen takes her off the bench into the slot of whoever left, and the coach then swaps her to
  where she is meant to play — seconds later, but a second the walk would credit to the wrong
  position (a field player showing a minute in goal). `HalfLineupWalk` therefore moves a swap made
  within `ArrivalSettlingSeconds` (60) of a player coming on back to the second she came on, provided
  that arrival is the last change either player was part of. Timeline and stored rows keep the real
  second; only who is credited where changes.
- **A timed swap is a change like any other, so a substitution cannot be rewound past one.**
  Undoing or editing a substitution whose player a later swap moved, or adding a forgotten one before
  a swap of either player, is refused with *"Undo the later position swap first"*: reversing it would
  hand back a slot she no longer holds and leave the swap naming someone who was never on. Adding or
  undoing a swap afterwards is the other way round. It never changes who is on the pitch, only who
  holds which slot, so `AddPositionSwapAsync`/`RemovePositionSwapAsync` walk the whole half again and
  rewrite the line-up and every later substitution's recorded slot to match.
- **A quarters match only ever kicks off two of its four periods.** The live match knows halves
  and nothing else: `Game.NextHalf()` skips a line-up whose half has already been played, so the
  second half opens at Q3. Q2 and Q4 keep their planned line-ups and never get `StartedAtSeconds`,
  which is exactly what `GameMinutesReport` needs — a line-up that was never kicked off contributes
  nothing, so the half is credited to the line-up that played it plus the substitutions made during
  it. Q2 and Q4 reach the touchline only as `Game.MidHalfPlan()`, behind the live screen's
  `Changes (n)` pop-up. Do not "fix" a Q2 with no timings, and do not read `PeriodCount` as a count
  of stages the clock stops for.
- **A goal's minute is derived, not stored — and two goals in the same table are placed by
  different columns.** A goal logged from `/live` carries `GamePeriodId` and `AtSeconds`, the same
  pair a substitution carries, and the minute anyone sees comes out of `MatchClockReport.MinuteOf`.
  A goal typed in on `/result` has neither and falls back to `Minute`. So do all the goals logged
  before `StoreGoalPeriodAndClock` that were not scored in stoppage time: that migration backfilled
  only what an old row states outright, and a plain minute does not say which half it belonged to.
  The trap is reading `Minute` directly and finding it null on a live match, or assuming a row that
  has one was typed in by hand. Never reinstate the previous shape — a minute frozen on the row
  moved under stored data whenever `GameDurationMinutes` changed, and could not be corrected when a
  half's timings were.
  The same migration dropped `AdditionalMinute`, but **backfilled the rows that carried one first**:
  an overrun on a row says outright that it was stoppage time, so the half follows from the minute
  and the clock reading from that half's kick-off, and those goals still read `30+2` afterwards.
  `32` would be the 32nd minute — two minutes into a second half — which is a different moment.
  Rows with `AdditionalMinute = 0` were left alone, because a stored `37` could equally be a minute
  typed in by hand. That backfill has run everywhere it was ever going to, and the migration was
  folded into `InitialCreate` along with the test that drove it across the boundary — so what is
  written here is now the only record of why an old row looks the way it does.
- **Correcting a half's length is the end of the half moving, never its start.** The clock left
  running past the whistle inflates `EndedAtSeconds`, and `GameMinutesReport` hands that overrun to
  whoever was on the pitch — real playing time in the season's utilisation, for minutes nobody
  played. `AdjustHalfLengthsAsync` writes `EndedAtSeconds` and nothing else. Moving
  `StartedAtSeconds` instead would look equivalent and is not: every goal, substitution and injury in
  the half stores an absolute elapsed second, and `MatchClockReport` derives their minutes as
  `elapsed - start`, so shifting a start silently re-times every event in that half. The second half
  also legitimately begins later than the first half now ends — the gap is the break, and it costs no
  minutes because the report credits each period from its own start to its own end.
- **A stored `Minute` is a scoreboard reading, and the timeline is ordered on elapsed seconds — do
  not mix the two.** They agree only while the halves run to length. On a match whose first half
  was whistled off three minutes long, the scoreboard's 31' is 33 minutes of elapsed play, so
  taking `(Minute - 1) * 60` as an ordering key files a second-half goal *before* one scored in
  first-half stoppage time — wrong running score out of `ScoreProgressionReport`, and the goal
  drawn on the wrong side of the half-time rule. `MatchClockReport.ElapsedOf` is the conversion,
  and it is the only thing that should produce an ordering key for a goal. It cost a review round
  on the change that introduced it.
- **The formation builder could overwrite the line-up a half was played with, from a tab nobody
  had touched since before kick-off.** `SavePeriodLineupAsync` is delete-then-insert from whatever
  the caller hands it, and the builder's `PeriodLineups` is filled once in `OnInitializedAsync` and
  never resubscribed — the page does not listen to `LiveMatchNotifier`. So a coach who opened
  `/games/{id}/formation` before the match, ran it from `/games/{id}/live` in another tab, and then
  went back and pressed Save All wrote the pre-kick-off plan over the first half: both substitutions
  gone from the line-up, every row handed a new id, and `GameMinutesReport` crediting minutes to the
  players who had been taken off. Nothing looked wrong — the pitch reads the slot. The fix is that a
  period which `HasKickedOff` belongs to the touchline: the service refuses it whoever asks, the
  builder renders it read-only, and Save All skips it on the strength of a fresh read rather than of
  its own cached game. The same shape had already been fixed once for the formation picker (#147,
  #148); Save All was the half of it left open, and it was reachable on `main` the whole time.
