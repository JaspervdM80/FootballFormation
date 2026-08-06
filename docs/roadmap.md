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

- **Filter stats by match type** — `Game.MatchType` (Competition / Cup / Practice) now exists and is
  shown on the games list, the result page and the shareable overview, but it is purely descriptive:
  every type still counts towards the season table and player minutes alike. Splitting the reports
  by type — a second filter alongside the season one — is the follow-up. Worth deciding first
  whether a practice game *should* count, since the answer changes every historic figure.

- **Share lineup as image** — export the formation/team sheet as a PNG for the WhatsApp group.
- **Opponent head-to-head** — a small "vs this club" history (we already replay teams like
  Sliedrecht and Hardinxveld).
- **In-app DB export / backup** — one-click database download (and/or a scheduled Fly backup).
  Hardens the single-SQLite-volume risk instead of relying on the manual restore flow.
- **Team position-development view** — a squad-wide grid of who has played where over the
  season, to support giving every youth player varied minutes.
