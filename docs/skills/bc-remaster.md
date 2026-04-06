# BC Remaster Skill

> **Version:** 0.2 (updated after Fulfillment BC Remaster event modeling session)
> **Status:** Active — Fulfillment Remaster implementation in progress (M41.x)
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

7. **Feature files describe specialized domain scope.** International shipping, compliance,
   regulatory workflows, or third-party logistics appear in the feature files. These signal
   that the remaster may need to be scoped carefully — some of this territory warrants a
   dedicated focused sub-session rather than being bundled into the primary remaster.

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

## Feature Files as Domain Expert Input

When the BC has existing feature files (`docs/features/<bc>/`), treat them as **domain
expert input**, not as a design constraint. Read them completely before Phase 1. They
often contain detailed business scenarios, actor perspectives, and edge cases that represent
significant prior analysis — even if none of it is implemented.

In the Fulfillment Remaster, three feature files described carrier API integration, warehouse
wave management, international customs flows, and delivery exception handling in extensive
detail. Using these as Phase 1 primers dramatically accelerated the brain dump and grounded
the session in realistic workflows rather than abstract domain modeling.

**How to use feature files during the session:**
- The Facilitator reads the seed events list (derived from feature files) aloud at the start
  of Phase 1 to prime all personas
- During Phase 2, reference specific feature file scenarios when gaps are discovered
- During Phase 5, update the feature files with the remastered scenarios — don't create
  parallel files

---

## Scope Complexity Signals — When to Plan a Sub-Session

Some scope that surfaces during a remaster warrants its own focused session rather than
being bundled into the primary event modeling session. Watch for these signals:

- **International / cross-border shipping.** Customs documentation, duties, carrier
  regulations, and compliance workflows require trade compliance domain expertise.
  The Fulfillment Remaster produced 7 international slices (P3) that were flagged as
  underspecified compared to P0–P2 and deferred to a dedicated sub-session.

- **Third-party logistics (3PL) integration.** 3PL handoffs have different SLAs, different
  integration patterns (API vs. EDI vs. file drop), and different operational contracts
  than in-house fulfillment. Surface it in Phase 1 but explicitly scope it to P3+ and plan
  a separate session.

- **Regulatory / compliance workflows.** Hazmat classification, food safety traceability,
  pharmaceutical cold-chain, or financial compliance add domain-specific rules that require
  specialist input beyond the standard five personas.

If any of these emerge, the Phase 4 priority decision should explicitly mark the scope as
P3+ and the session retrospective should note "requires dedicated sub-session."

---

## The Adjacent BC Gap Register

Every remaster naturally surfaces gaps in adjacent bounded contexts. For example, modeling
Fulfillment's warehouse routing decision reveals that Inventory's `OrderPlacedHandler`
hardcodes a single warehouse — an Inventory gap.

The **@principal-architect** maintains a running gap register throughout the session.

### Register Format (include severity)

```markdown
## Adjacent BC Gap Register

**BC:** Inventory

| # | Gap | Severity | Current State | Required State | Surfaced During |
|---|---|---|---|---|---|
| 1 | Hardcoded WH-01 warehouse | 🔴 Critical | `const string defaultWarehouse = "WH-01"` | Routing decision provides WarehouseId | Phase 2 |
| 2 | No multi-warehouse allocation | 🔴 Critical | Single aggregate per SKU | Per-warehouse stock levels | Phase 2 |
| 3 | StockReceived vs StockRestocked redundancy | 🟡 Medium | Both events apply identically | Clarify semantic distinction | Phase 1 |
| 4 | No InventoryTransferred event | 🟡 Medium | No inter-warehouse concept | Transfer event needed | Phase 2 |
```

**Severity ratings:**
- 🔴 **Critical** — blocks implementation of the remastered BC or a P0/P1 slice
- 🟡 **Medium** — required for P2 or full realism; known workaround exists
- 🟠 **Low** — quality improvement; doesn't block anything

This list becomes the charter for the **next remaster** in the sequence.

---

## Standard Questions to Always Ask

These questions are not domain-specific — they apply to every remaster. The Facilitator
should surface all of them during the session, regardless of the BC being modeled.

### 1. Notification Ownership

When customer-facing or operator-facing notifications appear in the model (customer emails,
SMS, real-time push), ask: **Who owns the notification?**

- Does this BC publish a domain event that **Correspondence consumes** (choreography)?
- Does this BC **trigger the notification directly** (tight coupling)?
- Does the **Customer Experience BFF** push the event to the UI via SignalR?

If this is not resolved during the session, the implementation prompt will need to decide.
The Fulfillment Remaster left this ambiguous — it produced scenarios describing customer
emails but didn't resolve the ownership question.

### 2. Multi-Aggregate Coordination Mechanism

If the session proposes splitting the current single aggregate into two or more aggregates,
immediately ask: **What is the coordination mechanism?**

- Which aggregate do boundary-crossing events live on?
- What triggers the second aggregate's lifecycle to begin? (Policy handler? Integration message?)
- Can the second aggregate be created independently, or does it always depend on the first?

In the Fulfillment Remaster, `PackingCompleted` on the `WorkOrder` stream triggers the
labeling flow on the `Shipment` stream via a policy handler. This was the correct decision,
but the coordination overhead wasn't fully appreciated during the session. Surface it
explicitly so the ADR documents it.

### 3. P3+ Scope Deferral Decision

Before Phase 4 ends, explicitly ask: **Does any P3+ scope require specialized domain
knowledge or a separate sub-session?** If yes:

- Mark it clearly as P3+ in the slice table with a note: "requires dedicated sub-session"
- List the specific reason (trade compliance, 3PL integration, regulatory, etc.)
- Note it in the session retrospective

### 4. Cross-BC Integration Test Gate

Ask @principal-architect: **Which adjacent BCs have integration tests that must stay green
throughout the implementation milestone?**

This becomes a mandatory bookend in the S1 implementation prompt. Example: the Fulfillment
Remaster required the Orders integration test suite to stay green throughout because the
dual-publish migration touched the Orders saga's existing message handlers.

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
P3+ international or compliance scope may require an additional dedicated session.

### Step 4: Commit Outputs

Required outputs:
- Updated feature files under `docs/features/<bc>/`
- Slice table at `docs/planning/<bc>-remaster-slices.md`
- Session retrospective answering all mandatory open questions
- ADR documenting the remaster rationale and key decisions
- CONTEXTS.md update for the remastered BC
- The gap register (with severity ratings) for the next BC

Recommended additional output:
- **Implementation Pre-Decisions** — a short document (or section in the retrospective)
  recording three decisions that the S1 implementation prompt will need:
  1. Stub infrastructure strategy (what needs a stub — routing engines, external APIs, etc.)
  2. Integration contract migration strategy (dual-publish, versioned events, hard cutover)
  3. P3+ scope deferral confirmation

These don't need to be fully designed during the event modeling session, but surfacing them
prevents the S1 implementation prompt from needing to re-open design questions.

### Step 5: Iterate on This Skill

After each remaster, add a version history entry to this skill file documenting what
was learned and what changed.

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
| 0.2 | 2026-04-06 | Post-Fulfillment event modeling session — added: feature files as domain input, scope complexity signals, standard questions (notification ownership, multi-aggregate coordination, P3 deferral, cross-BC test gate), severity ratings for gap register, Implementation Pre-Decisions output |

*Next update planned after Fulfillment BC Remaster implementation (S1) completes.*
