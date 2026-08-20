# Data / domain

- **Deleting a player used to be destructive across every season**: `PlayerService.DeleteAsync` cascades their `GamePlayerPosition` rows and nulls their `GameGoal` scorer, so last season's top scorer disappeared from last season's stats — from a confirm that said nothing about it. Fixed by `ArchivePlayersInsteadOfDeleting`: delete now **refuses** for anyone with a lineup or goal row anywhere, and `Player.IsArchived` is the way to retire someone. Worth knowing when the refusal surprises you: the counts are deliberately **not** scoped to a season, unlike `SeasonSquadService.RemoveMemberAsync`'s, because the cascade is not either.
- **A whole-second match clock ties, and a strict `>` calls both rows the newest**:
  `GameSubstitution.AtSeconds` counts whole seconds, and a double substitution is two taps in a row
  on the touchline, so two rows sharing a second is the normal case, not the edge case.
  `RemoveSubstitutionAsync` guarded "only the most recent one can be undone" with
  `s.AtSeconds > sub.AtSeconds` alone, which both rows of such a pair passed — undoing the earlier
  one restored the player it took off into a slot the later one had already given away, leaving two
  players on the same slot and a timeline naming someone who was not on the pitch. The guard now
  tie-breaks on `s.Id`, which is monotonic (the column is `AUTOINCREMENT`, so a rowid is never
  reused), and `GameMinutesReport` orders the substitutions it rewinds the same way. So does
  `LiveMatch.Timeline` — all three have to agree, or the entry the admin sees on top is not the one
  whose Undo is allowed. **`RecordedAt` is not the tie-break to reach for**: two changes entered in
  one instant share it, and rows written before the column existed all default to `0001-01-01`.
  Every test that predated this advanced `FakeTimeProvider` between substitutions, which is why it
  went unseen — the two that pin it now deliberately do not.
- **A period length in minutes silently loses the remainder**: `Game.PeriodDurationMinutes` used to
  be `GameDurationMinutes / PeriodCount` in `int`, and every planned-minutes figure multiplied that
  truncated number back by 60. A 50 minute match in quarters became 4 × 12, so the dialog offered
  48 minutes of a 50 minute match, the playing-time table planned everyone 4 minutes short, and the
  builder's caption disagreed with the duration printed next to it. The fix is the rule to keep:
  **period length is carried in seconds** (`Game.PeriodDurationSeconds`), which is always exact
  because 60 divides by every period count there is, and the minutes form is a `decimal` for
  display only. `MatchClockReport` already worked in seconds for exactly this reason — it just did
  the division itself instead of asking the model. Reach for `PeriodDurationSeconds` in any new
  arithmetic; `PeriodDurationMinutes` only ever goes on screen.
- **Archiving is a filter on the future, not on the past**: only the "add existing player" picker and copy-forward look at `IsArchived`. `PlayerService.GetAllAsync` deliberately still returns archived players — it is the id → name lookup the match report and live screen resolve against, so filtering it would blank a scorer out of a game they scored in, which is the very thing archiving exists to prevent. Same reasoning for `Game.IsInRoster`: a past game has to be judged the way it was played. If a picker ever *should* hide them, filter at that call site, not in the lookup.
- **A played-time fraction can only exceed 100% if its two sides round differently**: `Game.PlayedDurationMinutes` and `AvailableMinutesFor` used to convert seconds to minutes by truncating
  (`seconds / 60` in `int`), while `GameMinutesReport.ToMinutes` — which builds the numerator from the
  same underlying seconds — rounded to the nearest minute. A player on the pitch for the whole match
  is credited seconds numerically equal to `PlayedDurationSeconds`, so any match that overran by
  30–59 seconds of stoppage time (routine) rounded the numerator up a minute while the denominator
  stayed truncated, showing e.g. `85' / 84' · 101%` for someone who never left the pitch. The fix is
  the same rule as the period-length issue above, generalised: **there is exactly one seconds-to-
  minutes conversion for playing time**, `Game.SecondsToMinutes`, and every consumer —
  `PlayedDurationMinutes`, `AvailableMinutesFor`, `GameMinutesReport.ToMinutes` — calls through it
  rather than keeping its own rounding. Two figures built from the same seconds can no longer round
  apart, by construction rather than by convention.

