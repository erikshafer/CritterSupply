---
name: event-modeling-workshop
description: >
  Facilitate an Event Modeling workshop session for designing information systems.
  Use this skill when the user wants to run, simulate, plan, or get guidance on an
  Event Modeling session — including brain dumps, timeline construction, slice
  definition, scenario writing, or any phase of the workshop.
  Also use when the user asks about multi-persona facilitation of an Event Modeling
  session, or wants Claude to play multiple roles (facilitator, domain expert,
  developer, skeptic) during the exercise.
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

### Phase 2 — Storytelling
Arrange events into a coherent narrative on the timeline.
Ask: *"What happened first? What does this enable next?"*
Gaps in the story reveal missing events.

### Phase 3 — Storyboarding
Add UI wireframes above the timeline and views below.
Connect them to their triggering commands and resulting events.
This makes the full user journey visible.

### Phase 4 — Identify Slices
Draw vertical cuts through the model — each slice is one complete feature:
`UI → Command → Event(s) → View`
Slices become your work units (stories, tickets, PRs).

### Phase 5 — Scenarios (Given/When/Then)
For each slice, write acceptance scenarios:
- **Given**: the events already in the stream (preconditions)
- **When**: the command issued
- **Then**: the new events produced and/or the view state

---

## Output Artifacts

- **The Event Model** — the full visual blueprint (primary deliverable)
- **Slice definitions** — vertical feature cuts, each independently deliverable
- **Given/When/Then scenarios** — acceptance criteria per slice
- **API contracts** — command shapes and read model schemas emerge naturally
- **Aggregate/projection sketches** — implementation starting points

---

## Multi-Persona Facilitation

When facilitating or simulating a workshop, Claude can invoke distinct personas
to represent different stakeholders. This surfaces conflicts, blind spots, and
richer domain understanding than a single voice would produce.

### Invoking Personas

Declare which personas are active at the start of a session or phase.
Each persona should be clearly labeled when speaking.

**Recommended Core Personas:**

| Persona | Role | Behavior |
|---|---|---|
| **Facilitator** | Keeps the workshop moving | Asks clarifying questions, resolves naming disputes, time-boxes phases |
| **Domain Expert** | Owns the business language | Corrects event names, challenges assumptions, provides real-world examples |
| **Developer** | Thinks in implementation | Asks "how would we query that?", "what triggers this?", flags technical debt |
| **Skeptic** | Stress-tests the model | Asks "what if this fails?", "what about the edge case where...?" |
| **User / Customer** | Grounds it in reality | Asks "but why would I care about this screen?", "what does this tell me?" |

### How to Run Multi-Persona Mode

```
[FACILITATOR] Let's start the brain dump. Call out any events you know happen in this system.

[DOMAIN EXPERT] "CustomerOnboarded" — but only after they've verified their email. 
  That verification step is a separate event: "EmailVerified".

[DEVELOPER] Where does "CustomerOnboarded" get read? Is there a dashboard that shows 
  new signups? We'll need a view for that.

[SKEPTIC] What happens if the email verification expires? Is there an "EmailVerificationExpired" 
  event, or do we just let it silently fail?

[FACILITATOR] Good catch. Let's add "EmailVerificationExpired" to the board and 
  revisit the flow around it.
```

Personas may agree, disagree, and build on each other.
The goal is productive tension — not consensus for its own sake.

### When to Use Which Persona

- **Phase 1 (Brain Dump)**: Facilitator + Domain Expert dominate
- **Phase 2 (Storytelling)**: All personas contribute; Skeptic earns their keep here
- **Phase 3 (Storyboarding)**: Developer + User/Customer are most active
- **Phase 4 (Slicing)**: Facilitator + Developer drive prioritization
- **Phase 5 (Scenarios)**: Developer + Domain Expert write; Skeptic validates

---

## Reference Files

| File | When to load it |
|---|---|
| `references/scenarios.md` | When writing Given/When/Then scenarios for any slice — includes CritterSupply-specific examples across all seven bounded contexts, read model assertion patterns, and common pitfalls |

---

## Quick Reference: Common Mistakes to Catch

- Events named as commands: "CreateOrder" ❌ → "OrderCreated" ✓
- Missing the "why" behind a command — add a UI wireframe to show the trigger
- Views that can't be derived from the events on the board — you're missing events
- Slices too large to deliver independently — keep slicing
- Scenarios that test infrastructure instead of behavior — focus on domain facts
