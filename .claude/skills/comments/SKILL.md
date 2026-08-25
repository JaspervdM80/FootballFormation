---
name: comments
description: The few repository facts that change how the comment rule is applied here — no documentation file is generated, the <inheritdoc> trap, and the conventions that already have a canonical home. Read alongside comment-rule, which is the rule itself.
---

# Comments in this repository

**`.claude/skills/comment-rule/SKILL.md` is the rule.** Default: no comments; only a non-obvious
*why*; one line, two at most. This file only adds what that rule cannot know about this codebase,
and it never loosens it.

## XML docs

**No documentation file is generated.** `GenerateDocumentationFile` is set nowhere, nothing is
packable, nothing is published — a `///` block is read by whoever opens the file and by nobody else.
So it buys nothing a `//` does not, and the rule against docstrings with sections applies to it in
full. Where a signature genuinely hides something — what null means, the failure mode, who owns the
lifetime — one line above the member says it.

**Check `<inheritdoc cref=…>` before deleting the doc it points at.** Three exist:
`ServiceOperation.cs`, `GameService.cs` (which XPaths into one specific `<param>` node) and
`MatchGoalServiceTests.cs`. With no documentation file generated, a broken one fails silently.

## Conventions already have a canonical home

`TimeProvider` injection, context-per-operation, dates-as-TEXT and English-message-is-the-resx-key
are explained once each — in `MatchClockService`, `Program.cs`, `QueryTags` and `Result`. Elsewhere
they are a pointer or nothing. The same goes for `docs/` and the other skills: point at them, never
paraphrase, or the two drift and both have to be edited together.

## Two more

- Comments are English, though the UI ships Dutch first.
- A comment compensating for the code is not a comment problem — a block that needs prose to be
  followable wants an extraction and a name.

The `code-reviewer` agent reviews a finished diff against both files.
