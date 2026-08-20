# Formation/Pitch

- **Duplicate enum positions are fine — do not "fix" them.** A formation with two CBs or two
  strikers returns the same `PlayerPosition` twice from `DefaultPositions()`, and that is the
  design: which slot a player occupies comes from `GamePlayerPosition.SlotIndex`, ordered by
  `FormationSlots.OrdinalOf`. This entry used to say the opposite — that side-specific members
  (LCDM/RCDM, LST/RST) were needed — and they were, until `ConsolidatePlayerPositions` and
  `ConsolidatePositionsRound2` deleted them. Reintroducing them would undo those migrations.
- **Pitch too large**: `max-height: 65dvh` with `aspect-ratio: 3/4` and
  `max-width: calc(65dvh * 3/4)`. `dvh`, not `vh` — on iOS `vh` is the *large* viewport, so with
  the URL bar showing a `vh`-sized pitch is taller than the visible area.
- **Chips must scale with the pitch.** `.pitch` is `container-type: inline-size` and `--chip-size`
  is a `clamp(..., cqw, ...)`. Fixed-pixel chips looked right on a full-width pitch and collided on
  a narrow one — at ~225px wide, a 52px chip is a quarter of the pitch and the wide positions
  (LM at `left: 8%`) hung off the grass, since `.pitch` has no `overflow: hidden`.

