# Roadmap / Backlog

Forward-looking ideas for the app. Newest thinking at the top of each section. Past bug
fixes live in [known_issues.md](known_issues.md), not here.

## In progress

- **Season reporting** — data-completeness flags on the games list (past games missing a
  lineup are flagged), a season dashboard (`/stats`: record, goals for/against, form, top
  scorers), and a playing-time fairness table across the squad. Games are now grouped into
  `Season` records with a picker in the app bar filtering `/games`, `/stats` and player stats;
  see [models.md](models.md) and the "UI state services" section of [patterns.md](patterns.md).

## Next

## Later

- **Competitions** — a competition/type per game (league, cup, friendly, tournament) so stats can
  be split by competition as well as by season. Deliberately left out of the seasons work to keep
  that change focused; it would be an enum on `Game` plus a second filter alongside the season one.

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
