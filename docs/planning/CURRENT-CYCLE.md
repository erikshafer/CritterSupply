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

**Cycle:** 22 — Vendor Portal + Vendor Identity Phase 1
**Status:** 🚀 **IN PROGRESS** (Issues created, ready to implement)
**GitHub Milestone:** [Cycle 22: Vendor Portal + Vendor Identity Phase 1](https://github.com/erikshafer/CritterSupply/milestone/16)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)
**Epic Issue:** [#249](https://github.com/erikshafer/CritterSupply/issues/249)

---

## Current Status

**Cycle 22 has begun!** Vendor Portal + Vendor Identity Phase 1 will establish the infrastructure foundation for vendor-facing features (no UI yet).

**Priority #1:** VendorProductCatalog (the load-bearing pillar) — SKU→VendorTenantId lookup that enables all analytics and change request invariants.

**Key Deliverables (Phase 1 - Infrastructure Foundation):**
- VendorIdentity EF Core project (VendorTenant, VendorUser, VendorUserInvitation entities)
- CreateVendorTenant + InviteVendorUser commands + integration events
- VendorPortal domain project + VendorProductCatalog document store
- VendorProductAssociated integration event (Catalog BC → Vendor Portal)
- AssignProductToVendor + bulk-assignment commands (Catalog BC admin endpoints)
- VendorPortal.Api skeleton with RabbitMQ subscriptions
- Integration tests (full round-trip testable with no UI)

**Phase 1 Tasks (17 issues created):**

### Vendor Identity (4 issues)
- [#254](https://github.com/erikshafer/CritterSupply/issues/254): Create EF Core project structure + migrations
- [#255](https://github.com/erikshafer/CritterSupply/issues/255): Implement CreateVendorTenant command + handler
- [#256](https://github.com/erikshafer/CritterSupply/issues/256): Implement InviteVendorUser command + handler
- [#257](https://github.com/erikshafer/CritterSupply/issues/257): Write integration tests for Vendor Identity BC

### Vendor Portal (4 issues)
- [#270](https://github.com/erikshafer/CritterSupply/issues/270): Create domain + API project structure
- [#271](https://github.com/erikshafer/CritterSupply/issues/271): Implement VendorProductCatalog document + projection
- [#272](https://github.com/erikshafer/CritterSupply/issues/272): Configure RabbitMQ subscriptions in VendorPortal.Api
- [#273](https://github.com/erikshafer/CritterSupply/issues/273): Write integration tests for VendorPortal BC

### Product Catalog (4 issues)
- [#286](https://github.com/erikshafer/CritterSupply/issues/286): Add VendorProductAssociated integration event
- [#287](https://github.com/erikshafer/CritterSupply/issues/287): Implement AssignProductToVendor admin endpoint
- [#288](https://github.com/erikshafer/CritterSupply/issues/288): Implement BulkAssignProductsToVendor command
- [#289](https://github.com/erikshafer/CritterSupply/issues/289): Write integration tests for vendor assignment

### Infrastructure (4 issues)
- [#302](https://github.com/erikshafer/CritterSupply/issues/302): Update Messages.Contracts with VendorIdentity namespace
- [#303](https://github.com/erikshafer/CritterSupply/issues/303): Update docker-compose.yml with vendoridentity-api + vendorportal-api
- [#304](https://github.com/erikshafer/CritterSupply/issues/304): Update CONTEXTS.md: validate Phase 1 integration contracts
- [#305](https://github.com/erikshafer/CritterSupply/issues/305): Update .slnx with new Vendor Portal + Vendor Identity projects

**Next cycles:**
- **Cycle 23:** Vendor Portal Phase 2 — JWT auth + SignalR hub + static analytics dashboard
- **Cycle 24:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17))

---

## Recently Completed

- ✅ **Cycle 21:** Pricing BC Phase 1 (2026-03-07 to 2026-03-08) — **MILESTONE CLOSED**
  - ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
  - Money value object (140 unit tests)
  - CurrentPriceView inline projection (zero-lag queries)
  - Shopping BC security fix (server-authoritative pricing)
  - 5 ADRs written (UUID v5, price freeze, Money VO, bulk jobs, MAP vs Floor)
  - 151 Pricing tests + 56 Shopping tests (all passing)
  - Docker Compose integration
  - 11 issues closed (all deliverables complete)
  - [Plan](pricing-event-modeling.md) | [Retrospective](./cycles/cycle-21-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

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

- **Cycle 22:** Vendor Portal + Vendor Identity Phase 1 — Infrastructure foundation (no UI yet) ([Milestone 16](https://github.com/erikshafer/CritterSupply/milestone/16), Issues TBD)
  - Event Modeling: [`docs/planning/vendor-portal-event-modeling.md`](vendor-portal-event-modeling.md)
  - Gherkin features: [`docs/features/vendor-portal/`](../features/vendor-portal/), [`docs/features/vendor-identity/`](../features/vendor-identity/)
  - ADR: [ADR 0015: JWT for Vendor Identity](../decisions/0015-jwt-for-vendor-identity.md)

- **Cycle 23:** Vendor Portal Phase 2 — JWT auth + SignalR hub + static analytics dashboard (estimated 2 weeks)
  - Builds on Cycle 22 infrastructure
  - VendorPortalHub with dual group membership (vendor:{tenantId}, user:{userId})
  - JWT Bearer authentication (15-min access + 7-day refresh)
  - Static analytics dashboard (HTTP queries, no SignalR updates yet)
  - OrderPlacedHandler fan-out to ProductPerformanceSummary projection

- **Cycle 24:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17), Issues TBD)
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

*Last Updated: 2026-03-08 (Cycle 21 closed, Cycle 22 in progress — 17 issues created for Vendor Portal + Vendor Identity Phase 1)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
