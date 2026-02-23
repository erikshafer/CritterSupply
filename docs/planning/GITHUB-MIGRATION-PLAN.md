# GitHub Projects & Issues Migration Plan

> **Decision:** [ADR 0011](../decisions/0011-github-projects-issues-migration.md)  
> **Status:** 📋 Ready to Execute  
> **Supersedes:** `CYCLES.md` and `BACKLOG.md` as tracking sources (those files become deprecated)

---

## Overview

This plan migrates CritterSupply's planning and task tracking from markdown files to **GitHub Projects v2 + GitHub Issues**, while keeping AI-context documents (architecture, skills, ADRs) in the repository.

The result is a **hybrid model**:
- 🟦 **GitHub** = Live work tracking (issues, milestones, project boards)
- 🟩 **Repo markdown** = AI context + long-form reference (CONTEXTS.md, skills/, ADRs)

---

## Part 1: Understanding GitHub Projects vs. What We Have

### Current State → GitHub Equivalent

| What We Have | GitHub Replacement | Notes |
|---|---|---|
| `CYCLES.md` "Current Cycle" section | **GitHub Milestone** (e.g., `Cycle 19`) | Active milestone shows open/closed issues |
| `CYCLES.md` "Upcoming Cycles" section | **GitHub Project – Backlog view** with `status:planned` | Drag to reorder priority |
| `CYCLES.md` "Recently Completed" section | **Closed GitHub Milestones** | Each closed milestone = completed cycle |
| `BACKLOG.md` work items | **GitHub Issues** with label `status:backlog` | Structured metadata replaces prose |
| `cycle-NN-*.md` task checklists | **Sub-issues** linked to a parent "Cycle Epic" Issue | Each checkbox becomes a trackable issue |
| `cycle-NN-*.md` retrospective notes | Stays as markdown; linked from cycle Issue | Long-form notes stay in files |
| ADR companion tracking | GitHub Issue with label `type:adr` | Points to the markdown file |
| Manual progress tracking | **GitHub Project Roadmap view** | Visual timeline |

### GitHub Projects v2 Layout Options

You can switch between views on the same project:

| View | When to Use |
|---|---|
| **Board** | Day-to-day work (like Kanban/JIRA board). Columns: Backlog → In Progress → Review → Done |
| **Table** | Sprint planning. See all issues with their fields in a spreadsheet-style view |
| **Roadmap** | Cross-cycle planning. Visual timeline of milestones and their issues |

---

## Part 2: One-Time Setup

### Step 1: Create the GitHub Project

1. Go to `https://github.com/erikshafer/CritterSupply`
2. Click **Projects** tab → **New Project**
3. Select **Board** as the starting template (you can add other views later)
4. Name it: **"CritterSupply Development"**
5. Set visibility to **Private** (if preferred) or **Public**

### Step 2: Add Custom Fields to the Project

In the Project settings, add these custom fields:

| Field Name | Type | Options / Notes |
|---|---|---|
| `Bounded Context` | Single Select | Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience, Vendor Portal, Returns, Infrastructure, Documentation, Testing |
| `Priority` | Single Select | 🔴 High, 🟡 Medium, 🟢 Low |
| `Effort (Sessions)` | Number | Estimated 2-hour sessions |
| `Cycle` | Iteration | Auto-creates iterations; or use Milestone instead |
| `Type` | Single Select | Feature, Bug, ADR, Retrospective, Infrastructure, Spike |

> **Tip:** `Iteration` field in GitHub Projects is similar to a JIRA Sprint field. Alternatively, use GitHub Milestones for cycles (recommended — simpler, better integration with Issues).

### Step 3: Create the Label Taxonomy

Run these labels in the repository (Settings → Labels):

**Bounded Context Labels** (prefix `bc:`)
```
bc:orders          color: #0075ca
bc:payments        color: #e4e669
bc:shopping        color: #d93f0b
bc:inventory       color: #0e8a16
bc:fulfillment     color: #1d76db
bc:customer-identity  color: #5319e7
bc:product-catalog    color: #f9d0c4
bc:customer-experience color: #c2e0c6
bc:vendor-portal   color: #bfdadc
bc:returns         color: #fef2c0
bc:infrastructure  color: #cccccc
```

**Type Labels** (prefix `type:`)
```
type:feature       color: #0075ca
type:bug           color: #d73a4a
type:adr           color: #e4e669
type:spike         color: #cfd3d7
type:retrospective color: #ffffff
type:documentation color: #0075ca
type:testing       color: #d4c5f9
```

