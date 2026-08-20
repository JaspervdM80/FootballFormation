# MudBlazor 9.x Notes

## MudBlazor 9.x Notes
- `ValidateAsync()` not `Validate()`
- `IReadOnlyCollection<T>` for multi-select `@bind-SelectedValues`
- `IMudDialogInstance` (cascading parameter in dialogs)
- `MudIconButton` takes lowercase `title`, not `Title` (the MUD0002 analyzer flags it). The
  `.action-btn` row buttons are plain `<button>` elements with `title` anyway.
- **`MudMenu` + `ActivatorContent` does not wire itself up.** The custom activator receives a
  `MenuContext` and *you* must call `context.ToggleAsync` — MudBlazor attaches no click handler to
  the `.mud-menu-activator` wrapper, though it does give it `role="button"` and `tabindex="0"`,
  leaving it focusable but inert. Prefer `Label` + `StartIcon`/`EndIcon` and style the generated
  button (as the squad page's "Add Player" menu does), which arrives keyboard-accessible for free.
  Note that `MudMenu` is not an option in the chrome at all: it needs `MudPopoverProvider` and a
  circuit, and the layout has neither — the season and language pickers are `<details>` disclosures.
- **`MudMenu.Class` styles the root wrapper, not the activator.** There is no `ActivatorClass`
  parameter, so a button style has to be pushed down a level — `.btn-gold.mud-menu .mud-button-root`
  in app.css does that for the squad page's "Add Player" menu.
- **Scoped CSS does not reach a MudBlazor component's root element.** A `Class` you put on a
  `MudPaper`/`MudButton` lands on markup the child component renders, which carries *its* scope
  attribute (or none), never the page's — so the rule silently does nothing and you get MudBlazor's
  default. Style plain elements in the scoped `.razor.css`; put anything targeting a MudBlazor
  component in `app.css` (see the `.live-scoreboard` / `.live-action-btn` block there).
- **A Razor comment inside a component's attribute list is parsed as an attribute name**, and
  throws `does not have a property matching the name '@* … *@'` at render time — not at build time,
  so it survives `dotnet build`. Put the comment on the line *above* the tag.
- **Never set `position` in a global `.mud-paper` rule.** `MudPopover` is a `.mud-paper`, and
  overriding its `position: absolute` turns every dropdown into a full-width band. See
  [known_issues](../known_issues/index.md).
- `MudDialogProvider`, `MudSnackbarProvider`, `MudPopoverProvider` all in MainLayout
- Theme: **light mode**, club red/green from the crest. Colors are centralized as CSS
  variables — see [theming](../theming.md). The MudBlazor palette (built by `ClubTheme`)
  is a separate C# copy that must be kept in sync with `theme.css`.

