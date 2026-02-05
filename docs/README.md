# CritterSupply Documentation

This directory contains planning, decisions, and feature specifications for the CritterSupply reference architecture.

## 📋 Documentation Structure

### [planning/](./planning/) - Development Planning & Progress

**[CYCLES.md](./planning/CYCLES.md)** - Active development cycle, recent completions, upcoming work

**[cycles/](./planning/cycles/)** - Detailed per-cycle plans with objectives, deliverables, and completion criteria
- [cycle-16-customer-experience.md](./planning/cycles/cycle-16-customer-experience.md) - Customer Experience BC (BFF + Blazor) - Current
- _(More cycles to be added as they are completed)_

**[BACKLOG.md](./planning/BACKLOG.md)** - Future work not yet scheduled

**CI/CD Workflow Proposal (NEW - 2026-02-05):**
- [Executive Summary](./planning/WORKFLOW_PROPOSAL_SUMMARY.md) - Quick overview of workflow improvements
- [ADR 0007: GitHub Workflow Improvements](./decisions/0007-github-workflow-improvements.md) - Detailed 6-phase proposal
- [Workflow Roadmap](./planning/WORKFLOW_ROADMAP.md) - Visual timeline and priorities
- [Phase 1 Implementation Guide](./planning/phase-1-implementation-guide.md) - Step-by-step instructions

---

### [decisions/](./decisions/) - Architectural Decision Records (ADRs)

Lightweight records capturing **why** we made key architectural choices.

**Existing ADRs:**
- [0004: SSE over SignalR](./decisions/0004-sse-over-signalr.md) - Real-time updates for Customer Experience BC
- [0005: MudBlazor UI Framework](./decisions/0005-mudblazor-ui-framework.md) - UI component library choice
- [0006: Reqnroll BDD Framework](./decisions/0006-reqnroll-bdd-framework.md) - BDD testing approach
- [0007: GitHub Workflow Improvements](./decisions/0007-github-workflow-improvements.md) - **NEW** 6-phase CI/CD roadmap
- _(Historical ADRs to be backfilled for Cycles 1-15)_

**When to Create an ADR:**
- Technology selection decisions (SSE vs SignalR, EF Core vs Marten)
- Pattern/approach decisions (value objects vs primitives for queryable fields)
- Bounded context boundary changes (Checkout migration from Shopping to Orders)
- Integration pattern choices (orchestration vs choreography)

---

### [features/](./features/) - BDD Feature Specifications (Gherkin)

User-facing behavior in Given/When/Then format, organized by bounded context.

**Customer Experience BC:**
- [cart-real-time-updates.feature](./features/customer-experience/cart-real-time-updates.feature) - Real-time cart updates via SSE
- [checkout-flow.feature](./features/customer-experience/checkout-flow.feature) - Multi-step checkout wizard
- [product-browsing.feature](./features/customer-experience/product-browsing.feature) - Product listing and detail pages

_(Feature files for other BCs to be added over time)_

---

### [architecture/](./architecture/) - High-Level Overviews

_(Reserved for future architecture diagrams and overviews)_

- Bounded context map
- Integration patterns
- Saga orchestration flows

---

## 🔄 Workflow for New Cycles

### Before Starting a Cycle (Planning Phase)
1. Create cycle plan: `planning/cycles/cycle-NN-name.md`
2. Write 2-3 Gherkin `.feature` files for key user stories
3. Create ADRs for any architectural decisions made during planning
4. Review [CONTEXTS.md](../CONTEXTS.md) for integration requirements
5. Update `planning/CYCLES.md` (move cycle from "Upcoming" to "Current")

### During a Cycle (Implementation Phase)
1. Implement features using `.feature` files as acceptance criteria
2. Write integration tests verifying Gherkin scenarios
3. Update cycle plan with "Implementation Notes" section (learnings, gotchas)
4. Create ADRs when making architectural decisions during implementation

### After Completing a Cycle (Retrospective Phase)
1. Mark cycle complete in `CYCLES.md` (add completion date + summary)
2. Update [CONTEXTS.md](../CONTEXTS.md) with new integration flows
3. Archive detailed notes in cycle-specific doc (`cycles/cycle-NN-name.md`)
4. Plan next cycle based on backlog

---

## 📚 Other Key Documentation

**Root-Level Docs:**
- [README.md](../README.md) - Public-facing project overview
- [CLAUDE.md](../CLAUDE.md) - AI assistant development guidelines
- [CONTEXTS.md](../CONTEXTS.md) - Bounded context specifications (architectural source of truth)
- [DEVPROGRESS.md](../DEVPROGRESS.md) - **Deprecated** (see deprecation notice, migrated to `planning/CYCLES.md`)

**Skills Documentation:**
- [skills/](../skills/) - Pattern documentation for Wolverine, Marten, EF Core, BDD, testing, etc.

---

## 🎯 Quick Links for Cycle 16

**Current Cycle:** Customer Experience BC (BFF + Blazor)

- **Plan:** [cycle-16-customer-experience.md](./planning/cycles/cycle-16-customer-experience.md)
- **Decision:** [ADR 0004: SSE over SignalR](./decisions/0004-sse-over-signalr.md)
- **Features:**
  - [cart-real-time-updates.feature](./features/customer-experience/cart-real-time-updates.feature)
  - [checkout-flow.feature](./features/customer-experience/checkout-flow.feature)
  - [product-browsing.feature](./features/customer-experience/product-browsing.feature)

---

**Last Updated:** 2026-02-05
