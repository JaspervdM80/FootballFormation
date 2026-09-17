# Design sync notes

- Shape is `foundations`: the repo is Blazor/MudBlazor with no npm package or Storybook, so the converter cannot bundle components. The bundle is hand-assembled in `ds-bundle/`.
- `tokens/club.css` is written out from `ClubTheme.Gjs` (`src/FootballFormation.UI/Theming/ClubTheme.cs`); `tokens/theme.css` is a copy of `src/FootballFormation.UI/wwwroot/theme.css`. Re-copy both when either changes.
- `app.css` is not uploaded: nearly all of it targets `.mud-*` classes or app-specific pages and would be dead weight for designs. Only its `@font-face` rules and `html, body` base are carried.
- No `_ds_sync.json`: there is no honest hash recipe for a hand-built bundle, so every re-sync re-verifies everything.
- Phase 2 (later): React look-alikes of pitch player chips, venue/status badges, cards and buttons, built from the Razor markup. These are reimplementations, so each must be checked against a screenshot of the live page.
