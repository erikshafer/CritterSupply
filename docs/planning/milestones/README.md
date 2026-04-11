# Session Retrospectives

This directory holds the retrospectives produced at the close of each CritterSupply implementation session, alongside the milestone docs themselves. One retrospective corresponds to one session prompt — the durable record of what happened when that prompt ran, what was learned, and what the next session needs to know.

Retros are living documents in the sense that the **template and rules below evolve through the BC Remaster milestones**. After each session lands, revisit this file and fold in whatever the retro itself surfaced about the format. Do not treat anything here as frozen.

## Naming convention

```
m{milestone}-{sub}-session-{N}[{letter}]-retrospective.md
```

Examples: `m39-0-session-1-retrospective.md`, `m40-0-session-1b-retrospective.md`. The `letter` suffix is reserved for follow-up sessions that revise or replace the prior session's output (e.g. S1B replacing S1's manual workaround with the real API).

## Template format

Every retrospective follows this structure:

```markdown
# {Milestone} Session {N} Retrospective[ — {Short Title}]

**Date:** YYYY-MM-DD
**Milestone:** {M##.#} — {Milestone Name}
**Session:** Session {N} — {What this session did, in 5–10 words}
[**Duration:** ~Xh]

## Baseline

Three to five bullets capturing the starting state: build errors/warnings, test counts (BC-scoped and full solution), and the structural facts the diff will be measured against.

## Items Completed

| Item | Description |
|------|-------------|
| S{N}a | … |

Mirror the session prompt's item codes exactly so prompt ↔ retro traceability is mechanical.

## S{N}{letter}: {Title}

One subsection per non-trivial item. Each contains whichever of these apply:

- **Why this approach** — when a Critter Stack idiom was chosen or rejected (e.g. why `[WriteAggregate]` couldn't be used, why `HandlerContinuation.Stop` over `ProblemDetails`).
- **Handler/structure after** — short code block showing the resulting shape.
- **Structural metrics table** — `Metric | Before | After` for line count, class type, injected dependencies, return type. The most distinctive recurring element of CritterSupply retros.
- **Discovery / resolution** — when something failed and was worked around, document the error message verbatim, the root cause, and the resolution.
- **Edge cases preserved or fixed** — bugs found incidentally.

## Test Results

| Phase | {BC} Tests | Result |
|-------|-----------|--------|

A phased table showing pass count after each item or sub-step. Always end with the final state and call out whether test count changed.

## Build State at Session Close

Bullets covering: errors, warnings (with delta from baseline and explanation if changed), and BC-scoped grep-style metrics that prove the refactor landed (`session.Events.Append() calls: 0`, `MartenOps.StartStream calls: 9`). These are the "negative space" assertions — counts of things that should now be zero.

## Key Learnings

Numbered list of generalizable insights. Each is one or two sentences naming the principle and the evidence. Reserve this section for things future sessions in other BCs will need to know; do not restate item-level details.

## Verification Checklist

- [x] One item per acceptance criterion from the session prompt.

Mirrors the prompt's "Definition of Done" so the retro doubles as sign-off.

## What Remains / Next Session Should Verify

Bullets calling out deferred work, follow-ups, and explicit non-goals. Distinguish "in scope for the milestone, deferred to S{N+1}" from "out of scope, tracked elsewhere."
```

## Optional sections

Use only when the session warrants them:

- **{BC} Assessment (After S0+S1+S2…)** — for milestones that complete a BC, a numbered summary of what the BC now demonstrates idiomatically.
- **Files Changed** — categorized list (New / Modified / Deleted / Tests / Docs) with one-line annotations. Use only when the change set spans many files or projects.
- **API Surface Explored** — when a session is research-heavy against an unfamiliar Critter Stack feature, document what was tried, what worked, and what didn't.
- **Comparison Table vs Prior Session** — `Component | Before | After` spanning the whole session. Used when one session revises or replaces the output of the prior session.

## The ten rules

1. **One retro equals one session.** A session that produced one PR produces one retrospective. Multi-session summaries belong in the milestone doc, not here.

2. **The session prompt is the spine.** Retros mirror the prompt's item codes, acceptance criteria, and scope boundaries. If the prompt and retro disagree about what was attempted, the retro is wrong.

3. **Concrete over narrative.** Tables, signatures, counts, file paths, verbatim error messages. Prose is reserved for explaining tradeoffs the tables can't capture.

4. **Name the idiom.** When an idiomatic choice is made (`HandlerContinuation` vs `ProblemDetails`, `Load()` vs `[WriteAggregate]`, snapshot lifecycle), say *why* the alternative was rejected. These passages are the BC's contribution to the broader Critter Stack pattern library.

5. **Cross-reference other BCs.** When a pattern is applied, name the reference BC ("same API used by Promotions, Returns, Payments…"). This builds the implicit cross-BC index every future session benefits from.

6. **Verbatim error messages for failures.** The DELETE-body 400 and the `CS0128` duplicate-variable errors are useful precisely because they're searchable by future sessions hitting the same wall.

7. **Negative assertions are first-class.** "`IDocumentSession` usage: 0" is a stronger and more grep-able claim than any prose paragraph.

8. **No marketing voice.** A short BC assessment paragraph is the upper bound of self-congratulation; everything else stays factual.

9. **Preserve the prompt's item codes.** S1a, S1b, S2a — never renumber, even if the work landed in a different order.

10. **Retros are committed in the same PR as the session's code.** A session without a retro is a session whose lessons evaporate. The retro is part of the deliverable, not a follow-up.

## What a retro is not

- **Not a changelog.** Files-changed lists are optional and only useful when the change set is large or distributed.
- **Not a design doc.** ADRs handle the "why we chose this architecture" question; the retro records what happened during execution and what was learned.
- **Not a tutorial.** Code blocks show the resulting shape, not how to teach the pattern to a newcomer. Skill files do the teaching.

## Known gaps

Until the BC Remaster milestones are complete, expect the template and the rules to move. Propose changes by PR against this file with a short note describing which session surfaced the gap.

See `../../prompts/README.md` for the matching session prompt template — together they form the prompt → execute → retro loop.
