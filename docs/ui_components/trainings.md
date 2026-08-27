# Trainings (`/trainings`)

Season-scoped and **admin-only** — `@attribute [Authorize(Roles = AppRoles.Admin)]` on the page,
`AdminOnly: true` on the `AppNav.Menu` entry, and `RunAdminAsync` behind both. The reason the whole
section is gated rather than just its buttons is in [models](../models/training.md#the-one-read-that-is-not-public).

Interactive (`@rendermode InteractiveServer`), so it opens with `<InteractiveShell AdminOnly="true" />`
and reports through `ISnackbar`: the page is a dialog and a confirm, which need a circuit.

## The list is grouped by week, not by month

That is what the request was — *"training days per week"* — and it is how the coach plans. Weeks are
ISO weeks (`System.Globalization.ISOWeek`), so one runs Monday to Sunday, and the heading names the
number and the date range rather than merely separating the rows.

**It reads forwards, with the finished weeks at the bottom.** This week first, then the weeks ahead;
below them a `.training-earlier` rule labelled *Earlier*, and under it the weeks already over, most
recent first. With the period generated in full there are forty weeks either side of that line, and
the evenings still to be written up are the ones next to it. `TrainingService.GetAllAsync` hands the
list over in that order (`TrainingOrdering.UpcomingFirst`, off the injected `TimeProvider`), so the
grouping in `Trainings.razor.cs` follows it rather than sorting again; inside a week the sessions run
ascending, with the id tie-break keeping two on one day in the order they were entered.

## The row

Date, a `.badge-unavailable` count when anyone was out, the note, then the edit/delete `.action-btn`
pair. All four of those classes are `app.css`'s; only the row, the week block and the *Earlier* rule
are scoped in `Trainings.razor.css`.

**A session that did not take place is dimmed, not hidden.** It carries a `cancelled` class that
mutes the date and note, and shows `.badge-warning` **"Cancelled"** *instead of* the absence count —
the two are mutually exclusive, because a cancelled evening is not one everybody missed. Dimmed
rather than removed so the week still reads honestly: a fortnight with nothing in it and a fortnight
that was called off look nothing alike. The header subtitle appends "· {0} cancelled" when there are
any, the way `/games` appends "{0} without lineup".

**The row says how many were out, not who — the badge's tooltip says who.** Four names on a card is
unreadable at a glance, so the count is what is rendered and the names go in the `.badge-unavailable`
`title`, comma-separated in shirt-number order. A `title` is a pointer affordance and nothing on a
phone, which is fine: the dialog is a tap away and is where they are edited anyway.
`Trainings.razor.cs` resolves them through `PlayerService.GetAllAsync` — the whole roster rather than
the attendance rows below it, which are one season's full members with sessions already behind them:
a badge can sit on an evening still to come, under "All seasons", or on a player since archived.
Below 600px the note wraps to a line of its own rather than
being clipped to nothing beside the date, and the actions keep the 44px floor `app.css` gives every
`.action-btn` on a coarse pointer.

## The attendance disclosure

A `<details class="attendance">` above the weeks, holding the season's figure in its `<summary>` —
`79%` and *"7 trainingen gehouden"* — and a row per full member behind it, best attendance first,
each with `n / m`, a percentage and a `.position-track` bar. The rows link to
`/players/{id}/stats`.

**Collapsed by default**, because the register is what the page is for: a squad's worth of rows
above it would push this week's session off a phone screen. A `<details>` rather than a bound panel,
the same choice `SeasonPicker` makes — the browser brings the keyboard and screen-reader behaviour,
and the summary needs no circuit even though this page has one. The chevron rotates off
`.attendance[open]`, through `::deep`: `MudIcon`'s root carries no scope attribute, so without it
that rule matches nothing. The summary keeps the 44px floor.

**It lives here rather than on `/stats`.** The figure is close enough to the absence data that it
belongs behind the same gate, and this page is already `[Authorize]`d in full — putting it on the
public statistics page would have meant a third gated card there. The numbers themselves, and why
guests and cancelled evenings are out of them, are in
[models](../models/training.md#reading-the-register-back).

**Sessions on their own are not enough to render it**, which is why the guard reads
`{ Held: > 0, Players.Count: > 0 }` rather than counting evenings alone. Saving a training period
writes the season's ninety sessions in one go, and a season rolled over that way has them before its
squad is copied forward — so there is a real window with a register full of evenings and nobody to
measure against them. The percentage divides by the player-sessions, which is zero there, and a bare
`0%` over an empty panel reads as *"nobody came"* rather than as *"no answer yet"*.

`Held` counts only the evenings that have **already been and gone**, so the panel stays away through
the start of a season for a second reason as well: in August the ninety sessions are all still ahead,
and none of them is anybody's attendance yet. It appears with the first session the team has actually
had. The rule, and why a session dated today waits until tomorrow, is in
[models](../models/training.md#reading-the-register-back).

The page loads it with a second call, `StatsService.GetTrainingAttendanceAsync`, rather than
building it from the list it already holds: attendance needs the squad, which is not this page's to
load. Every write here reloads, so the figure never lags the register.

## The dialog

`TrainingDialog` — date, unavailable players, note. No start time: a session had one on paper and
nobody ever read it off the screen, so the field went and the migration wiped what the rows were
carrying. Short enough that it needs no season select: the season is resolved from the date on save (`SeasonId` 0, the same convention
`GameDialog` uses), and an existing session keeps its own season so retyping a date never silently
moves it. The player picker reloads on a date change while the season is still being resolved, and
drops ids that are not in that season's squad — so moving the date cannot smuggle a stale one
through.

Injured players are left out of the picker, as in `GameDialog`: they are out of everything already,
and offering them says the same thing twice.

The date arrives pre-filled from `MatchPreferencesService.GetNextTrainingDateAsync`: the soonest
training day in the period with no session on it yet, and once every one is taken — the ordinary
state after the period has generated them — the soonest that has one, i.e. the next evening the team
trains. Add is then mostly for the extra evening, and the admin moves the date; what it must not do
is open on the closing night of the season, which is where a fall-through to the end of the period
put it. With no training days set it proposes today (or the day after the last session entered), so
the section is usable before anyone visits Preferences.

Saving is what makes a session the coach's: `Submit` clears `FromSchedule`, so rewriting the period
afterwards leaves it where it is.

**The "Did not take place" switch** sits under a divider at the foot of the form, amber rather than
red — a cancelled evening is a fact, not a problem. Turning it on removes the unavailable-players
select entirely instead of leaving it there contradicting the switch, and the note's placeholder
changes to ask why. `Submit` sends an empty list either way; `TrainingService` clears it regardless,
because [that invariant does not live in the markup](../models/training.md#a-session-that-did-not-take-place).

## Preferences

`/settings` carries the training block: the weekday multi-select, then **First training** and **Last
training** as two `Clearable` `MudDatePicker`s, then the "Next calculated training date" caption the
three of them move. Clearable matters — an empty date is a real value here, meaning the season's own
window, not an unfilled field, and clearing either end is how the generated sessions are taken back
out. A period that ends before it starts, or reaches outside the season, is refused by `SaveAsync`
with a message rather than saved.

**Save writes the sessions**, and says so: a second snackbar, "{0} trainings created, {1} removed",
shown only when either count is non-zero, over the `TrainingSync` the service returns. A caption
above the button says it before the button is pressed.

Both day selects pass `ToStringFunc`, because `MudSelectItem`'s child content styles the open list
only — without it the collapsed field falls back to MudBlazor's default enum converter and reads
"Monday, Wednesday" in a Dutch UI. The names come from
`CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName`, not from the resx: a weekday is a date
format, not a string somebody translates.

## The way in

`AppNav.Menu` (`AdminOnly: true`) and a tile on the homepage, inside the same `<AuthorizeView>` as
the Preferences tile — `/trainings` is `[Authorize]`d, so a tile a visitor can see is a door that
only ever opens onto the login screen.
