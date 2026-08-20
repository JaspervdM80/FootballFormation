---
name: localization
description: Adding or changing a user-facing string. Every string goes through IStringLocalizer with the English text as the resource key, and a duplicate resx key now fails the build. Use whenever a new L["..."] key, a resx entry, or a ServiceOperation action phrase is added.
---

# Localization

Dutch is the default culture, English the fallback. Every user-facing string goes through
`IStringLocalizer<Strings>` (`L`) — in pages and dialogs, not only in services — and **the English
text is the resource key**, so only `Strings.nl.resx` exists.

```csharp
L["{0} added to the squad", player.DisplayName]
Result.Failure("Season {0} still has {1} games", name, count)   // the template is the key
```

## A missing key renders English, silently

Nothing warns. After adding an `L["..."]` key, check it against `src/FootballFormation.UI/Strings.nl.resx`
or it ships untranslated.

## Resx keys are case-insensitive, and a collision now fails the build

`ServiceOperation`'s action phrases are lowercase verb phrases ("delete game"); several collided with
capitalized button labels ("Delete Game"). MSBuild warns `MSB3568: Duplicate resource name ... ignored`
and **the first entry silently wins**, so the Dutch string is wrong from a build that was green.

Reuse the existing key, or word the phrase so the two genuinely differ ("archive the player" beside the
menu item "Archive player").

**Why this needed a second MSBuild property**, and the trap worth remembering:
`TreatWarningsAsErrors` is a *compiler* property and does not touch `MSB####` codes, so a duplicate key
warned and built green even in Release. `Directory.Build.props` promotes it with
`MSBuildWarningsAsErrors`, unconditionally — a colliding key is never something to iterate past in
Debug. One limit: `GenerateResource` is incremental, so the check only runs when a resx actually
changed.

## Watch homographs

"Home" was already the venue label ("Thuis") when the nav needed a home link — the nav uses the key
"Start" instead. No `"SUB"`/`"Sub"` pairs either.

## Other rules

- Comments and resource keys are English, even though the UI ships Dutch first.
- The language switcher is the globe menu in `MainLayout` → `/culture/set` → culture cookie → full
  page reload. Circuit culture is fixed at startup, so it cannot be swapped in place.
- `UiFeedback.Translate` looks up both the message template and its arguments — a service states its
  error in English and the page translates it with `L`.

Detail: [docs/ui_components/](../../../docs/ui_components/index.md) ·
[docs/known_issues/](../../../docs/known_issues/localization.md)
