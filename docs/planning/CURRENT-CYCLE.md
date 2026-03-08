# Current Development Cycle

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development cycle.  
> It is the fallback when GitHub Issues/Projects are not directly accessible.  
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="Cycle 19", state="open")`  
> This works identically on any machine — MacBook, Windows PC, Linux laptop.

---

**Cycle:** 21 — Pricing BC Phase 1
**Status:** 📋 **PLANNED** (Ready to start)
**GitHub Milestone:** [Cycle 21: Pricing BC Phase 1](https://github.com/erikshafer/CritterSupply/milestone/15)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)
**Epic Issue:** [#184](https://github.com/erikshafer/CritterSupply/issues/184)

---

## Current Status

**Cycle 21 is ready to begin!** Pricing BC Phase 1 will establish server-authoritative pricing and close the critical security gap in Shopping BC.

**Priority #1:** Server-authoritative pricing at add-to-cart (Shopping BC integration) — closes security vulnerability where client can supply any price.

**Key Deliverables:**
- ProductPrice event-sourced aggregate (Unpriced → Published lifecycle)
- CurrentPriceView inline projection (zero-lag price queries)
- Money value object with currency support
- 4 required ADRs written before implementation
- Integration with Product Catalog BC (ProductRegistered event handler)
- Shopping BC calls Pricing BC for authoritative prices

**Phase 1 Tasks:** [9 issues created](https://github.com/erikshafer/CritterSupply/milestone/15) (#190-#214)

**Next cycles:**
- **Cycle 22:** Vendor Portal + Vendor Identity Phase 1 ([Milestone 16](https://github.com/erikshafer/CritterSupply/milestone/16)) - Issues TBD
- **Cycle 23:** Admin Portal Phase 1 ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17)) - Issues TBD

---

## Recently Completed

- ✅ **Cycle 20:** Automated Browser Testing (2026-03-04 to 2026-03-07)
  - Playwright + Reqnroll E2E testing infrastructure
  - Real Kestrel servers (not TestServer) for SignalR testing
  - Page Object Model with data-testid selectors
  - MudBlazor component interaction patterns (MudSelect)
  - Stub coordination via TestIdProvider (deterministic IDs)
  - Playwright tracing for CI failure diagnosis
  - Full coverage: product browsing, cart, checkout wizard, order history, SignalR real-time updates
  - [Plan](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md) | [Issues Export](./cycles/cycle-20-issues-export.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/2)

- ✅ **Cycle 19.5:** Complete Checkout Workflow (2026-03-04)
  - Wired checkout stepper to backend APIs
  - Checkout initialization + CheckoutId persistence
  - Error handling with MudSnackbar toasts
  - End-to-end manual testing
  - [Milestone](https://github.com/erikshafer/CritterSupply/milestone/13)

- ✅ **Cycle 19:** Authentication & Authorization (2026-02-25 to 2026-02-26)
  - Cookie-based authentication (ASP.NET Core Authentication middleware)
  - Login/Logout pages with MudBlazor
  - Protected routes (Cart, Checkout)
  - AppBar authentication UI (Sign In / My Account dropdown)
  - Replaced all stub customerIds with authenticated session values
  - Cart persistence via browser localStorage
  - Swagger UI + seed data for ProductCatalog.Api
  - Npgsql logging noise reduction
  - [Plan](./cycles/cycle-19-authentication-authorization.md) | [Retrospective](./cycles/cycle-19-retrospective.md) | [Issues Export](./cycles/cycle-19-issues-export.md)

---

## Upcoming (Planned)

### Next 3 Cycles (Milestones Created, Issues Ready/TBD)

- **Cycle 21:** Pricing BC Phase 1 — Server-authoritative pricing, ProductPrice aggregate, Money value object ([Milestone 15](https://github.com/erikshafer/CritterSupply/milestone/15), [Epic #184](https://github.com/erikshafer/CritterSupply/issues/184), 9 issues created)
  - Event Modeling: [`docs/planning/pricing-event-modeling.md`](pricing-event-modeling.md)
  - Gherkin features: [`docs/features/pricing/`](../features/pricing/)
  - ADRs required: 4 ADRs before implementation ([Issue #190](https://github.com/erikshafer/CritterSupply/issues/190))

- **Cycle 22:** Vendor Portal + Vendor Identity Phase 1 — Infrastructure foundation (no UI yet) ([Milestone 16](https://github.com/erikshafer/CritterSupply/milestone/16), Issues TBD)
  - Event Modeling: [`docs/planning/vendor-portal-event-modeling.md`](vendor-portal-event-modeling.md)
  - Gherkin features: [`docs/features/vendor-portal/`](../features/vendor-portal/), [`docs/features/vendor-identity/`](../features/vendor-identity/)
  - ADR: [ADR 0015: JWT for Vendor Identity](../decisions/0015-jwt-for-vendor-identity.md)

- **Cycle 23:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17), Issues TBD)
  - Event Modeling: [`docs/planning/admin-portal-event-modeling.md`](admin-portal-event-modeling.md)
  - Gherkin features: [`docs/features/admin-portal/`](../features/admin-portal/)

### Future BCs (Priority Roadmap)

**High Priority (Customer-Facing Gaps):**
- 🔴 **Notifications BC** — Transactional emails, SMS, push notifications
- 🔴 **Promotions BC** — Discount codes, percentage-off campaigns, BOGO
- 🔴 **Returns BC** — Return authorization, refund processing, restocking

**Medium Priority (Scaling + Internal Tooling):**
- 🟡 **Search BC** — Full-text product search, faceted navigation
- 🟡 **Recommendations BC** — Personalized product recommendations
- 🟡 **Analytics BC** — Business intelligence, reporting, dashboards

**Low Priority (Strategic/Retention):**
- 🟢 **Store Credit BC** — Gift cards, store credit issuance, balance tracking
- 🟢 **Loyalty BC** — Rewards program, points accumulation
- 🟢 **Operations Dashboard** — Developer/SRE event stream visualization (React + SignalR)

See [CONTEXTS.md — Future Considerations](../../CONTEXTS.md) for full specifications.

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [Full Backlog on GitHub](https://github.com/erikshafer/CritterSupply/issues?q=label%3Astatus%3Abacklog) *(after migration)*
- [GitHub Project Board](https://github.com/users/erikshafer/projects) *(after setup)*
- [Historical cycles](./cycles/) — Markdown retrospectives
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change

---

*Last Updated: 2026-03-08 (Cycle 20 closed, Cycle 21-23 milestones created, Pricing BC issues ready, CURRENT-CYCLE now reflects active planning state)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
