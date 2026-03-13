# Current Development Cycle

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development cycle.  
> It is the fallback when GitHub Issues/Projects are not directly accessible.  
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="Cycle 25", state="open")`  
> This works identically on any machine — MacBook, Windows PC, Linux laptop.

---

**Cycle:** 25 — Returns BC Phase 1
**Status:** 📋 **PLANNED** — Cycle 24 complete; all prerequisites in place; sign-offs obtained
**GitHub Milestone:** Cycle 25: Returns BC Phase 1 *(create before starting)*
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)

---

## Current Status

**Cycle 24 is COMPLETE.** All Fulfillment integrity bugs fixed, Orders saga return handlers implemented, and Returns prerequisites in place. Sign-offs obtained from Product Owner, UX Engineer, and Principal Software Architect.

**What Cycle 24 delivered:**
- ✅ RabbitMQ transport wired in Fulfillment.Api (messages cross process boundaries)
- ✅ `RecordDeliveryFailure` endpoint + `ShipmentDeliveryFailed` cascade
- ✅ `shipment-delivery-failed` SSE case in OrderConfirmation.razor
- ✅ UUID v5 idempotent shipment creation + clean ShipmentStatus enum
- ✅ `SharedShippingAddress` with dual JSON annotations (Phase A)
- ✅ Orders saga return handlers: `ReturnRequested`, `ReturnCompleted`, `ReturnDenied`
- ✅ `IsReturnInProgress` guard on `ReturnWindowExpired` (critical bug fix)
- ✅ `GET /api/orders/{orderId}/returnable-items` endpoint
- ✅ Returns integration message contracts + RabbitMQ routing

**Cycle 24 Plan:** [`docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md`](cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

**Cycle 25 Scope (Returns BC Phase 1):**

*Pre-implementation tasks (from Cycle 24 stakeholder observations):*
- 🔴 Add `DeliveredAt` persistence to Order saga's `Handle(ShipmentDelivered)` handler
- 🟡 Add exchange workflow placeholder feature file
- 🟡 Add mixed-inspection-results scenario to `return-inspection.feature`
- 🔴 Write CS agent runbook for manual return approvals (Option A — API + runbook)
- 🟡 Decide on `ReturnCompleted` contract expansion for Inventory BC item dispositions

*Core Returns BC implementation:*
- 🔴 Returns BC domain project (`src/Returns/Returns/`) with event-sourced Return aggregate
- 🔴 Returns BC API project (`src/Returns/Returns.Api/`) with Wolverine + Marten configuration
- 🔴 Return lifecycle: Requested → Approved → LabelGenerated → InTransit → Received → Inspecting → Completed/Denied/Expired/Rejected
- 🔴 Return eligibility: 30-day window, non-returnable categories, duplicate prevention
- 🔴 Return inspection: disposition logic (restock, dispose, quarantine, return-to-customer)
- 🔴 Return expiration: auto-expiry of unshipped approved returns
- 🟡 Order History page in Storefront.Web (prerequisite for return initiation UX)

**Next cycles (roadmap):**
- **Cycle 26:** Notifications BC Phase 1 — Transactional email (OrderPlaced, ShipmentDispatched; Phase 1b: Returns events)
- **Cycle 27:** Promotions BC Phase 1 — Coupons and discounts; RBAC ADR for Admin Portal
- **Cycle 28+:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling

---

## Recently Completed

- ✅ **Cycle 24:** Fulfillment Integrity + Returns Prerequisites (2026-03-12) — **COMPLETE**
  - RabbitMQ transport wired in Fulfillment.Api
  - `RecordDeliveryFailure` endpoint + ShipmentDeliveryFailed cascade
  - `shipment-delivery-failed` SSE case in OrderConfirmation.razor
  - UUID v5 idempotent shipment creation + clean ShipmentStatus enum
  - SharedShippingAddress with dual JSON annotations (Phase A)
  - Orders saga return handlers + IsReturnInProgress guard
  - `GET /api/orders/{orderId}/returnable-items` endpoint
  - Event modeling exercise conducted with PO + UXE
  - Sign-offs: PO ✅, UXE ✅, PSA ✅
  - [Plan](./cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

- ✅ **Cycle 23:** Vendor Portal E2E Testing (2026-03-11) — **COMPLETE**
  - 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host)
  - 12 BDD scenarios (P0 + P1a) across 3 feature files
  - Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
  - SignalR hub message injection testing via IHubContext
  - Collaborative design: PA + QA + PO
  - [Plan](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Skills Update](../skills/e2e-playwright-testing.md)

- ✅ **Cycle 22:** Vendor Portal + Vendor Identity Phase 1 (2026-03-08 to 2026-03-10) — **ALL 6 PHASES COMPLETE**
  - Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
  - Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
  - Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
  - Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
  - Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
  - Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler, last-admin protection)
  - 143 integration tests across Vendor Portal + Identity (100% pass rate)
  - [Event Modeling](vendor-portal-event-modeling.md) | [Retrospective](./cycles/cycle-22-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/16)

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

### Next 4 Cycles (Revised after Post-Cycle 23 Priority Review)

> **See** [`docs/planning/priority-review-post-cycle-23.md`](priority-review-post-cycle-23.md) for the full cross-functional analysis (Product Owner + UX Engineer + Principal Architect) that produced this revised roadmap.
> **See** [`docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md`](cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md) for Cycle 24 plan with event modeling exercise and stakeholder observations for Cycle 25.

- **Cycle 25:** Returns BC Phase 1 — Self-Service Returns + Order History page *(current — ready to start)*
  - Domain spec ready: `docs/features/returns/` (4 feature files)
  - Prerequisite: Cycle 24 Fulfillment + saga work ✅ COMPLETE
  - Pre-implementation tasks documented in Cycle 24 stakeholder observations

- **Cycle 26:** Notifications BC Phase 1 — Transactional email
  - Phase 1a: `OrderPlaced`, `ShipmentDispatched` (existing BC events)
  - Phase 1b: Returns events (`ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnExpired`)

- **Cycle 27:** Promotions BC Phase 1 — Coupons and discounts
  - RBAC ADR for Admin Portal to be authored during this cycle (PO suggests moving to Cycle 26)
  - Shopping BC already has `CouponApplied`/`CouponRemoved` placeholder events

- **Cycle 28+:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling *(deferred from Cycle 24)*
  - Event Modeling: [`docs/planning/admin-portal-event-modeling.md`](admin-portal-event-modeling.md)
  - UX Research: [`docs/planning/admin-portal-ux-research.md`](admin-portal-ux-research.md)
  - Gherkin features: [`docs/features/admin-portal/`](../features/admin-portal/)
  - RBAC ADR must exist before implementation begins

### Future BCs (Priority Roadmap)

**High Priority (Customer-Facing Gaps):**
- 🔴 **Returns BC** — Return authorization, refund processing, restocking *(Cycle 25)*
- 🔴 **Notifications BC** — Transactional emails, SMS, push notifications *(Cycle 26)*
- 🔴 **Promotions BC** — Discount codes, percentage-off campaigns, BOGO *(Cycle 27)*

**Medium Priority (Scaling + Internal Tooling):**
- 🟡 **Admin Portal** — Internal operations portal *(Cycle 28+, after RBAC ADR)*
- 🟡 **Product Catalog Evolution** — Variants, Listings, Marketplaces *(D2–D10 decisions resolved; [cycle plan](catalog-listings-marketplaces-cycle-plan.md) approved — Cycles 29–35)*
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

*Last Updated: 2026-03-12 (Cycle 24 closed; all sign-offs obtained — Cycle 25 Returns BC Phase 1 ready to start)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
