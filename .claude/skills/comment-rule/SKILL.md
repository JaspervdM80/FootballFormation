---
name: comment-rule
description: When Claude should and should NOT write code comments, plus the repository facts that change how the rule applies here. Apply on any code edit/write/refactor; the SessionStart hook points at it every session.
---
# The code commenting rule

**Default: write no comments.** A comment must justify its existence; the absence of a comment is the right answer almost always.

## When a comment IS warranted

Only when the **WHY** is non-obvious and a future reader would otherwise be confused or break the code:

- A hidden constraint (e.g. "API requires this header lowercased — server rejects mixed case").
- A subtle invariant (e.g. "must run before X is mounted, otherwise the event listener attaches to the wrong target").
- A workaround for a specific bug or spec quirk (e.g. "Safari 17 fires `focus` twice — debounce").
- Behavior that would surprise a competent reader of this code.

Keep it to **one line** wherever possible. Two lines max. Never a paragraph, never a multi-line block, never a docstring with sections.

## When a comment is NOT warranted (delete or skip)

- Restating WHAT the code does ("// increment counter", "// loop through users") — well-named identifiers already do that.
- Narrating the change or its origin ("// added to fix bug #123", "// used by the X flow", "// previously called Y"). That belongs in the PR description and rots as the codebase evolves.
- Section headers ("// === Helpers ===", "// --- Validation ---") inside a normal function body.
- Restating type annotations or signatures.
- TODO/FIXME without a tracked issue or owner — either fix it now or file it; don't leave drift markers.
- Commented-out code. Delete it; git remembers.

## How to apply

1. Before writing a comment, ask: *would removing this comment confuse a future reader who doesn't know about the current task?* If no, don't write it.
2. When editing existing code, treat redundant comments around your change as fair game to remove (don't go on a comment-stripping crusade in unrelated files).
3. If the user explicitly asks for verbose comments, follow the user — they override this rule.

## XML docs

**No documentation file is generated.** `GenerateDocumentationFile` is set nowhere, nothing is
packable, nothing is published — a `///` block is read by whoever opens the file and by nobody else.
So it buys nothing a `//` does not, and the rule against docstrings with sections applies to it in
full. Where a signature genuinely hides something — what null means, the failure mode, who owns the
lifetime — one line above the member says it.

**Check `<inheritdoc cref=…>` before deleting the doc it points at.** Three exist:
`ServiceOperation.cs`, `GameService.cs` (which XPaths into one specific `<param>` node)  and `MatchGoalServiceTests.cs`. With no documentation file generated, 
a broken one fails silently. The bare `<inheritdoc />` in the migrations is scaffolded and points at nothing.

## Conventions already have a canonical home

`TimeProvider` injection, context-per-operation, dates-as-TEXT and English-message-is-the-resx-key
are explained once each — in `MatchClockService`, `Program.cs`, `QueryTags` and `Result`. Elsewhere
they are a pointer or nothing. The same goes for `docs/` and the other skills: point at them, never
paraphrase, or the two drift and both have to be edited together.

Comments are English, though the UI ships Dutch first.

## How it loads here

`.claude/hooks/session-start.sh` names this skill in the context it emits at session start, so that
it is read before code is written. The `code-reviewer` agent reviews a finished diff against it.

This file is the repository's own copy of the rule, not a synced one: it began as the `mcpmarket-me`
plugin's `comment-rule` skill and has diverged deliberately. Take an upstream improvement by hand,
keeping the sections above.
