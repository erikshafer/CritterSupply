# [BC Name] Remaster — Event Modeling Session

> **Template version:** 0.2
> **Skill:** `docs/skills/bc-remaster.md`
> **Instructions:** Replace all `[FILL IN: ...]` blocks with BC-specific content.
> Sections marked 📋 are boilerplate — copy as-is.
> Sections marked ✏️ require domain-specific input.
> Remove this header block before committing the final session prompt.

---

## What "Remaster" Means

📋 *Copy as-is — do not modify.*

This is not a code quality pass. A **remaster** means taking the existing `[BC Name]` BC —
which was built fast with happy-path priority — and producing the version it should have
been: a complete event model with realistic workflows, proper compensation events, and
well-reasoned aggregate boundaries.

The domain is not changing. What changes is the depth and honesty of the model.

The output of this session is **not code**. It is a complete event model artifact:
timeline, slices, scenarios, updated feature files, integration contract implications,
and an ADR documenting the remaster decision. Implementation follows in a subsequent
milestone.

---

## Session Type

📋 *Copy as-is.*

Event Modeling workshop — multi-persona, full five-phase process.

This session is long by design. Don't rush Phase 1 to get to implementation artifacts.
Productive tension between personas is the mechanism — let it run.

---

## Required Reading — Do First, In This Order

✏️ *Fill in all items. Minimum: current aggregate, current handlers, all feature files for
this BC, CONTEXTS.md, bc-remaster skill, event-modeling-workshop skill.*

Every persona reads all of these before the session begins. No exceptions.

### 1. Current implementation

- `src/[BC]/[BC]/[Domain]/[Aggregate].cs` — the aggregate as it exists today. Note how
  thin the event model is. This is the baseline you are redesigning from.

- `src/[BC]/[BC]/[Domain]/[PrimaryHandler].cs` — [describe what this handler does and
  what makes it notable or limiting].

- `[additional files]` — [why each is relevant].

### 2. Feature files

These files describe what the `[BC Name]` domain *should* be. They are almost entirely
unimplemented. **Treat them as domain expert input, not as a design constraint.** Read
them completely — they often represent significant prior analysis that the implementation
never absorbed.

- `docs/features/[bc]/[feature-file-1].feature` — **read completely.** [One-line summary.]

- `docs/features/[bc]/[feature-file-2].feature` — **read completely.** [One-line summary.]

- `[additional feature files]`

### 3. Domain context

- `CONTEXTS.md` — `[BC Name]` and `[Adjacent BC]` entries. Know exactly what is currently
  published, what is consumed, and what the stated constraints are.

- `docs/bc-revamp-candidate-analysis.md` — read the `[BC Name]` section.

### 4. Workshop skills

- `docs/skills/bc-remaster.md` — **read completely.** Particularly: feature files as
  domain input, scope complexity signals, and the standard questions to always ask.

- `docs/skills/event-modeling-workshop.md` — **read completely.** All five phases, the
  multi-persona guidance table, and the "Common Mistakes to Catch" quick reference.

---

## Personas and Agents

📋 *Copy as-is — agent files are consistent across all remasters.*

Run all five workshop personas simultaneously. All agent definitions are at `.github/agents/`.

| Persona | Agent file | Role in this session |
|---|---|---|
| **@event-modeling-facilitator** | `event-modeling-facilitator.md` | Drives phases, maintains timeline, produces artifacts |
| **@product-owner** | `product-owner.md` | Business voice — validates real-world workflows, names events correctly, challenges assumptions |
| **@principal-architect** | `principal-architect.md` | Technical voice — aggregate design, BC boundary decisions, integration contract implications |
| **@qa-engineer** | `qa-engineer.md` | Skeptic — every stage must have a failure mode; every failure mode needs compensation |
| **@ux-engineer** | `ux-engineer.md` | User advocate — what does the operator see? What does the customer see? |

The Facilitator runs the process. The Product Owner and QA Engineer will be the most
active voices in Phase 1. The Architect owns Phase 4 decomposition. The QA Engineer
earns their keep in Phase 2 and Phase 5.

---

## Phase 1 — Brain Dump

### Charter

✏️ *Describe the scope of the brain dump — what domain area, from whose perspective.*

The scope of this brain dump is the **entire `[BC Name]` domain as it should exist**, not
as it currently exists. Use the feature files as prompts, but don't be constrained by them.
Call out any event that represents a meaningful business fact in this domain.

