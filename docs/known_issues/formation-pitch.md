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
  review.
- **The squad list is a grid on a phone, not a capped scroller.** `max-height: 70vh` was written on
  the card, and a sixteen-player squad in one column is taller than that — the last names were only
  reachable through an inner scroller inside the page's own, which is how they came to be reported
  as missing. Below 959.98px `.player-grid` drops the cap and lays the squad out in
  `repeat(auto-fill, minmax(120px, 1fr))` columns. **The 120px is the load-bearing number**: the
  track has to be small enough that the narrowest phone still gets *two* columns, because dropping
  the cap at one column makes the list taller than it was and moves the pitch further from the
  player being dragged onto it, not closer. At 320px the card leaves the grid 276.4px, which takes
  two 120px tracks and an 8px gap and not two 140px ones. Measured: two columns at 320px and at
  375px, six on a 844px landscape phone, and a row never under 121x44.9. Its `gap` replaces the
  rows' 4px `margin-bottom`, so the column and row gutters read alike.
- **Nothing measures more than one row of that grid.** `scripts/touch-targets.mjs` audits the
  builder of the game `scripts/visual-check.mjs` seeds with a line-up — four players, three of them
  already on the pitch — so `neighbours()` never sees a *pair* of `.draggable-player` boxes and the
  grid's gutters are checked by no harness. Both were computed from the CSS and measured by hand
  instead (320/375/844). Widening that seed would point the same scene at a game with no chips on
  the pitch, which is the other half of what it exists to measure — the same shape of mistake
  `touch-pwa.md` records under "The scenes added for the pitch chips measured no pitch chip".
