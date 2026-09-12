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

- **A wrapping row that becomes a column must stop wrapping.** `.formation-layout` is
  `flex-wrap: wrap` for the three-panel desktop row, and the `959.98px` media query used to flip
  only `flex-direction` to `column`. A *column* that still wraps packs its items into columns and
  resolves its own height from that heuristic, which shrank `.pitch-panel` below the pitch inside
  it — `aspect-ratio: 3/4` on a `width: 100%` box contributes almost nothing to a flex item's
  automatic minimum size, so nothing stopped it. The pitch then overflowed its panel and the
  substitutes card, a `position: relative` `.mud-paper`, painted over the static legend and the
  bottom of the pitch: on a 390x844 phone the legend was invisible and the card sat 14px over the
  grass. Only on tall phones — a 375x667 viewport reflowed correctly, which is why it survived a
  review. `tests/ui/specs/mobile.formation.spec.js` measures the panel's overflow now.