### Seed Events

✏️ *Group seed events by domain area. Derive these primarily from the feature files — they
contain domain expert knowledge. These are starting points only, not exhaustive. The
Facilitator reads them aloud to prime the session. Target 40–80 total after brain dump.*

The Facilitator reads these aloud to prime the session. Personas should surface additional events.

**[Domain Area 1] (e.g., Warehouse / Pre-dispatch):**
`EventOne`, `EventTwo`, `EventThree`, ...

**[Domain Area 2] (e.g., Carrier / In-Transit):**
`EventFour`, `EventFive`, `EventSix`, ...

**[Domain Area 3] (e.g., Compensation / Exceptions):**
`EventSeven`, `EventEight`, `EventNine`, ...

**Additional — let personas surface more during Phase 1:**
What events happen when:
- [Failure scenario 1]?
- [Failure scenario 2]?
- [Edge case 1]?

### Phase 1 Output

An unordered list of all candidate events, collected from all personas.
Target 40–80 events. Do not filter during this phase.

---

## Phase 2 — Storytelling

### Charter

📋 *The first paragraph is boilerplate. The key questions are domain-specific.*

Arrange events into a chronological timeline. There may be multiple parallel swim lanes.

The **@qa-engineer** leads gap-finding here. For every adjacent pair of events on the
timeline, QA Engineer asks: *"What's missing between these two? What can go wrong here?"*

### Key Storytelling Questions

✏️ *Replace with the 3–6 most important structural/design questions for this BC. These
should be genuine design decisions with non-obvious answers — not rhetorical questions.*

The Facilitator must push through these in Phase 2:

