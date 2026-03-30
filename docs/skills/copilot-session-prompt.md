---
name: copilot-session-prompt
description: >
  Use this skill whenever writing a GitHub Copilot custom agent session prompt for
  CritterSupply. Triggers include: any request to "write a prompt for the agents",
  "write the next session prompt", "generate a Copilot prompt", or any request to
  plan or implement a CritterSupply milestone session using the custom agent roster
  (@PSA, @QAE, @UXE, @ASIE, @EMF, @FEPE, @PO, @DOE). Also use when writing planning
  session prompts, documentation audit prompts, creative domain exercises, or any
  other structured agent session for the CritterSupply project.
---

# CritterSupply — Copilot Session Prompt Skill

This skill standardizes how session prompts are written for CritterSupply's GitHub
Copilot custom agent workflow. Every prompt produced using this skill will have
consistent structure, reliable agent assignments, and non-negotiable session habits
that have proven effective across many milestones.

---

## The Custom Agent Roster

CritterSupply uses eight custom agents defined in `.github/agents/`. Each has a
specific domain of ownership. When writing a prompt, select agents whose strengths
match the session's work — do not invoke all agents for every session.

| Agent | Role | Primary Strengths |
|-------|------|------------------|
| **@PSA** | Principal Software Architect | Implementation lead; Critter Stack patterns; aggregate design; handler correctness; vertical slice structure; cross-BC coordination |
| **@QAE** | QA Engineer | Integration tests; E2E Playwright/Reqnroll; test fixture setup; coverage gaps; `@wip` tagging; test classification |
| **@UXE** | UX Engineer | Blazor component design; user flow coherence; vocabulary alignment between UI and domain; placeholder cleanup; accessibility |
| **@ASIE** | Application Security & Identity Engineer | JWT/cookie auth; `[Authorize]` coverage; policy naming; RBAC; test fixture auth bypass; credential management |
| **@EMF** | Event Modeling Facilitator | Domain naming; ubiquitous language; past-tense event names; Given/When/Then scenarios; state machine review; `*Requested` convention |
| **@FEPE** | Frontend Platform Engineer | Blazor WASM components; MudBlazor patterns; data-testid attributes; component lifecycle; auth state in frontend |
| **@PO** | Product Owner | Domain authority; feature prioritization; acceptance criteria; creative domain content; business rule decisions |
| **@DOE** | DevOps Engineer | CI/CD; GitHub Actions; Docker Compose; RabbitMQ topology; port allocation; CI run number recording |

**Agent selection principles:**
- A focused session with 3–4 agents is better than a diluted session with all 8
- @PSA is nearly always present for implementation sessions
- @QAE is nearly always present; tests are not optional
- @ASIE activates when auth, endpoints, or credentials are touched
- @EMF activates when naming, conventions, or domain modeling decisions arise
- @UXE and @FEPE activate for Blazor component and admin UI work
- @PO leads creative domain exercises and planning scope decisions
- @DOE monitors CI and records run numbers; activates for infrastructure changes

---

## Session Types

Choose the type that matches the request. Each type has a characteristic structure.

### Implementation Session
The most common type. Executes a named set of plan items.
- Has a "Where we are" section with prior session deliverables
- Has a named scope table or list (exactly what this session does)
- Has an execution order
- @PSA drives; @QAE validates; others as needed
- Ends with retrospective + CURRENT-CYCLE.md update + test suite run

### Planning Session
Produces a plan document. No implementation code.
- Has a research phase (agents investigate independently)
- Has a discussion phase (panel answers specific questions)
- Has a plan document phase (produces `m{N}-plan.md`)
- Ends with plan committed + CURRENT-CYCLE.md updated
- Often needs @PSA + @PO + @EMF + @UXE + @QAE + @ASIE

### Documentation Audit Session
Verifies documents against the codebase.
- Treats all documents as hypotheses to be verified, not facts
- Produces findings documents
- Updates stale CURRENT-CYCLE.md, CONTEXTS.md, README.md
- Does not implement features

### Creative Domain Exercise
Produces domain artifacts (vendor catalogs, seed data, Gherkin scenarios).
- @PO leads creative output
- @PSA constrains to actual data shapes
- @EMF reviews naming and vocabulary
- @QAE advises on testability and seed strategy
- Produces Markdown + JSON/structured format

