# Roadmap / Backlog

Forward-looking ideas for the app. Newest thinking at the top of each section. Past bug
fixes live in [known_issues.md](known_issues.md), not here.

## In progress

- **Season reporting** — data-completeness flags on the games list (past games missing a
  lineup are flagged), a season dashboard (`/stats`: record, goals for/against, form, top
  scorers), and a playing-time fairness table across the squad. Games are now grouped into
  `Season` records with a picker in the app bar filtering `/games`, `/stats` and player stats;
  see [models.md](models.md) and the "UI state services" section of [patterns.md](patterns.md).

- **Live match mode** — shipped: `/games/{id}/live` runs the clock, records substitutions as
  timestamped events and logs goals/assists (ours and the opponent's) straight into the score.
  Admin drives it, everyone else can watch the same URL read-only. See [models.md](models.md)
  and the "Live match screen" section of [ui_components.md](ui_components.md).

## Next

- **Exact minutes in the season reports** — `LiveMinutesReport` now computes real minutes from
  `GameSubstitution` plus `GamePeriod.StartedAtSeconds`/`EndedAtSeconds`, but only for the live
  screen. `PlayingTimeReport` and `PlayerStatsReport` still estimate
  `periodsPlaying × PeriodDurationMinutes`. Folding the exact figure in is the follow-up, and it
  has to stay a *fallback*: a game that was never run live has no clock anchors to read, so the
  estimate remains the only answer for historic fixtures.

## Later

- **Competitions** — a competition/type per game (league, cup, friendly, tournament) so stats can
  be split by competition as well as by season. Deliberately left out of the seasons work to keep
  that change focused; it would be an enum on `Game` plus a second filter alongside the season one.

- **Share lineup as image** — export the formation/team sheet as a PNG for the WhatsApp group.
- **Opponent head-to-head** — a small "vs this club" history (we already replay teams like
  Sliedrecht and Hardinxveld).
- **In-app DB export / backup** — one-click database download (and/or a scheduled Fly backup).
  Hardens the single-SQLite-volume risk instead of relying on the manual restore flow.
- **Team position-development view** — a squad-wide grid of who has played where over the
  season, to support giving every youth player varied minutes.