**Status Labels** (prefix `status:`)
```
status:backlog     color: #ededed
status:planned     color: #c5def5
status:in-progress color: #fbca04
status:blocked     color: #d73a4a
status:deferred    color: #cccccc
```

**Priority Labels** (prefix `priority:`)
```
priority:high      color: #d73a4a
priority:medium    color: #fbca04
priority:low       color: #0e8a16
```

### Step 4: Create Milestones for Each Cycle

Create one Milestone per development cycle:

| Milestone | Due Date | Description |
|---|---|---|
| Cycle 19: Authentication & Authorization | TBD | Customer Identity BC auth integration |
| Cycle 20: Automated Browser Testing | TBD | Playwright/Selenium/bUnit evaluation |
| Cycle 21+: Vendor Portal Phase 1 | TBD | Vendor-facing product management |

For past cycles (historical reference):
- Create as **closed** milestones with their completion dates
- Link to their cycle markdown files in the description

### Step 5: Configure Project Views

**View 1: Board (Default)**
- Status columns: `Backlog` | `Planned` | `In Progress` | `In Review` | `Done`
- Group by: Status
- Filter: `is:open` (hide closed)

**View 2: Backlog Table**
- Layout: Table
- Columns: Title, Bounded Context, Priority, Effort, Milestone
- Filter: `status:backlog`
- Sort by: Priority

**View 3: Active Cycle**
- Layout: Board
- Filter: `milestone:"Cycle 19: Authentication & Authorization"`
- Group by: Type

**View 4: Roadmap**
- Layout: Roadmap
- Group by: Milestone
- Date field: Milestone due dates

---

## Part 3: Content Migration

### Phase A: Migrate the Backlog

#### Items to Convert from `docs/planning/BACKLOG.md`

Create one GitHub Issue per backlog item:

---

**Issue: Authentication & Authorization (Cycle 19)**
```
Title: [Auth] Replace stub customerId with Customer Identity BC authentication

Labels: bc:customer-experience, type:feature, priority:medium, status:planned
Milestone: Cycle 19: Authentication & Authorization

Body:
## Description
Replace hardcoded stub customerId with real authentication via Customer Identity BC.

## Tasks
- [ ] Create ADR for authentication strategy (cookie/JWT, session storage)
- [ ] Implement authentication in Storefront.Web
- [ ] Call Customer Identity BC for login/logout
- [ ] Store customerId in session/claims
- [ ] Update Cart.razor, Checkout.razor to use authenticated customerId
- [ ] Add authorization policies (protected routes)
- [ ] Add Login/Logout pages with MudBlazor forms
- [ ] Add "Sign In" / "My Account" buttons to AppBar

## Acceptance Criteria
- Users must log in to access cart/checkout
- CustomerId comes from authenticated session (no hardcoded GUIDs)
- Logout clears session
- Protected routes redirect to login page
- Session persists across browser refreshes

## Dependencies
- Customer Identity BC complete ✅
- Cycle 18 complete ✅

## Effort
2-3 sessions

## References
- [cycle-17-customer-experience-enhancement.md](../planning/cycles/cycle-17-customer-experience-enhancement.md)
```

---

**Issue: Automated Browser Testing**
```
Title: [Testing] Automated browser tests for Customer Experience Blazor UI

Labels: bc:customer-experience, type:testing, priority:medium, status:backlog
Milestone: Cycle 20: Automated Browser Testing

Body:
## Description
Evaluate and implement automated browser tests for Storefront.Web Blazor UI.

## Tasks
- [ ] Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
- [ ] Set up test infrastructure (TestContainers + browser automation)
- [ ] Automated tests for cart page rendering and SSE connection
- [ ] Automated tests for checkout wizard navigation (4 steps)
- [ ] Automated tests for order history table
- [ ] Automated tests for real-time SSE updates (end-to-end)
- [ ] Add to CI/CD pipeline

## Acceptance Criteria
- All manual test scenarios from cycle-16-phase-3-manual-testing.md are automated
- Tests run in CI/CD pipeline
- No flaky tests
- Tests complete in <5 minutes

## Effort
2-3 sessions
```

---

