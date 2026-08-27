# Squad and Statistics Pages

## Squad table (`/players`) — responsive layout
- One `MudTable` (`.players-table`) serves both breakpoints; **desktop is a normal table
  and must stay untouched** when changing mobile.
- The four data cells carry classes `cell-name` / `cell-pref` / `cell-alt` /
  `cell-actions` so mobile CSS can place them; desktop ignores the classes.
- Below MudBlazor's `599.98px` xs breakpoint (its stacked-card mode), `app.css` overrides
  the card into a **CSS grid** per row: name + preferred position on line 1, alternative
  positions on line 2, edit/delete on line 3, all data right-aligned except the name. The
  per-cell `::before` labels are hidden — the grid replaces "label: value" stacking.
- To make the row a grid container, `.mud-table-root`/`.mud-table-body` are flipped to
  `display: block` on mobile (they are normal table boxes on desktop).
- Gotcha: MudBlazor's dense-table rule outspecifies a plain `.cell-name` selector, so the
  mobile name font-size is set on the inner `.player-name-cell` wrapper, not the cell.
- A row with no alternatives collapses its line via `.cell-alt:not(:has(.badge-gold))`
  (alternatives render as `.badge-gold`, preferred as `.badge-teal`).
- Both position badges are sized for the widest abbreviation (`min-width: 3.25rem`, centred),
  so `GK` and `CDM` are the same box and the alternatives line up under the preferred one.
  Scoped to `.players-table`: elsewhere those two badge classes carry words.
- The row actions are the same box as a game card's `.action-btn` — 32px for a pointer, 44px
  and flush for a finger. MudBlazor sizes a small icon button from its padding, which lands
  at 26px, well under the touch floor.