### Closure Session
Closes a milestone and prepares for the next.
- Finishes remaining items from the milestone
- Produces milestone closure retrospective
- Updates CURRENT-CYCLE.md to completed
- Produces pre-planning findings for the next milestone

---

## Prompt Structure

Every session prompt follows this section order. Omit sections that don't apply
to the session type, but never reorder the sections that are present.

### 1. Title
```
# {Milestone} — Session {N}: {Short description of work}
```
For planning sessions: `# {Milestone} — Planning Session`
For closure sessions: `# {Milestone} — Closure Session`

### 2. Where We Are
A factual, concise summary of the prior session's deliverables. Bullet list.
Include:
- Named items completed (not "various improvements")
- Test counts per affected BC at prior session close
- Build state (errors, warnings)
- CI run number if known

```markdown
## Where we are

Session N is merged. [One sentence characterizing the state.]

**Session N deliverables:**
- Item: what was done. BC: X/X tests passing.
- Item: what was done.
- Build: 0 errors, N warnings
```

### 3. Documents to Read
List every document the agents must read before acting. Mandatory skill files
for implementation sessions always include `wolverine-message-handlers.md` and
`marten-event-sourcing.md` when Critter Stack patterns are involved.

```markdown
Read before starting:
- `docs/planning/milestones/m{N}-plan.md` — **read {Track} completely**
- `docs/planning/milestones/m{N}-session-{N}-retrospective.md`
- `docs/skills/wolverine-message-handlers.md` — **mandatory before touching any handler**
- `docs/skills/marten-event-sourcing.md`
```

### 4. What This Session Does
A clear, bounded scope statement. Use a table for implementation sessions.
Always end with an explicit statement of what is NOT in scope.

```markdown
## What this session does

[One sentence summary of the session's goal.]

| Item | BC | Description |
|------|----|-------------|
| X-N  | BC | What this item fixes/builds |

**[Next item group] is not in scope for this session.**
```

### 5. Guard Rails
4–8 non-negotiable constraints. These protect correctness, consistency, and
the patterns the codebase enforces. Each guard rail is a specific, verifiable
statement — not a vague principle.

```markdown
## Guard rails — non-negotiable

1. **[Pattern to enforce.]** [Why it matters / how to verify compliance.]
2. **[Constraint from the plan.]** [Specific files or behaviors it applies to.]
3. **Commit each item separately.** [Item A] is one commit. [Item B] is one commit.
   No batching.
```

**Standard guard rails that appear in nearly every implementation session:**
- Do not start the next track/phase while the current one is incomplete
- Verify `AutoApplyTransactions()` in `Program.cs` before removing `SaveChangesAsync()`
- Test fixtures must bypass ALL authorization policies, not just one
- Each plan item is one commit — no batching
- Do not rename persisted events without a migration ADR

### 6. Execution Order
A dependency-ordered sequence. Use a code block for clarity.

```markdown
## Execution order

```
Item A (no dependencies) →
Item B (depends on A) →
Item C (depends on A) →
Item D (depends on B and C)
```

[Brief note explaining the key dependency if non-obvious.]
```

### 7. Mandatory Session Bookends
This section is **required in every prompt, every session type, no exceptions.**
It defines what happens first and what happens last.

```markdown
## Mandatory session bookends

**First act:** [Specific verification step before writing any code. Usually:
build the solution and confirm baseline; run existing tests; read orientation documents.]

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m{N}-session-{N}-retrospective.md`**

Must cover:
- [Specific item 1 the retrospective must address]
- [Specific item 2 — any surprising findings, decisions made, deviations from plan]
- Test counts per affected BC at session start and session close
- Build state at session close (errors, warnings)
- CI run number confirming green (or explaining any remaining red)
- What the next session should pick up first

**2. Update `CURRENT-CYCLE.md`**

Add "Session N Progress" block. Record completed items, test counts, and CI run number.
Update the "Last Updated" timestamp.

**3. Run and record the full test suite**

Run `dotnet build` (or `dotnet test` if containers are available). Record exact
counts per project. Both the retrospective and CURRENT-CYCLE.md must reference the
same CI run number.
```

### 8. Roles
One section per agent. The format is consistent:
- Agent name as header
- One sentence stating their primary ownership this session
- Bulleted instructions organized by item
- `**Skills:**` line at the end listing relevant skill files

```markdown
## Roles

### @PSA — Principal Software Architect

[One sentence primary ownership.]

**Item X-N — [Item title]**

