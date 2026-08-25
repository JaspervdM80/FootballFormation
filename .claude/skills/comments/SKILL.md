---
name: comments
description: Whether a comment earns its place, and what to do when it does not — the cut/keep test, over-explaining as the middle case, XML docs in a repo that generates no documentation file, and pointing at a skill instead of copying it. Use whenever writing or editing a comment or an XML doc.
---

# Comments

A comment earns its place by carrying information the code cannot. Everything else is a maintenance
liability: it goes stale, it lies, and it pushes the code it describes off the screen.

The test for every one: **would someone editing this in six months make a worse decision without it?**

Write for the next engineer on this codebase, not for someone being taught it. They know C#, Blazor
and EF Core, and they can read the code — so *what* it does is never the job.

## Cut it when it

- Restates the line below it. `// Save the changes` over `SaveChangesAsync()`.
- Narrates structure the language already declares — `// Constructor`, `// Properties`, banner bars.
- Explains a language or framework feature the reader must already know to be in this file at all:
  what `await` does, what `??=` means, what a primary constructor is.
- Paraphrases a well-named identifier. If the name genuinely doesn't say it, rename instead.
- Repeats a codebase-wide convention a skill already owns. **One canonical site each** —
  `TimeProvider` in `MatchClockService`, context-per-operation in `Program.cs`, dates-as-TEXT in
  `QueryTags`, English-message-is-the-resx-key in `Result`. Everywhere else: a pointer, or nothing.
- Narrates history or this diff — *"replaces ten hand-rolled copies that had drifted"*,
  `// changed 2026-03, was 40 minutes`. Git holds history; the file holds the present. Review notes
  belong in the pull request.
- Is commented-out code, or a `TODO` with no owner and no issue.

## Keep it when it

- States a constraint of the platform or the data. `// Fresh entities with Id = 0 — reusing tracked
  IDs trips the UNIQUE constraint.`
- Records a path already tried and rejected, and why. `// DbSet.Update walks the whole graph and
  marks every row Modified — renaming an opponent would rewrite the lineup history.`
- Says why the *obvious* alternative is wrong here.
- Carries an invariant the type system doesn't, or a cross-file coupling a reader of this file alone
  cannot see (`// see GameMinutesReport`).
- Names a deliberate degradation: what the app does when this fails, and why that is acceptable.
- In a test, says what would break without the assertion.

**Cutting one of these is the serious mistake, not leaving a descriptive one in.** The rationale
comments are this repository's best asset.

## Over-explaining is the middle case

A real reason, buried in three paragraphs of tutorial. **Compress to the load-bearing sentence —
don't delete it.** One clause of *why* beats a paragraph of *what*, and a numbered case for the
decision reads as an argument with the reader rather than a note to them.

## XML docs

**This repository generates no documentation file.** `GenerateDocumentationFile` is set nowhere,
nothing is packable, and no XML doc is published — so a `///` block is read by whoever opens the
file and by nobody else.

It therefore earns its place only by saying what the signature cannot: what null means, the
guarantee, the failure mode, the invariant, who owns the lifetime. A `<summary>` describing *what* a
member does goes, and rationale that survives is usually better as a plain `//` above the member
than as a `<summary>` wrapping it. `[Parameter]` docs are worth keeping when they state a precedence
or constraint rule.

**Check `<inheritdoc cref=…>` before deleting the doc it points at.** Three exist —
`ServiceOperation.cs`, `GameService.cs` (which XPaths into one specific `<param>` node) and
`MatchGoalServiceTests.cs`. With no documentation file generated, a broken one fails silently.

## Four more

- **Point at `docs/` or a skill rather than copying it.** `// see docs/patterns/` stays true; a
  paraphrase drifts, and now two things have to be edited together.
- **Comments are English**, even though the UI ships Dutch first.
- **A comment compensating for the code is not a comment problem.** A block that needs a paragraph
  to be followable wants an extraction and a name.
- **Comment and code disagreeing outranks verbosity.** A stale comment is worse than none: fix it
  the moment you notice, and never leave one describing behaviour a change just altered.

The `code-reviewer` agent applies this same lens over a finished diff before a pull request.