**Issue: .NET Aspire Orchestration**
```
Title: [Infrastructure] Replace docker-compose with .NET Aspire for local orchestration

Labels: bc:infrastructure, type:feature, priority:medium, status:backlog

Body:
## Description
Replace docker-compose with .NET Aspire for local development orchestration.

## Tasks
- [ ] Create Aspire AppHost project
- [ ] Configure all BCs as Aspire resources
- [ ] Configure Postgres, RabbitMQ as Aspire resources
- [ ] Update README.md with Aspire instructions
- [ ] Migrate from docker-compose to Aspire

## Acceptance Criteria
- Single `dotnet run` starts entire stack
- Aspire dashboard shows all services + dependencies
- Developer experience improved

## Effort
3-4 sessions

## References
- [ASPIRE-*.md documents](../../docs/) — analysis already done
```

---

**Issue: Property-Based Testing**
```
Title: [Testing] Add property-based tests with FsCheck for domain invariants

Labels: type:testing, priority:low, status:backlog

Body:
## Tasks
- [ ] Add FsCheck property tests for Order aggregate invariants
- [ ] Add FsCheck property tests for Inventory reservation logic
- [ ] Document property-based testing patterns in skills/

## Notes
FsCheck is already in Directory.Packages.props ✅

## Effort
1-2 sessions
```

---

**Issue: Vendor Portal BC**
```
Title: [BC] Vendor Portal bounded context — Phase 1

Labels: bc:vendor-portal, type:feature, priority:low, status:backlog

Body:
## Description
Vendor-facing portal for managing products, viewing orders, and analytics.

## Features
- Vendor authentication (Vendor Identity BC)
- Product management (CRUD in Product Catalog)
- Order fulfillment view
- Analytics dashboard

## Effort
5-8 sessions
```

---

**Issue: Returns BC**
```
Title: [BC] Returns bounded context

Labels: bc:returns, type:feature, priority:low, status:backlog

Body:
## Description
Handle return authorization and processing.

## Features
- Return request submission (customers)
- Return authorization (customer service)
- Refund processing (integration with Payments BC)
- Inventory restocking (integration with Inventory BC)

## References
- [docs/features/returns/return-request.feature](../features/returns/return-request.feature)

## Effort
3-5 sessions
```

---

### Phase B: Create the Active Cycle Issue Structure

For Cycle 19 (next active cycle), create:

1. **Parent "Cycle Epic" Issue:**
```
Title: 🚀 Cycle 19: Authentication & Authorization

Labels: type:feature, priority:high
Milestone: Cycle 19: Authentication & Authorization

Body:
## Objective
Wire real authentication into Customer Experience BC.
Replace stub customerId with authenticated session from Customer Identity BC.

## Key Deliverables
- [ ] #[auth-issue-number] Authentication strategy ADR
- [ ] #[auth-issue-number] Storefront.Web login/logout pages
- [ ] #[auth-issue-number] Protected routes (cart, checkout require auth)
- [ ] #[auth-issue-number] Real customerId from session

## Exit Criteria
- [ ] All integration tests pass
- [ ] Manual testing checklist complete
- [ ] Zero hardcoded stub GUIDs

## References
- [CONTEXTS.md](../../CONTEXTS.md)
- [cycle-18-customer-experience-phase-2.md](./cycles/cycle-18-customer-experience-phase-2.md) (previous cycle)
```

2. **Individual task Issues** (linked as sub-issues or referenced via checklist)

### Phase C: Migrate Historical Cycle Records

For each completed cycle (Cycles 1–18), create a **closed Milestone** with:
- Milestone name: `Cycle NN: <Name>` 
- Due date: completion date from `CYCLES.md`
- Description: one-paragraph summary from `CYCLES.md`
- Status: **Closed**

The detailed notes remain in `docs/planning/cycles/cycle-NN-*.md` and are linked from the Milestone description.

### Phase D: Create ADR Companion Issues

For each ADR, create a GitHub Issue as a tracking/discussion companion:

```
Title: [ADR 0011] GitHub Projects & Issues Migration

Labels: type:adr
Milestone: (associated cycle or none)

Body:
## Summary
Migrate planning from markdown to GitHub Projects + Issues (hybrid model).

## Document
[docs/decisions/0011-github-projects-issues-migration.md](../decisions/0011-github-projects-issues-migration.md)

## Status
✅ Accepted — 2026-02-23

## Discussion
<!-- This issue is the discussion thread for questions about this ADR -->
```

---

## Part 4: Maintaining `CURRENT-CYCLE.md` (AI Agent Fallback)

