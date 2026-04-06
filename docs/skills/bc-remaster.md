# BC Remaster Skill

> **Version:** 0.1 (post-Fulfillment Remaster iteration planned)
> **Status:** Active — first instance running on Fulfillment BC (M41.x)
> **Template:** `docs/planning/templates/bc-remaster-event-modeling-template.md`

---

## What Is a BC Remaster?

A **BC Remaster** is a deliberate, full event modeling refresh of an existing bounded context
that was originally built with happy-path priority and thin event coverage. It redesigns the
domain model from first principles — questioning aggregate boundaries, surfacing missing events,
modeling compensation paths, and producing a new implementation plan.

**A remaster is not:**
- A **refactor** — code structure changes with the same behavior
- A **feature addition** — new capability layered onto an existing model
- A **new BC** — greenfield design without an existing predecessor

**A remaster is:**
- Same domain, same BC purpose
- Designed properly from first principles using event modeling
- Compensation events and failure paths modeled explicitly, not deferred
- Integration contracts revisited but not broken carelessly
- The version the BC should have been from the start

The term "remaster" is used consistently throughout CritterSupply planning docs, ADRs, and
retrospectives to distinguish this class of work. Like remastering a film — same story,
produced properly.

---

## When to Consider a Remaster

A remaster is appropriate when **two or more** of the following are true:

1. **Feature files ahead of implementation.** `docs/features/<bc>/` describes workflows,
   events, or scenarios that don't exist in the implementation.

2. **Thin event model.** The BC has fewer than 8–10 domain events and the happy path
   covers the vast majority of them. Failure modes and compensation are absent or terminal
   states only.

3. **Missing compensation events.** Workflows that can fail have no recovery path modeled.
   "Failed" is a terminal state with no compensation story.

4. **Hardcoded assumptions.** Comments like `// TODO: implement routing logic` or
   `const string defaultWarehouse = "WH-01"` signal deferred design decisions.

5. **Aggregate doing too much.** The aggregate carries multiple dictionaries, complex
   derived state, or mixes concerns that could be separate streams.

6. **Adjacent BC surfacing.** Modeling a related BC reveals that this BC's model is
   insufficient. ("We can't model X properly until Y's events are richer.")

---

## How This Skill Relates to `event-modeling-workshop.md`

The `event-modeling-workshop.md` skill documents the **general event modeling methodology**
(Adam Dymitruk-style) — the four building blocks, five phases, multi-persona facilitation,
and slice/scenario formats.

This skill (`bc-remaster.md`) documents **how to apply that methodology specifically to
redesigning an existing bounded context**. It adds:

- How to structure the required reading (existing code + feature files + CONTEXTS.md)
- The "mandatory open questions" pattern for resolving key design decisions
- The **Adjacent BC Gap Register** — a running list of gaps in neighboring BCs surfaced
  during the remaster session (becomes the charter for the next remaster)
- How to handle integration contract implications without breaking existing contracts
- The v0.1 → v1.0 iteration model for the skill itself

**Always read `event-modeling-workshop.md` before running a remaster session.** This skill
assumes familiarity with all five phases.

---

## The Remaster-Specific Differences from Greenfield Event Modeling

| Aspect | Greenfield | Remaster |
|---|---|---|
| Starting point | Blank | Existing thin model + feature files |
| Phase 1 seed events | Domain brainstorm | Feature files + existing events as prompts |
| Phase 2 key question | "What's the narrative?" | "Is this aggregate right? What's missing?" |
| Phase 3 UX | Design from scratch | Redesign to match feature file scenarios |
| Phase 4 slices | All new | P0 may rebuild existing slices better |
| Phase 5 scenarios | New BDD features | Update existing feature files |
| Output scope | New feature files | Update existing feature files |
| ADR | BC establishment ADR | Remaster rationale ADR |
| Integration contracts | New contracts | Contracts change carefully |
| Adjacent BCs | Not in scope | Gap Register surfaces adjacent work |

---

## The Adjacent BC Gap Register

Every remaster naturally surfaces gaps in adjacent bounded contexts. For example, modeling
Fulfillment's warehouse routing decision reveals that Inventory's `OrderPlacedHandler`
hardcodes a single warehouse — an Inventory gap.

The **@principal-architect** maintains a running gap register throughout the session. Format:

```
## Adjacent BC Gap Register

**BC:** Inventory
- Hardcoded `WH-01` in `OrderPlacedHandler` — no routing decision event exists
- `StockReceived` and `StockRestocked` do identical things to the aggregate
- No `InventoryTransferred` event for multi-warehouse stock movement
- Reservation commit timing unclear relative to warehouse pick events

**BC:** Orders (saga)
- Saga only handles `ShipmentDelivered` — doesn't handle `TrackingNumberAssigned`
```

This list becomes the charter for the **next remaster** in the sequence.

---

## How to Use This Skill

### Step 1: Identify a Candidate

Use the signals in "When to Consider a Remaster" above. Read `docs/bc-revamp-candidate-analysis.md`
for the CritterSupply-specific candidate ranking.

### Step 2: Produce the Session Prompt

Use the template at `docs/planning/templates/bc-remaster-event-modeling-template.md`.
Fill in all `[FILL IN: ...]` placeholders. The template distinguishes boilerplate
(copy as-is) from domain-specific sections (fill in from your BC's context).

The session prompt is a full, self-contained document that agents can execute without
additional context. It references specific source files, specific feature files, and
specific mandatory open questions for this BC.

### Step 3: Run the Session

The session is documentation-only. No `src/` or `tests/` files are touched. All five
personas run. The @event-modeling-facilitator drives phases; @qa-engineer drives gap-finding
in Phase 2 and scenario stress-testing in Phase 5.

Estimated session length: 3–5 hours for a BC with significant feature file coverage.

### Step 4: Commit Outputs

Required outputs:
- Updated feature files under `docs/features/<bc>/`
- Slice table at `docs/planning/<bc>-remaster-slices.md`
- Session retrospective answering all mandatory open questions
- ADR documenting the remaster rationale and key decisions
- CONTEXTS.md update for the remastered BC
- The gap register for the next BC

### Step 5: Iterate on This Skill

After the first remaster completes, add a retrospective note to this skill file documenting:
- What the template got right
- What needed adjustment
- Whether the mandatory open questions format was effective
- Whether the seed events approach helped or anchored Phase 1 too much

---

## Skill Invocation

This skill should be loaded when:

- A session prompt starts with "Remaster" in the title or mentions "BC Remaster"
- The user references `bc-remaster-event-modeling-template.md`
- The user asks to "redesign," "reimagine," or "model from scratch" an existing BC that
  already has implementation

Related skills to load alongside this one:
- `event-modeling-workshop.md` — the underlying methodology
- `marten-event-sourcing.md` — for aggregate design decisions in Phase 4
- `wolverine-sagas.md` — if the remaster surfaces saga design questions

---

## Version History

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-04-06 | Initial version — extracted from Fulfillment Remaster prompt |
| — | TBD | Post-Fulfillment retrospective pass — update to 1.0 |

*Next update planned after Fulfillment BC Remaster event modeling session completes.*
