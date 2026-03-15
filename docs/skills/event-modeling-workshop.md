---
name: event-modeling-workshop
description: >
  Facilitate an Event Modeling workshop session for designing information systems.
  Use this skill when the user wants to run, simulate, plan, or get guidance on an
  Event Modeling session — including brain dumps, timeline construction, slice
  definition, scenario writing, or any phase of the workshop.
  Also use when the user asks about multi-persona facilitation of an Event Modeling
  session, or wants to invoke CritterSupply agent personas (Product Owner, Principal
  Architect, UX Engineer, QA Engineer) during the exercise. A Facilitator role may be
  advantageous to ensure objectives are met and discussions moves forward.
---

# Event Modeling Workshop Skill

Event Modeling is a collaborative workshop technique created by Adam Dymitruk (Adaptech Group)
for designing information systems. It produces a visual, timeline-based blueprint showing how
data flows through a system — from user intent through state changes to read-side projections.
It works for any information system, not just event-sourced ones, but maps naturally onto
CQRS and event sourcing patterns.

## The Four Building Blocks

| Block | Color | Meaning |
|---|---|---|
| **Events** | Orange | Facts that occurred — past tense, immutable |
| **Commands** | Blue | User intentions or system requests that cause events |
| **Views / Read Models** | Green | Projections of event data back to the UI |
| **UI Wireframes / Screens** | White | What the user actually sees and interacts with |

Arrange these in chronological order on a horizontal timeline, in swim lanes:
`UI → Command → Event Stream → View → UI`

---

## Workshop Phases

### Phase 1 — Brain Dump

Everyone writes events as fast as possible — no ordering, no judgment.
Events are facts: past tense, concrete, meaningful to the domain.
> "OrderPlaced", "PaymentFailed", "ItemShipped"

**Input:** A domain or feature area to explore (e.g., "Returns workflow", "Checkout redesign")
**Process:** Each persona calls out events. No filtering, no sequencing — volume over accuracy.
**Output:** Unordered list of candidate events (expect 15–60 for a single bounded context)

### Phase 2 — Storytelling

Arrange events into a coherent narrative on the timeline.
Ask: *"What happened first? What does this enable next?"*
Gaps in the story reveal missing events.

**Input:** Unordered event list from Phase 1
**Process:** Place events left-to-right on the timeline. Fill gaps: "What happened between X and Y?"
**Output:** Chronologically ordered event timeline with gap markers resolved

### Phase 3 — Storyboarding

Add UI wireframes above the timeline and views below.
Connect them to their triggering commands and resulting events.
This makes the full user journey visible.

**Input:** Ordered event timeline from Phase 2
**Process:** For each event, ask: "What UI triggered this?" (add screen above) and "What does the user see after?" (add view below). Connect with commands.
**Output:** Full storyboard: `UI → Command → Event(s) → View → UI` for the entire flow

### Phase 4 — Identify Slices

Draw vertical cuts through the model — each slice is one complete feature:
`UI → Command → Event(s) → View`
Slices become your work units (stories, tickets, PRs).