- Sorting is unavailable on mobile — MudBlazor collapses the header to zero height in card
  mode, and its small-devices sort select is hidden (see `../known_issues/blazor-mudblazor.md`, "the sort select
  that appears on the second render"). Sorting stays a desktop affordance.


## Squad page (`/players`)
Season-scoped: it follows the season picker and shows that season's squad, not everyone on file.

- **The player's name is an `<a>` to `/players/{id}/stats`, not a handler on the row** — the same
  shape `/stats/positions` and `/stats` use, and for the same reason plus one more. The destination
  has no circuit, so a row click would be a round trip to reach a page that needs none; and
  dispatching it also re-rendered the table on the way out, which is what made MudBlazor's
  small-devices sort select appear and left its popover looking for a provider the destination has
  not got (`../known_issues/blazor-mudblazor.md`, "a row click that navigates to a page with no circuit"). The chevron
  and the "Tap a player's name…" hint say the name is the target; `.player-name-cell` carries the
  44px floor that makes it one.
- Row actions (admin only): **remove from squad** (person-remove) and an overflow `MudMenu` holding
  "Edit player", "Archive player" / "Restore player", and "Delete player permanently". Removing from
  a squad is the everyday action; the two that act on the *person* are demoted out of the icon row,
  with archive listed above delete because it is what is almost always meant.
- **Guest and injured status are switches in the Edit Player dialog, not icons on the row.** The
  row already states them, and a toggle next to a mark saying the same thing is two controls for one
  fact. `PlayerDialog` therefore returns a `PlayerEdit(Player, IsGuest, IsInjured)` record rather
  than a `Player`: the person and the two membership flags are separate writes, and the page only
  makes each one when its own switch moved, so renaming someone never touches the squad. Both
  switches are seeded from the row's flags and separated from the person's fields by a divider.
  Only Guest carries a caption — "Injured" on a red switch explains itself, and the caption under it
  only restated what the row shows.
- **Injury is a red cross in front of the name (`.injured-mark`), not a worded badge after it.** It
  is the one status on the row that is read without being spelled out, and the one worth seeing
  before the name rather than after it. `GUEST` and `ARCHIVED` stay worded badges — nothing about
  either is pictured.
- **Archive vs delete.** Archiving retires someone who has left the club: nothing they are in
  changes, they simply stop being offered for seasons still to come (see
  [models](../models/player.md#archiving-and-why-deleting-is-guarded)). Its confirm says so; the delete
  confirm now spells out what would be lost and points at archiving, instead of a bare "are you
  sure" about a cascade nobody can see. `PlayerService.DeleteAsync` refuses anyway once the player
  has played, so the dialog is the explanation and the service is the guard.
- An archived player keeps their place in the squads of the seasons they played and is badged
  **`ARCHIVED`** (`.badge-archived` — the quietest badge on the row; it states an absence rather
  than warning about one). That badge is also the only place the restore action lives, so switching
  the season picker to a season they played is how you find them again.
- "Add Player" is a `MudMenu` with two items: **New player** (creates the person *and* adds them to
  this squad in one action) and **Existing player** (`SquadMemberDialog`, picking from
  `GetNonMembersAsync` — someone from an earlier season, or a guest being promoted).
- **Copy squad from {previous season}** shows only while the squad is **empty** and a previous
  season exists — copying into a populated squad would silently merge two rosters. It lives in the
  header next to "Add Player" (not in the empty-state card, which would duplicate it), preserves
  guest flags and is idempotent.
- **Mobile header actions**: `.squad-actions` becomes `column-reverse` below 700px so "Add Player"
  sits on top and copy-forward below it; the markup order is the reverse so desktop reads
  left-to-right. Both buttons, and `/games`' "Add", carry `btn-compact`, which drops the label
  below 700px and leaves the icon alone (`font-size: 0` on `.mud-button-label` — MudBlazor icons
  set their own font-size, so they don't shrink).
- Three edge states: "All seasons" selected (a card asking you to pick one — a squad belongs to
  exactly one season); an empty squad with a previous season (copy-forward offer); an empty squad
  with no previous season (plain "add your first player").
- The `GUEST` badge is driven by the **member's** flag, so the existing `.badge-guest` CSS is
  untouched. It stays visible at every width here — the `display: none` hide is in
  `Games.razor.css`, scoped to the game cards, and does not reach this table.
- Row actions sit in `.row-actions`, which is `display: inline-flex`. `MudMenu` wraps its trigger in
  a plain `div`, and that wrapper's inline-block baseline put the ⋮ about 6px above the icon buttons
  beside it; flex removes baselines from the equation.

## Statistics screens (`/stats`, `/players/{id}/stats`, `/stats/positions`)
`/stats` and `/players/{id}/stats` are public reads that hold minute figures back from a visitor —
the rule and its one exception are in
[patterns](../patterns/authorization-and-auth.md#authorization-is-at-the-service-boundary-not-only-in-the-markup).
How each page implements it differs, and the difference is the point:

- **`SeasonStats` uses `<AuthorizeView Roles="@AppRoles.Admin">`** around the whole Playing-time
  card. One gate, markup only, nothing else on the page depends on who is looking — so the default
  mechanism is the right one and the code-behind stays unaware.
- **`PlayerStats` uses an `_isAdmin` flag** read in `OnInitializedCoreAsync` (**not**
  `OnInitializedAsync` — `SeasonAwarePage` owns that). It has to: the per-game table is a CSS grid,
  and hiding its minutes cell means dropping a *track* as well, which is a class on the container
  that no `AuthorizeView` inside the rows could set. Hence `.game-list-no-minutes` and, for the same
  reason one tile up, `.stat-tiles-3` / `.stat-tiles-5` — `.stat-tiles` is a fixed `repeat(4, 1fr)`,
  so a row of three would leave a dead fourth column and a row of five would wrap one tile onto a
  line of its own. The `TileColumns` property in the code-behind picks between them: three for a
  visitor, five for an admin looking at a season with training sessions on file, four otherwise. All
  three variants live in `app.css` beside the base rule, and both two-class ones are repeated inside
  the `max-width: 760px` block because a two-class selector outranks the one-class phone rule that
  would otherwise narrow them to two.

The two mechanisms are not quite equivalent, and the difference shows up exactly once: an
`AuthorizeView` re-renders on `AuthenticationStateChanged`, while `_isAdmin` is read once in
`OnInitializedCoreAsync`. An admin whose account is revoked mid-session loses the `/stats` card
immediately and keeps the player page's minutes until they navigate. That is the same trade
`Games` and `MatchResult` already make, and the circuit's own revalidation ends the session shortly
after regardless — but it is why the flag is not the default choice.

**The Playing-time bar is filled to the player's own available minutes** (`PlayerStats.Utilization`
— on-pitch minutes over the played duration of every match they were in the roster for), and the
list is ordered by that share. It used to be filled relative to the busiest player, which made the
top of the table 100% by definition and read as a ranking of who plays most rather than of who is
being rotated fairly. `AvailableMinutes` already excludes matches a player was unavailable for, so
someone who missed half the season is not punished for it. The inline `width:` needs
`InvariantCulture`, like every other percentage in this app — a Dutch decimal comma kills the bar
silently.

**The fifth tile is training attendance**, `n/m` with the percentage and the number missed under
it — admin-only, and present only once the season has held sessions, so a squad in July still reads
as four tiles. It comes from `StatsService.GetPlayerTrainingAttendanceAsync`, which the page skips
entirely when `_isAdmin` is false rather than letting the refusal surface as an error notice for a
figure a visitor was never going to be shown. The rules behind the number are in
[models](../models/training.md#reading-the-register-back).

That tile is also why the page can now render with an *empty* per-game list, which it never could
before: the empty state at the top gives way as soon as there is attendance to show, so the per-game
card carries its own "no games yet" line instead of a column header row over nothing.

**The Availability switch beside that card's label swaps in a second bar**, filled to
`PlayerStats.MaximumMinutes` instead — every minute the season's completed matches offered. The two
answer different questions: the fairness bar is each player against her own availability, so a girl
who was there for two matches and played both reads 100%; the availability bar puts every player on
one scale, and hers is short. It splits into the four figures that partition that maximum — played,
injured, unavailable, not played — with the segments sized by `flex-grow` rather than by a
percentage, so the four normalise themselves whatever per-game rounding did to them.

**The switch is a checkbox, for the same reason the nav drawer is one**: `/stats` has no circuit, so
`@bind` would bind to nothing. Both views are rendered and `.availability-toggle:checked ~ …` picks
between them, which is also why every rule in `SeasonStats.razor.css` selects forward from that
input — it has to stay the list's first sibling inside the `MudPaper`. The input is visually hidden
but not `display: none`, or it would leave the tab order with its label behind.

**The injured segment has two sources**, and neither is today's squad flag: a `GameInjury` for the
stretch after she was carried off, and the match's own `InjuredPlayerIds` for one she missed
entirely. The second is a copy of `SeasonSquadMember.IsInjured` taken at the final whistle
(`StandingInjuries.RecordAsync`, [../models/game.md](../models/game.md)) — reading the live flag
here would be the retroactive rewrite `Game.IsInRoster` refuses, since a girl injured today would
have her whole season recoloured including the matches she was fit for.

**The per-game rows carry `<VenueBadge Inline="true" />`**, rather than the `vs` / `@` prefix the
rest of the app still uses in running text. The row is a list of fixtures where the venue is a fact
about each one, not a sentence about a single match — a badge reads down it, a prefix does not.
`.g-opp-name` is a flex row so the pill keeps its width and `.g-opp-text` gives way to the ellipsis;
the rule holding that needs **`::deep`**, because the pill is a child component's root element and
those carry no scope attribute.

**`/stats/positions` is admin-only outright** (`@attribute [Authorize(Roles = AppRoles.Admin)]`,
same as `/settings` and `/users`), not redacted like the two pages above — who has been played where
is a selection tool, not a figure worth sharing with a visitor. `PositionDevelopmentReport` pivots
the `PlayerStats.Positions` the other two pages already compute — no new query, no new minutes
aggregation — into a players × positions grid, reached from a `<PageHeader>` `<Actions>` button on
`/stats` gated the same way. The table follows `FormationBuilder`'s `playtime-table`: a `MudTable`
with one `MudTh`/`MudTd` per position rather than a fixed column set, `.stacked-table` for the phone
card layout, and `HorizontalScrollbar="true"` because the column count is the size of the squad's
position spread, not capped like the four-period playing-time table.