**Purpose:** A single, small markdown file that AI agents can read when they don't have GitHub MCP server access, or as a fast-load summary at the start of sessions.

**Location:** `docs/planning/CURRENT-CYCLE.md`

**Update Frequency:** Updated at the start and end of each cycle (and whenever major tasks change).

**Format (template):**

```markdown
# Current Development Cycle

**Cycle:** 19 — Authentication & Authorization  
**Status:** 🟡 In Progress  
**GitHub Milestone:** [Cycle 19](https://github.com/erikshafer/CritterSupply/milestone/NN)  
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/NN)

## Active Tasks
- [ ] #XX Create ADR for auth strategy
- [ ] #XX Implement login/logout pages
- [ ] #XX Protected routes

## Recently Completed
- ✅ Cycle 18: Customer Experience Phase 2 (2026-02-14)

## Next Up (After This Cycle)
- Cycle 20: Automated Browser Testing
- Cycle 21: Vendor Portal Phase 1

## Quick Links
- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth
- [BACKLOG on GitHub](https://github.com/erikshafer/CritterSupply/issues?q=label%3Astatus%3Abacklog)
```

---

## Part 5: Updated AI Agent Workflow

### How AI Agents Should Use GitHub Issues

**Starting a New Session (with MCP access):**

```
1. Read CONTEXTS.md (always first — architectural truth)
2. Call: list_issues(milestone="Cycle 19", state="open")
   → Gets: all open tasks for active cycle
3. Call: issue_read(method="get", issue_number=XX)
   → Gets: task details, acceptance criteria, dependencies
4. Proceed with implementation
5. When closing a task: reference issue in commits ("Fixes #XX")
```

**Starting a New Session (without MCP access / fallback):**

```
1. Read CONTEXTS.md
2. Read docs/planning/CURRENT-CYCLE.md
   → Gets: active cycle name, key tasks, GitHub links
3. Navigate to GitHub links manually if needed
4. Proceed with implementation
```

### Commit Message Convention

Reference Issues in all commits:
```
feat: implement login page with MudBlazor (#42)
fix: resolve stub customerId in Cart.razor (#43)
docs: update CURRENT-CYCLE.md for Cycle 19 start
```

---

## Part 6: Deprecation Plan for Markdown Files

### Timeline

**Immediately (after GitHub setup):**
- Create labels, milestones, project board
- Migrate backlog items to Issues
- Create Cycle 19 milestone and issues

**After Cycle 19 Begins:**
- Add deprecation notice to `docs/planning/CYCLES.md`
- Add deprecation notice to `docs/planning/BACKLOG.md`
- Create `docs/planning/CURRENT-CYCLE.md`
- Update `CLAUDE.md` custom instructions to reference GitHub Issues

**After Cycle 20 (steady state):**
- `CYCLES.md` — Read-only archive (add "DEPRECATED" header)
- `BACKLOG.md` — Read-only archive (add "DEPRECATED" header)
- New cycles tracked entirely in GitHub; only retrospective docs created in `cycles/`

**Files that are NEVER deprecated:**
- `CONTEXTS.md` — AI architectural truth
- `CLAUDE.md` — AI custom instructions
- `skills/*.md` — AI skill guides
- `docs/decisions/NNNN-*.md` — ADR authoritative sources
- `docs/features/**/*.feature` — BDD living documentation
- `docs/planning/CURRENT-CYCLE.md` — AI fallback summary

---

## Part 7: Updated Development Workflow (Post-Migration)

### Starting a New Cycle

**Before:**
```
1. Create docs/planning/cycles/cycle-NN-name.md
2. Write Gherkin .feature files
3. Create ADRs
4. Update CYCLES.md "Current Cycle" section
```

**After:**
```
1. Create GitHub Milestone: "Cycle NN: Name" with due date
2. Create parent "Cycle Epic" Issue linked to milestone
3. Create individual task Issues linked to milestone
4. Write Gherkin .feature files (stays the same)
5. Create ADR markdown file + companion Issue
6. Update docs/planning/CURRENT-CYCLE.md
7. Move milestone from "Upcoming" to active in GitHub Project
```

### During a Cycle

**Before:**
```
- Update cycle markdown checkboxes manually
- Write implementation notes inline
- Create ADRs as new markdown files
```

