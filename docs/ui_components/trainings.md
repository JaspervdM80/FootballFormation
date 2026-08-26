# Trainings (`/trainings`)

Season-scoped and **admin-only** — `@attribute [Authorize(Roles = AppRoles.Admin)]` on the page,
`AdminOnly: true` on the `AppNav.Menu` entry, and `RunAdminAsync` behind both. The reason the whole
section is gated rather than just its buttons is in [models](../models/training.md#the-one-read-that-is-not-public).

Interactive (`@rendermode InteractiveServer`), so it opens with `<InteractiveShell AdminOnly="true" />`
and reports through `ISnackbar`: the page is a dialog and a confirm, which need a circuit.

## The list is grouped by week, not by month

That is what the request was — *"training days per week"* — and it is how the coach plans. Weeks are
ISO weeks (`System.Globalization.ISOWeek`), so one runs Monday to Sunday, and the heading names the
number and the date range rather than merely separating the rows. Newest week first, so this week is
at the top; inside a week the sessions run newest-first too, with the id tie-break keeping two
sessions on one day in the order they were entered.

The grouping lives in `Trainings.razor.cs` as a private `Sections`-style iterator, the same shape
`Games.Sections()` has. It is presentation, not a report — nothing outside the page asks for it.

## The row

Date (with the start time when there is one), a `.badge-unavailable` count when anyone was out, the
note, then the edit/delete `.action-btn` pair. All four of those classes are `app.css`'s; only the
row and the week block are scoped in `Trainings.razor.css`.

**The row says how many were out, not who.** Four names on a card is unreadable at a glance, and the
names are one tap away in the dialog. Below 600px the note wraps to a line of its own rather than
being clipped to nothing beside the date, and the actions keep the 44px floor `app.css` gives every
`.action-btn` on a coarse pointer.

## The dialog

`TrainingDialog` — date, optional start time, unavailable players, note. Short enough that it needs
no season select: the season is resolved from the date on save (`SeasonId` 0, the same convention
`GameDialog` uses), and an existing session keeps its own season so retyping a date never silently
moves it. The player picker reloads on a date change while the season is still being resolved, and
drops ids that are not in that season's squad — so moving the date cannot smuggle a stale one
through.

Injured players are left out of the picker, as in `GameDialog`: they are out of everything already,
and offering them says the same thing twice.

The date arrives pre-filled from `MatchPreferencesService.GetNextTrainingDateAsync`, which is what
the per-season training days are for. With none chosen it proposes today (or the day after the last
session entered), so the section is usable before anyone visits Preferences.