1. **[The aggregate boundary question]:** [Describe the specific aggregate boundary
   ambiguity. e.g., "Is the warehouse lifecycle and the carrier lifecycle one Shipment
   aggregate or two?"] Argue both sides.

   - Option A: [Approach and tradeoffs]
   - Option B: [Alternative and tradeoffs]

   **If two or more aggregates are proposed:** immediately ask what the coordination
   mechanism is. Which aggregate holds boundary-crossing events? What triggers the
   second aggregate's lifecycle? Can it be created independently, or does it depend
   on the first? Document the answer in the ADR.

   The @principal-architect owns this argument. The @product-owner provides the
   business tiebreaker.

2. **[Compensation/failure question]:** [e.g., "What does a failed delivery attempt look
   like vs. a terminal delivery failure? Map all intermediate states explicitly."]

3. **[Timing/ownership question]:** [e.g., "When does X actually happen from Y's
   perspective? Is it the same event as Z, or a different fact?"]

4. **[Identity/stream question]:** [e.g., "If we create a reshipment, is it a new stream
   or an event on the original stream?"]

5. **[Routing/responsibility question]:** [e.g., "Where does the routing decision live?"]

### Phase 2 Output

A timeline with events in chronological order across swim lanes.
Gap markers documented. Key design decisions answered.

---

## Phase 3 — Storyboarding

### Charter

📋 *The first paragraph is boilerplate. The prompts are domain-specific.*

Add the three layers around the event timeline: UI wireframes above, commands below,
read models below.

The **@ux-engineer** owns this phase but needs @product-owner to validate workflows and
@principal-architect to flag query feasibility.

### Storyboarding Prompts

✏️ *Fill in the operator/user personas and specific UI questions for this domain.*

For each major stage, the Facilitator asks:

**[Primary operator] view (e.g., warehouse operator, CS agent):**
- What does the [screen/tool] show when [event happens]?
- What does the [actor] see when [failure occurs]?
- [2–3 more specific questions]

**[Secondary persona] view (e.g., customer, vendor):**
- When does the [customer/user] first see [key information]?
- What does the [user] see when [failure/exception occurs]?
- [2–3 more specific questions]

**Read models that must exist:**
- `[ViewName]View` — [what it serves and why]
- `[ViewName]View` — [what it serves and why]
- [others as surfaced by the session]

### Phase 3 Output

Complete storyboard connecting UI → Command → Event(s) → View for every major flow.

---

## Phase 4 — Identify Slices

### Charter

📋 *Copy as-is.*

Draw vertical cuts. Each slice is one independently deliverable feature:
`UI → Command → Event(s) → View`.

The **@principal-architect** drives decomposition. The **@product-owner** prioritizes.
The **@event-modeling-facilitator** enforces that slices are small enough to deliver
in a single session.

### Slice Table Format

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 1 | ... | ... | ... | ... | [Aggregate] | [BC Name] | P0 |

*Note: Include the Aggregate column — if the remaster introduces multiple aggregates,
tracking which slice touches which aggregate is important for implementation sequencing.*

### Required Slices to Identify

✏️ *Fill in required slices by priority tier.*

**P0 (foundation — nothing works without these):**
- [Slice name] — [brief description]
- [Slice name] — [brief description]

**P1 (failure modes — real-world correctness):**
- [Failure scenario] — [brief description]
- [Failure scenario] — [brief description]

**P2 (compensation and advanced):**
- [Compensation workflow]
- [Advanced scenario]

**P3+ (optional — may need dedicated sub-session):**
✏️ *If P3+ scope involves international shipping, trade compliance, regulatory requirements,
or 3PL integration, explicitly note it requires a dedicated sub-session and explain why.*
- [Scope area] — [reason this needs its own session, or "model now, implement later"]

### Phase 4 Output

Populated slice table with at minimum all P0 and P1 slices defined.

---

## Phase 5 — Scenarios

### Charter

📋 *Copy as-is.*

For each P0 and P1 slice, write at least one Given/When/Then scenario. For failure paths,
write the compensation scenario as a separate scenario on the same slice.

The **@qa-engineer** owns this phase. The **@event-modeling-facilitator** keeps scenarios
small — one scenario per meaningful variation.

### Scenario Format

```
Given:  [prior events that set up the starting state]
When:   [the command or system trigger]
Then:   [the expected domain events]
        [the expected read model state]
```

### Required Scenarios

📋 *The pattern is boilerplate; stress-test questions are boilerplate.*

For every P0 slice, write:
1. Happy path scenario
2. At least one failure/edge case

For every P1 slice, write:
1. The failure event scenario
2. The compensation path scenario
3. The idempotency scenario (what if the same failure is reported twice?)

The @qa-engineer should ask for every P1 slice:
- *"What if the same failure message arrives twice?"*
- *"What if the compensation succeeds but the acknowledgement is lost?"*
- *"What if this slice is triggered during a different stage than expected?"*

### Phase 5 Output

Given/When/Then scenarios committed to Gherkin files under `docs/features/[bc]/`.
Update existing feature files — do not create a parallel set.

---

## Mandatory Open Questions

✏️ *Replace with 5–7 domain-specific questions. These MUST be answered before the session
closes. Document the answer for each in the session retrospective.*

The Facilitator must ensure all of these are explicitly answered. Document each answer
in the retrospective.

1. **[Aggregate boundary decision]** [Specific yes/no or either/or]

2. **[Timing/ownership decision]** [When X happens from Y's perspective]

3. **[Terminal state model]** [What is the correct terminal state model for the main failure path?]

4. **[Identity/stream decision]** [Stream identity for a specific remaster concept]

5. **[Routing/responsibility decision]** [Which BC owns which decision]

6. **What are the `[Adjacent BC]` gaps surfaced?** The session must produce a severity-rated
   list of gaps that will feed the `[Adjacent BC]` Remaster.

---

## Standard Questions — Ask in Every Remaster

📋 *These apply to all remasters. Do not skip them.*

The Facilitator raises these before the session closes, regardless of domain.

**Notification ownership:** When customer or operator notifications appear in the model,
who owns them? Does this BC publish a domain event that Correspondence consumes? Does the
Customer Experience BFF push via SignalR? Or does this BC trigger notifications directly?
Resolve this explicitly — leaving it ambiguous causes implementation-time confusion.

**Multi-aggregate coordination:** If two or more aggregates were proposed, what is the
coordination mechanism? Which events live on which stream? What policy handler or integration
message bridges them? Is it possible for the second aggregate's stream to be created
without the first?

**P3+ scope deferral:** Does any P3+ scope require specialized domain knowledge (trade
compliance, regulatory, 3PL integration patterns) that warrants its own focused sub-session?
If yes, note it explicitly in the session retrospective with the reason.

**Cross-BC integration test gate:** Which adjacent BCs have integration tests that must stay
green during the implementation milestone? Document this — it becomes a mandatory gate in
the S1 implementation prompt.

---

## Integration Contract Implications

✏️ *Fill in current contracts from CONTEXTS.md.*

As the model is built, @principal-architect must flag every place where current integration
contracts would change.

**Current contracts (from CONTEXTS.md):**
- `[BC Name]` → `[Adjacent BC]`: publishes `[EventA]`, `[EventB]`
- `[Adjacent BC]` → `[BC Name]`: sends `[CommandA]`
- [additional current contracts]

**Questions the session must answer:**
- Does `[Adjacent BC]`'s saga/handler need new message handlers for `[NewEvent]`?
- Does `[ExistingEvent]` get renamed or replaced?
- What is the proposed migration strategy? (dual-publish, versioned events, hard cutover?)
- [additional contract change questions]

Do not change any contracts during this session. Document implications only.

---

## Session Outputs

✏️ *Fill in BC-specific file paths.*

All outputs are committed to the repository by end of session.

### Required

1. **Updated feature files** under `docs/features/[bc]/`

2. **Slice table** at `docs/planning/[bc]-remaster-slices.md`
   Include the Aggregate column if the remaster introduces multiple aggregates.

3. **Session retrospective** at `docs/planning/milestones/[bc]-remaster-event-modeling-retrospective.md`
   Must answer all mandatory open questions and all standard questions. Must include
   the severity-rated Adjacent BC gap list.

4. **ADR** at `docs/decisions/[XXXX]-[bc]-remaster-rationale.md` (Next ADR: [XXXX+1])
   If multiple aggregates were introduced, the ADR must document the coordination
   mechanism and the arguments considered.

5. **CONTEXTS.md update** for the `[BC Name]` entry

### Recommended

6. **Implementation Pre-Decisions** — a short section in the retrospective (or a standalone
   note) capturing three things the S1 implementation prompt will need to resolve:
   - *Stub infrastructure strategy:* What external dependencies need stub implementations
     for the implementation milestone? (routing engines, carrier APIs, 3PL adapters, etc.)
   - *Integration contract migration strategy:* How will contract changes be introduced
     without breaking existing consumers? (dual-publish, feature flags, versioned schemas)
   - *P3+ scope confirmation:* Which P3+ slices are explicitly deferred, and to what?

### Optional

7. Preliminary aggregate sketches in pseudocode
8. Integration contract delta document (side-by-side of current vs. proposed)

---

## What This Session Is NOT

📋 *Copy as-is.*

- Not an implementation session. No `src/` files are touched.
- Not a retrospective on the current code. The current implementation is baseline context.
- Not constrained by what's easy to implement. Model the right domain first.
- Not a session for adjacent BCs. Note their implications in the Gap Register and move on.

---

## Adjacent BC Gap Register

✏️ *Fill in which BCs this remaster is expected to surface gaps in, and seed with known gaps.
Use severity ratings — they make the register actionable for the next remaster session.*

Throughout the session, the **@principal-architect** maintains a running list of gaps
in adjacent BCs using the severity-rated format below.

**Severity:**
- 🔴 **Critical** — blocks implementation of the remastered BC or a P0/P1 slice
- 🟡 **Medium** — required for P2 or full realism; known workaround exists for now
- 🟠 **Low** — quality improvement; doesn't block anything

**Expected adjacent BCs:**
- `[BC Name]` — [why this BC will likely surface during modeling]

**Known gaps entering the session (seed the register with pre-session analysis):**

| # | Gap | Severity | Current State | Required State | Surfaced During |
|---|---|---|---|---|---|
| 1 | [Gap from pre-session analysis] | 🔴/🟡/🟠 | [Current] | [Required] | Pre-session |

Add rows as the session proceeds. The final register becomes the charter for the next BC Remaster.

---

## Commit Convention

✏️ *Replace [bc-name] with kebab-case BC identifier and [XXXX] with the next ADR number.*

```
[bc-name]-remaster: Phase 1–5 event model — brain dump, timeline, storyboard
[bc-name]-remaster: slice table — P0 through P2 defined
[bc-name]-remaster: feature files — updated scenarios from Phase 5
[bc-name]-remaster: ADR [XXXX] — [bc] remaster rationale
[bc-name]-remaster: session retrospective + adjacent BC gap register
[bc-name]-remaster: CONTEXTS.md — [bc] entry updated
```

---

## Terminology Reference

📋 *Copy as-is.*

Use **"remaster"** consistently throughout this session and all subsequent implementation
milestones. See `docs/skills/bc-remaster.md` for the full definition.
