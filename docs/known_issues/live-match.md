# Live match

- **A change to the pitch that writes no row is invisible to everything that reads the rows.**
  `SwapPositionsAsync` moves two players between slots without a `GameSubstitution`, which is right
  — nobody left the pitch — but two readers assumed the rows were the whole story.
  `RemoveSubstitutionAsync` handed back the slot the *substitution* recorded, so subbing into slot
  5, swapping that player to slot 0 and then undoing seated two players in slot 5 and emptied
  slot 0; it now reads the slot off the player coming off instead. And `GameMinutesReport` seeds
  from the lineup as it finally stands, so a swap credits **the position moved into** for the whole
  half, earlier minutes included — the opposite of what its comment used to claim. Totals are
  right either way; only the split by position is affected, and a test pins it.
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
- **A stored `Minute` is a scoreboard reading, and the timeline is ordered on elapsed seconds — do
  not mix the two.** They agree only while the halves run to length. On a match whose first half
  was whistled off three minutes long, the scoreboard's 31' is 33 minutes of elapsed play, so
  taking `(Minute - 1) * 60` as an ordering key files a second-half goal *before* one scored in
  first-half stoppage time — wrong running score out of `ScoreProgressionReport`, and the goal
  drawn on the wrong side of the half-time rule. `MatchClockReport.ElapsedOf` is the conversion,
  and it is the only thing that should produce an ordering key for a goal. It cost a review round
  on the change that introduced it.