[What to build, what pattern to follow, what to verify before committing.
Specific enough that a future agent can execute without re-deriving intent.
Reference exact file paths, class names, and method signatures where known.]

**Skills:** `wolverine-message-handlers`, `marten-event-sourcing`

---

### @QAE — QA Engineer

[One sentence primary ownership.]

**After [PSA item] is committed:**
- `[TestMethodName]` — [what it verifies]
- `[TestMethodName]` — [what it verifies]

[Write tests promptly after each handler — do not defer all tests to the end.]

**Skills:** `critterstack-testing-patterns`
```

**Roles section principles:**
- State what agent is NOT responsible for (parallel workstreams)
- For supporting roles, say "supporting role this session" and name the specific
  questions they may be asked to answer
- For idle agents, give them productive orientation work ("read Session N+1 items
  so you are ready without a cold start")
- Never leave an active agent without a named deliverable

### 9. Session Habits
A short closing section. Consistent across all implementation prompts.

```markdown
## Session habits

Commit frequently and atomically. [Item A] is one commit. [Item B] is one commit.
[If applicable: suggested commit message format: `M{N}.{x} {Item}: {BC} — {description}`]

[One sentence reinforcing the most important quality constraint for this specific session.]

The retrospective and CURRENT-CYCLE.md update are required deliverables equal in weight
to any code commit. [One sentence on why this session's retrospective is particularly
important — what it establishes for future sessions.]
```

---

## Retrospective Requirements

The retrospective is non-optional. It is a first-class deliverable, not a summary.
Every retrospective must be committed before the session ends.

**Minimum required content for every session retrospective:**

| Section | Content |
|---------|---------|
| Date and scope | Session number, focus area, outcome summary |
| Items completed | Named items with specific evidence (file paths, counts, changes) |
| Items not completed | If any; honest reason; pickup instructions for next session |
| Surprising findings | Anything not in the plan — root cause classifications, pattern discoveries, decisions made during implementation |
| Test counts | Per-project breakdown at session start and end |
| Build state | Errors and warnings at session close |
| CI run number | The run that confirms green |
| Next session pickup | Specific first item; any prerequisites to verify |

**For planning sessions, additionally:**
- Decision log: every decision made (D-N format), who made it, confidence level
- Deferred items: what was explicitly excluded and why
- Open questions: anything that requires owner input before implementation begins

**For milestone closure retrospectives:**
- Summary of all tracks delivered
- Key lessons learned — patterns discovered, anti-patterns corrected, decisions
  that proved right or wrong
- What the next milestone inherits (named items, not vague "remaining work")
- Test counts at milestone close (unit, integration, E2E per project)
- CI run number confirming final green state

---

## CURRENT-CYCLE.md Update Protocol

Every session ends with a CURRENT-CYCLE.md update. The format is consistent.

**Adding a session progress block:**
```markdown
**Session N Progress (YYYY-MM-DD):**
- ✅ **[Item ID] ([Short title]):** [What was done]. **X/X tests passing.**
- ✅ **[Item ID] ([Short title]):** [What was done].
- ✅ **Full solution build:** 0 errors, N warnings (unchanged)
- ✅ **Session N Retrospective:** [link]
```

**Status icons:**
- ✅ Complete
- ⚠️ Partial or warning
- ❌ Not done
- ⏳ Pending CI

**When to update what:**
- Session start → update "Active Milestone" status to `🚀 IN PROGRESS — Session N`
- Session end → add Session N Progress block; update test counts; update CI run
- Milestone close → move active milestone to Recent Completions; set next milestone as active in planning status
- After three milestones in Recent Completions → move oldest to Milestone Archive

---

## Guard Rail Reference

The following guard rails appear repeatedly across sessions. When relevant to the
session's work, include them verbatim or adapted.

### Critter Stack patterns
```
**Verify `AutoApplyTransactions()` before removing any `SaveChangesAsync()`.**
Open `{BC}.Api/Program.cs` and confirm `opts.Policies.AutoApplyTransactions()` is
present before removing a manual save. EF Core BCs need `UseEntityFrameworkCoreTransactions()`
instead — do not remove `SaveChangesAsync()` from EF Core handlers.
```

```
**Do not return tuples from handlers that manually load aggregates.**
Tuple returns (`return (events, messages)`) only work with Wolverine's `[AggregateHandler]`
/ `[WriteAggregate]` pattern. If a handler calls `session.LoadAsync<T>()` manually,
use explicit session operations instead.
```

```
**`bus.ScheduleAsync()` is the only justified `IMessageBus` injection in handlers.**
All integration event publishing uses `OutgoingMessages` return. `bus.PublishAsync()`
for integration events is an anti-pattern that bypasses the transactional outbox.
```

### Authorization
```
**Test fixtures must bypass ALL authorization policies, not just one.**
Enumerate every `[Authorize(Policy = "...")]` used by the BC's endpoints and bypass
all of them. A fixture that bypasses `CustomerService` but not `FinanceClerk` will
produce intermittent 401s that look like flakiness.
```

```
**Every new BC API must have `[Authorize]` on all non-auth endpoints from the first commit.**
Auth endpoints (login, logout, refresh) must be explicitly `[AllowAnonymous]`. This
documents intent and protects against future middleware ordering changes.
```

### Naming and events
```
**Do not rename persisted events without a migration ADR.**
Renaming an event class that has been written to a Marten event stream requires a
migration strategy. Stop and create an ADR first.
```

```
**Events are past tense. Commands are imperative. No exceptions.**
`OrderPlaced` ✓, `PlaceOrder` ✓ — `PlacingOrder` ✗, `OrderPlacement` ✗.
```

### Commits and scope
```
**Commit each item separately. [Item A] is one commit. [Item B] is one commit.**
This enables clean revert if any single change introduces a regression and keeps
the git history readable as a record of the quality work.
```

```
**Do not start the next track while the current one is incomplete.**
If a session is scoped to Track B, do not begin Track C items even if a Track B
item reveals an obvious Track C fix nearby. Document it. Fix it in the next session.
```

---

## Anti-Patterns to Avoid

These patterns have caused problems in previous sessions. Do not reproduce them.

**In prompts:**
- Vague scope ("improve naming throughout") — always name specific files, classes, or
  items from the plan
- Batching multiple guard rails into one ("follow all patterns") — each guard rail is
  a separate numbered item
- Omitting the retrospective requirement — it is always mandatory; its absence
  makes the milestone's history invisible to future agents
- Assigning all 8 agents to a session — idle agents dilute focus; assign only the
  agents whose strengths match the session's work
- Leaving an agent without a named deliverable — every active agent has at least one
  specific thing to produce

**In implementation:**
- Double-touching files across sessions without explicit plan separation (e.g., removing
  `SaveChangesAsync()` AND restructuring the file in the same commit)
- "Just fixing" a violation discovered incidentally — document it, fix it in the
  right session
- Retrospectives written from memory at the end rather than documented throughout
- CURRENT-CYCLE.md updates that say "session complete" without named items and test counts

---

## Quick Reference: Prompt Checklist

Before finalizing any session prompt, verify:

- [ ] Title follows `{Milestone} — Session {N}: {Short description}` format
- [ ] "Where we are" names specific deliverables from the prior session with test counts
- [ ] Scope is bounded — what this session does AND what it does not do
- [ ] Guard rails are numbered, specific, and verifiable
- [ ] Execution order respects dependencies and explains non-obvious sequencing
- [ ] Mandatory bookends section is present with all three required last acts
- [ ] Every active agent has at least one named deliverable
- [ ] Retrospective requirements specify what the retro must cover (not just "write a retro")
- [ ] CURRENT-CYCLE.md update is listed as a required last act
- [ ] Session habits section closes the prompt

---

## Reference: Established Conventions

These conventions are stable across the project and should be referenced but not
redefined in individual session prompts.

| Convention | Location |
|------------|----------|
| Vertical slice structure (command + handler + validator + events in one file) | ADR 0039 |
| `AutoApplyTransactions()` required in every Marten BC's `Program.cs` | M36.0 Session 6 retro |
| Shared `TestAuthHandler` + `AddTestAuthentication()` | `tests/Shared/CritterSupply.TestUtilities/` |
| `*Requested` suffix for command-intent integration messages | M36.0 Session 3, C-7 |
| UUID v5 stream IDs with BC-specific namespace prefix | ADR 0042 |
| `[AllowAnonymous]` required explicitly on all auth endpoints | M36.0 Track D |
| `OutgoingMessages` for all integration event publishing | M36.0 Track B |
| `bus.ScheduleAsync()` is the only justified `IMessageBus` use in handlers | M36.0 Session 2 retro |
| Commit message format: `M{N}.{x} {Item}: {BC} — {description}` | Established M36.0 |