**After:**
```
- Close Issues as tasks complete (via PR "Fixes #XX")
- Add comments to Issues for implementation notes
- Create ADR markdown file + close companion Issue when decided
- GitHub Project automation moves issues between columns
```

### Completing a Cycle

**Before:**
```
1. Mark cycle complete in CYCLES.md
2. Update CONTEXTS.md
3. Archive notes in cycle-specific doc
```

**After:**
```
1. Close GitHub Milestone (sets completion date automatically)
2. Create retrospective doc: docs/planning/cycles/cycle-NN-retrospective.md
3. Update CONTEXTS.md with new integration flows
4. Update docs/planning/CURRENT-CYCLE.md to next cycle
5. GitHub Project: archive completed milestone items
```

---

## Part 8: GitHub Project Automation Rules

Configure these automation rules in the Project settings:

| Trigger | Action |
|---|---|
| Issue added to project | Set status to "Backlog" |
| Issue assigned | Set status to "In Progress" |
| Pull request opened (linked to issue) | Set status to "In Review" |
| Pull request merged | Set status to "Done" |
| Issue closed (no PR) | Set status to "Done" |
| Issue reopened | Set status to "In Progress" |

---

## Part 9: Practical Benefits vs. Trade-Offs Summary

### For the Human Developer

| Benefit | Trade-Off |
|---|---|
| Click-to-filter by BC, priority, type | Initial setup time (~2-4 hours) |
| Live progress bars (Milestone completion %) | Must update Issues not markdown |
| PR links automatically reference work items | New habit to remember `Fixes #XX` |
| Roadmap view across cycles | GitHub Projects UI learning curve |
| Notifications when issues are commented on | More email/notifications to manage |

### For AI Agents (GitHub Copilot, Claude)

| Benefit | Trade-Off |
|---|---|
| `list_issues(milestone="Cycle 19")` = instant state | Requires MCP server access |
| Structured metadata (labels, fields) | Must fallback to `CURRENT-CYCLE.md` without MCP |
| `Fixes #XX` in commits = automatic cross-referencing | More tokens spent on tool calls |
| Can search issues across all BCs instantly | Cannot read Issues without internet |

### For Different Computers / Locations

| Scenario | How It Works |
|---|---|
| Working with GitHub Copilot in VS Code | Copilot can reference Issues via GitHub context |
| Working with Claude + MCP server | Direct `list_issues`, `issue_read` calls |
| Working without internet / air-gapped | Read `docs/planning/CURRENT-CYCLE.md` fallback |
| New machine, fresh clone | `CONTEXTS.md` + `CURRENT-CYCLE.md` = immediate context |

---

## Part 10: Execution Checklist

### One-Time Setup
- [ ] Create GitHub Project: "CritterSupply Development"
- [ ] Add custom fields (Bounded Context, Priority, Effort, Type)
- [ ] Create label taxonomy (bc:*, type:*, status:*, priority:*)
- [ ] Configure Project views (Board, Backlog Table, Active Cycle, Roadmap)
- [ ] Configure automation rules

### Historical Migration
- [ ] Create closed Milestones for Cycles 1-18
- [ ] Create ADR companion Issues for all 11 ADRs
- [ ] Create backlog Issues for all items in BACKLOG.md

### Cycle 19 Setup
- [ ] Create Milestone: "Cycle 19: Authentication & Authorization"
- [ ] Create parent Cycle Epic Issue
- [ ] Create individual task Issues for Cycle 19
- [ ] Add deprecation notice to CYCLES.md
- [ ] Add deprecation notice to BACKLOG.md
- [ ] Create `docs/planning/CURRENT-CYCLE.md`

### CLAUDE.md Updates
- [ ] Update workflow section to reference GitHub Issues
- [ ] Add note: "Before starting a cycle, check GitHub Issues for milestone"
- [ ] Add note: "Use `Fixes #XX` in commits to close issues"

---

## References

- **ADR:** [0011-github-projects-issues-migration.md](../decisions/0011-github-projects-issues-migration.md)
- **Current Cycle:** [CURRENT-CYCLE.md](./CURRENT-CYCLE.md) *(created during migration)*
- **Historical cycles:** [docs/planning/cycles/](./cycles/)
- **GitHub Projects docs:** https://docs.github.com/en/issues/planning-and-tracking-with-projects
- **GitHub Issues docs:** https://docs.github.com/en/issues/tracking-your-work-with-issues
- **Sub-issues:** https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/adding-sub-issues
