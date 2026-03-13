# Current Development Cycle

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development cycle.  
> It is the fallback when GitHub Issues/Projects are not directly accessible.  
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="Cycle 27", state="open")`  
> This works identically on any machine — MacBook, Windows PC, Linux laptop.

---

**Cycle:** 26 — Returns BC Phase 2 *(just completed)*  
**Status:** ✅ **COMPLETE** — All sign-offs obtained (PSA, PO, UXE)  
**GitHub Milestone:** Cycle 26: Returns BC Phase 2  
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)

---

## Current Status

**Cycles 25 and 26 are COMPLETE.** Returns BC Phase 1 (core domain + API) and Phase 2 (integration events, mixed inspection, contract expansion) are both delivered. All stakeholder sign-offs obtained.

**What Cycle 25 delivered (Returns BC Phase 1):**
- ✅ Event-sourced Return aggregate (10 lifecycle states, 9 domain events)
- ✅ 6 command handlers: RequestReturn, ApproveReturn, DenyReturn, ReceiveReturn, SubmitInspection, ExpireReturn
- ✅ ReturnEligibilityWindow (from Fulfillment.ShipmentDelivered)
- ✅ Auto-approval logic + restocking fee calculation
- ✅ 7 API endpoints (port 5245, schema `returns`)
- ✅ 48 unit tests + 5 integration tests
- [Phase 1 Retrospective](cycles/cycle-25-returns-bc-phase-1.md)

**What Cycle 26 delivered (Returns BC Phase 2):**
- ✅ `ReturnCompleted` expanded with per-item disposition (CustomerId, IReadOnlyList\<ReturnedItem\>)
- ✅ 5 new integration events: ReturnApproved, ReturnRejected, ReturnExpired, ReturnReceived, ReturnedItem
- ✅ `ReturnDenied` expanded with CustomerId and customer-facing Message
- ✅ Mixed inspection three-way logic (all-pass/all-fail/mixed → partial refund)
- ✅ `GetReturnsForOrder` query implemented (Marten inline snapshots)
- ✅ RabbitMQ dual-queue routing (orders-returns-events + storefront-returns-events)
- ✅ Fulfillment → Returns queue wiring fix (production bug)
- ✅ Orders saga: DeliveredAt persistence + ReturnRejected/ReturnExpired handlers
- ✅ CS agent runbook
- ✅ 53 Returns unit tests + 34 integration tests + 12 Orders return saga tests = ~99 total
- [Phase 2 Implementation Plan](cycles/cycle-26-returns-bc-phase-2.md)
- [Phase 2 Retrospective](cycles/cycle-26-returns-bc-phase-2-retrospective.md)

**Next cycles (roadmap):**
- **Cycle 27:** Returns BC Phase 3 — Exchange workflow (UC-11, ~20-30% of return volume) + CE SignalR handlers
- **Cycle 28:** Notifications BC Phase 1 — Transactional email (OrderPlaced, ShipmentDispatched, Returns events)
- **Cycle 29:** Promotions BC Phase 1 — Coupons and discounts; RBAC ADR for Admin Portal
- **Cycle 30+:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling

---

## Recently Completed

- ✅ **Cycle 26:** Returns BC Phase 2 (2026-03-12 to 2026-03-13) — **COMPLETE**
  - ReturnCompleted expanded with per-item disposition (CustomerId, ReturnedItem[])
  - 5 new integration events (ReturnApproved, ReturnRejected, ReturnExpired, ReturnReceived, ReturnedItem)
  - ReturnDenied expanded with CustomerId and Message
  - Mixed inspection three-way logic (all-pass/all-fail/mixed → partial refund)
  - GetReturnsForOrder query implemented (Marten inline snapshots)
  - RabbitMQ dual-queue routing + Fulfillment queue wiring fix
  - Orders saga: DeliveredAt + ReturnRejected/ReturnExpired handlers
  - CS agent runbook
  - ~99 total return-related tests (53 unit + 34 integration + 12 saga)
  - Sign-offs: PSA ✅, PO ✅, UXE ✅
  - [Plan](./cycles/cycle-26-returns-bc-phase-2.md) | [Retrospective](./cycles/cycle-26-returns-bc-phase-2-retrospective.md)

- ✅ **Cycle 25:** Returns BC Phase 1 (2026-03-12) — **COMPLETE**
  - Event-sourced Return aggregate (10 lifecycle states, 9 domain events)
  - 6 command handlers + 7 API endpoints (port 5245)
  - ReturnEligibilityWindow from Fulfillment.ShipmentDelivered
  - Auto-approval logic + restocking fee calculation
  - 48 unit tests + 5 integration tests
  - Sign-offs: PO ✅, UXE ✅, QA ✅, PSA ✅
  - [Plan & Retrospective](./cycles/cycle-25-returns-bc-phase-1.md)

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

### Next 4 Cycles (Revised after Phase 2 Retrospective)

> **See** [`docs/planning/cycles/cycle-26-returns-bc-phase-2-retrospective.md`](cycles/cycle-26-returns-bc-phase-2-retrospective.md) for Phase 3 priorities from PSA, PO, and UXE sign-offs.

- **Cycle 27:** Returns BC Phase 3 — Exchange workflow (UC-11, ~20-30% return volume) + CE SignalR handlers
  - 🔴 Exchange workflow: customer returns item A and gets item B in exchange
  - 🟡 Customer Experience BC SignalR handlers for 7 return events
  - 🟡 Sequential returns (multiple returns per order before window expires)
  - Prerequisite: Cycles 25-26 Returns Phase 1-2 ✅ COMPLETE

- **Cycle 28:** Notifications BC Phase 1 — Transactional email
  - Phase 1a: `OrderPlaced`, `ShipmentDispatched` (existing BC events)
  - Phase 1b: Returns events (`ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnExpired`, `ReturnReceived`, `ReturnRejected`)

- **Cycle 29:** Promotions BC Phase 1 — Coupons and discounts
  - RBAC ADR for Admin Portal to be authored during this cycle
  - Shopping BC already has `CouponApplied`/`CouponRemoved` placeholder events

- **Cycle 30+:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling
  - Event Modeling: [`docs/planning/admin-portal-event-modeling.md`](admin-portal-event-modeling.md)
  - UX Research: [`docs/planning/admin-portal-ux-research.md`](admin-portal-ux-research.md)
  - Gherkin features: [`docs/features/admin-portal/`](../features/admin-portal/)
  - RBAC ADR must exist before implementation begins

### Future BCs (Priority Roadmap)

**High Priority (Customer-Facing Gaps):**
- 🔴 **Returns BC Phase 3** — Exchange workflow, CE SignalR handlers, sequential returns *(Cycle 27)*
- 🔴 **Notifications BC** — Transactional emails, SMS, push notifications *(Cycle 28)*
- 🔴 **Promotions BC** — Discount codes, percentage-off campaigns, BOGO *(Cycle 29)*

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

*Last Updated: 2026-03-13 (Cycle 26 closed; Returns BC Phase 2 complete — all sign-offs obtained)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
