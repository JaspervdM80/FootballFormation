# Localization

- **Resource keys are English text, so watch for homographs**: "Home" was already the
  venue label ("Thuis") when the nav needed a home link — the nav uses the key "Start"
  instead. Resx names are also case-insensitive, so no "SUB"/"Sub" pairs.
- **Case-insensitivity bites the service action phrases**: `ServiceOperation`'s actions are
  lowercase verb phrases ("delete game"), and several collided with existing capitalized button
  labels ("Delete Game"). MSBuild warns `MSB3568: Duplicate resource name ... ignored` and the
  first entry silently wins. Reuse the existing key rather than adding a lowercase twin — or, when
  the phrase has to differ because it is substituted into a sentence ("archive the player" beside
  the menu item "Archive player"), word it so the two are not the same key.
  **This one now fails the build** — `Directory.Build.props` promotes MSB3568 to an error. The trap
  worth remembering is *why it took a second property*: `TreatWarningsAsErrors` is a compiler
  property and does not touch `MSB####` codes, so a duplicate key warned and built green even in
  Release, where every other warning is fatal. Promoting an MSBuild-engine warning needs
  `MSBuildWarningsAsErrors`. It is set unconditionally, so Debug catches it too.
  One limit: `GenerateResource` is incremental, so the check only runs when a resx has actually
  changed — which is when a duplicate arrives, and CI builds cold regardless.

