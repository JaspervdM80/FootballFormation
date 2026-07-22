# Roadmap / Backlog

Forward-looking ideas for the app. Newest thinking at the top of each section. Past bug
fixes live in [known_issues.md](known_issues.md), not here.

## In progress

- **Season reporting** — data-completeness flags on the games list (past games missing a
  lineup are flagged), a season dashboard (`/stats`: record, goals for/against, form, top
  scorers), and a playing-time fairness table across the squad.

## Next

- **Seasons / competitions** — group games into a season (and optionally a competition), and
  filter the games list and all stats by the selected season. Right now every stat blends all
  games together; as more years of data accumulate this will blur career vs. current-season
  numbers. Likely needs: a `Season` entity (or a `SeasonId`/date-range on `Game`), a season
  picker in the header, and a season filter threaded through `SeasonStatsReport` /
  `PlayerStatsReport`.

## Later

- **Live match mode** — a phone-friendly sideline screen: running clock, quick "+ goal / +
  assist" buttons, and quick substitutions that write straight into the period lineup. This is
  the root-cause fix for missing-lineup data (e.g. the ASWH game), since minutes and goals get
  captured as they happen instead of reconstructed afterward.
- **Share lineup as image** — export the formation/team sheet as a PNG for the WhatsApp group.
- **Opponent head-to-head** — a small "vs this club" history (we already replay teams like
  Sliedrecht and Hardinxveld).
- **In-app DB export / backup** — one-click database download (and/or a scheduled Fly backup).
  Hardens the single-SQLite-volume risk instead of relying on the manual restore flow.
- **Team position-development view** — a squad-wide grid of who has played where over the
  season, to support giving every youth player varied minutes.
