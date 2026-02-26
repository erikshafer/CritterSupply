# CritterSupply Documentation

This directory contains planning, decisions, and feature specifications for the CritterSupply reference architecture.

## 🤖 AI-Discoverable Documentation

CritterSupply uses a structured documentation approach optimized for AI assistants:

- **[planning/](./planning/)** - Development cycles, detailed plans, and roadmap
- **[decisions/](./decisions/)** - Architectural Decision Records (ADRs) explaining key choices
- **[features/](./features/)** - BDD specifications in Gherkin format (Given/When/Then scenarios)

These are automatically discoverable by AI assistants through [CLAUDE.md](../CLAUDE.md), which serves as the entry point for AI-assisted development. This structure ensures AI tools can locate planning context, understand architectural decisions, and reference user stories without manual prompting.

## 📋 Documentation Structure

### [planning/](./planning/) - Development Planning & Progress

**[CYCLES.md](./planning/CYCLES.md)** - Active development cycle, recent completions, upcoming work

**[cycles/](./planning/cycles/)** - Detailed per-cycle plans with objectives, deliverables, and completion criteria
- [cycle-18-customer-experience-phase-2.md](./planning/cycles/cycle-18-customer-experience-phase-2.md) - Customer Experience Enhancement (Phase 2) - ✅ Complete (2026-02-14)
- [cycle-17-customer-identity-integration.md](./planning/cycles/cycle-17-customer-identity-integration.md) - Customer Identity Integration - ✅ Complete (2026-02-13)
- [cycle-16-customer-experience.md](./planning/cycles/cycle-16-customer-experience.md) - Customer Experience BC (BFF + Blazor) - ✅ Complete (2026-02-05)
- _(Earlier cycles documented in CYCLES.md)_

**[BACKLOG.md](./planning/BACKLOG.md)** - Future work not yet scheduled

**Infrastructure Initiatives:**
- [CI/CD Planning](./planning/infrastructure/ci-cd/) - GitHub Actions workflow improvements and roadmap
- [ADR 0007: GitHub Workflow Improvements](./decisions/0007-github-workflow-improvements.md) - 6-phase CI/CD modernization proposal

---

### [decisions/](./decisions/) - Architectural Decision Records (ADRs)

Lightweight records capturing **why** we made key architectural choices.

**Existing ADRs:**
- [0004: SSE over SignalR](./decisions/0004-sse-over-signalr.md) - Real-time updates for Customer Experience BC
- [0005: MudBlazor UI Framework](./decisions/0005-mudblazor-ui-framework.md) - UI component library choice
- [0006: Reqnroll BDD Framework](./decisions/0006-reqnroll-bdd-framework.md) - BDD testing approach
- [0007: GitHub Workflow Improvements](./decisions/0007-github-workflow-improvements.md) - 6-phase CI/CD roadmap
- [0008: RabbitMQ Configuration Consistency](./decisions/0008-rabbitmq-configuration-consistency.md) - Explicit RabbitMQ publishing configuration
- [0009: .NET Aspire v13.1 Integration](./decisions/0009-aspire-integration.md) - Local development orchestration with Aspire

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

**Product Catalog BC:**
- [add-product.feature](./features/product-catalog/add-product.feature) - Product creation and management

_(Feature files for other BCs to be added over time)_

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
- [docs/skills/](../skills/) - Pattern documentation for Wolverine, Marten, EF Core, BDD, testing, etc.

---

## 🎯 Current Status

**Latest Completed Cycle:** Cycle 18 - Customer Experience Enhancement (Phase 2) - ✅ Complete (2026-02-14)

**Next Planned Cycle:** Cycle 19 - Authentication & Authorization

See [CYCLES.md](./planning/CYCLES.md) for detailed progress tracking and cycle history.

---

**Last Updated:** 2026-02-16