**Input:** Complete storyboard from Phase 3
**Process:** Draw vertical lines. Each slice must be independently deliverable and testable.
**Output:** Slice table (see [Structured Output Format](#structured-output-format-for-slices) below)

### Phase 5 — Scenarios (Given/When/Then)

For each slice, write acceptance scenarios:
- **Given**: the events already in the stream (preconditions)
- **When**: the command issued
- **Then**: the new events produced and/or the view state

**Input:** Slice definitions from Phase 4 + `references/scenarios.md` for patterns
**Process:** Write happy path first, then edge cases and failure modes per slice.
**Output:** Given/When/Then scenarios per slice (see `references/scenarios.md` for CritterSupply examples)

---

## Structured Output Format for Slices

When producing slices (Phase 4), use this table format:

| # | Slice Name | Command | Events | View | BC | Priority |
|---|-----------|---------|--------|------|----|----------|
| 1 | Add item to cart | `AddItemToCart` | `ItemAddedToCart` | CartView (updated count, total) | Shopping | P0 |
| 2 | Remove item from cart | `RemoveItemFromCart` | `ItemRemovedFromCart` | CartView (updated count, total) | Shopping | P0 |
| 3 | Initiate checkout | `InitiateCheckout` | `CheckoutInitiated` | CheckoutView (address, payment) | Orders | P0 |
| 4 | Reject expired cart | *(system timer)* | `CartExpired` | — (cart cleared) | Shopping | P1 |

**Column definitions:**
- **Slice Name**: Human-readable feature name
- **Command**: The command that enters the system (user or system-initiated)
- **Events**: Domain events produced (comma-separated if multiple)
- **View**: The read model or UI state updated after the event
- **BC**: Bounded context that owns this slice (verify against `CONTEXTS.md`)
- **Priority**: P0 = must-have, P1 = should-have, P2 = nice-to-have

---

## Output Artifacts

- **The Event Model** — the full visual blueprint (primary deliverable)
- **Slice definitions** — vertical feature cuts, each independently deliverable (table above)
- **Given/When/Then scenarios** — acceptance criteria per slice
- **API contracts** — command shapes and read model schemas emerge naturally
- **Aggregate/projection sketches** — implementation starting points

---

## Multi-Persona Facilitation

When facilitating or simulating a workshop, invoke distinct personas to represent
different stakeholder perspectives. This surfaces conflicts, blind spots, and richer
domain understanding than a single voice would produce.

### CritterSupply Agent Personas

CritterSupply has four custom agents that map naturally to Event Modeling workshop roles.
Each agent has a detailed behavioral profile in `.github/agents/`.

| Persona | Agent | Role | Asks questions like... |
|---|---|---|---|
| **Domain Expert** | **Product Owner** (`@product-owner`) | Owns the business language; corrects event names, validates workflows against real e-commerce practices, challenges assumptions | *"That's not how returns work — the customer chooses refund vs. exchange before we inspect."* |
| **Technical Voice** | **Principal Architect** (`@principal-architect`) | Thinks in implementation; flags BC boundaries, aggregate design, query feasibility, technical debt | *"How would we query that view? We'd need a multi-stream projection across Orders and Fulfillment."* |
| **User Advocate** | **UX Engineer** (`@ux-engineer`) | Grounds the model in the user's experience; asks what the user sees, feels, and needs at each step | *"But why would a customer care about this screen? What decision does it help them make?"* |
| **Skeptic** | **QA Engineer** (`@qa-engineer`) | Stress-tests the model; asks about failures, edge cases, race conditions, missing compensation | *"What if payment succeeds but inventory reservation fails? Where's the compensation event?"* |

> **Agent definition files:** `.github/agents/principal-architect.md`, `.github/agents/product-owner.md`, `.github/agents/ux-engineer.md`, `.github/agents/qa-engineer.md`

### How to Run Multi-Persona Mode

```
[PRODUCT OWNER] "CustomerOnboarded" — but only after they've verified their email.
  That verification step is a separate event: "EmailVerified".

[PRINCIPAL ARCHITECT] Where does "CustomerOnboarded" get read? Is there a dashboard
  that shows new signups? We'll need a view for that.

[QA ENGINEER] What happens if the email verification expires? Is there an
  "EmailVerificationExpired" event, or do we just let it silently fail?

[UX ENGINEER] When verification expires, what does the customer see? A dead link?
  We need an "expired verification" screen that lets them request a new one.
```

Personas may agree, disagree, and build on each other.
The goal is productive tension — not consensus for its own sake.

### Which Personas Lead Each Phase

| Phase | Primary Voices | Why |
|---|---|---|
| **Phase 1 (Brain Dump)** | Product Owner + Principal Architect | PO knows the business events; PSA knows the technical events |
| **Phase 2 (Storytelling)** | All four — QA Engineer earns their keep here | QA finds gaps in the narrative; UXE maps events to user moments |
| **Phase 3 (Storyboarding)** | UX Engineer + Product Owner | UXE designs what users see; PO validates business workflows |
| **Phase 4 (Slicing)** | Principal Architect + Product Owner | PSA drives technical decomposition; PO prioritizes business value |
| **Phase 5 (Scenarios)** | QA Engineer + Principal Architect | QAE writes edge cases; PSA validates implementation feasibility |

---

## CritterSupply Integration

### How Workshop Outputs Connect to Existing Artifacts

Workshop outputs feed directly into CritterSupply's development workflow:

| Workshop Output | CritterSupply Artifact | Location |
|---|---|---|
| **Slices** | GitHub Issues (one issue per slice) | GitHub Milestones |
| **Scenarios (Given/When/Then)** | Gherkin `.feature` files | `docs/features/<bc>/` |
| **BC discovery / boundary changes** | Update or verify | `CONTEXTS.md` |
| **Architectural decisions surfaced** | ADR markdown files | `docs/decisions/` |
| **Event / command shapes** | Integration message contracts | `src/Shared/Messages.Contracts/` |
| **View / read model designs** | Marten projection implementations | `src/<BC>/<Project>/` |

### Mini Example: Returns BC — Request a Return

**Phase 1 (Brain Dump):**
> ReturnRequested, ReturnApproved, ReturnDenied, ReturnReceived, RefundIssued,
> ReturnExpired, ReturnRejected, ReturnCompleted, ReturnShipped, InspectionPassed,
> InspectionFailed, ReturnLabelGenerated

**Phase 4 (Slice):**

| # | Slice Name | Command | Events | View | BC | Priority |
|---|-----------|---------|--------|------|----|----------|
| 1 | Request a return | `RequestReturn` | `ReturnRequested` | ReturnStatusView (pending) | Returns | P0 |
| 2 | Approve return | `ApproveReturn` | `ReturnApproved`, `ReturnLabelGenerated` | ReturnStatusView (approved, label) | Returns | P0 |
| 3 | Deny return | `DenyReturn` | `ReturnDenied` | ReturnStatusView (denied, reason) | Returns | P0 |

**Phase 5 (Scenario for Slice 1):**
```
Given:  OrderPlaced { orderId: "ord-1", customerId: "cust-1", items: ["SKU-100"] }
        ShipmentDelivered { orderId: "ord-1" }
When:   RequestReturn { orderId: "ord-1", items: ["SKU-100"], reason: "Wrong size" }
Then:   ReturnRequested { returnId: "ret-1", orderId: "ord-1", reason: "Wrong size" }
        ReturnStatusView: { returnId: "ret-1", status: "Pending", reason: "Wrong size" }
```

---

## Reference Files

| File | When to load it |
|---|---|
| `references/scenarios.md` | Phase 5 — includes CritterSupply-specific Given/When/Then examples across bounded contexts, read model assertion patterns, and common pitfalls |
| `CONTEXTS.md` | Phase 1 and Phase 4 — verify BC ownership and integration directions when assigning slices |

---

## Quick Reference: Common Mistakes to Catch

- Events named as commands: "CreateOrder" ❌ → "OrderCreated" ✓
- Missing the "why" behind a command — add a UI wireframe to show the trigger
- Views that can't be derived from the events on the board — you're missing events
- Slices too large to deliver independently — keep slicing
- Scenarios that test infrastructure instead of behavior — focus on domain facts
- Assigning a slice to the wrong BC — check `CONTEXTS.md` for ownership boundaries
- Skipping the Skeptic voice — edge cases found late are expensive to fix
