# Development Cycles

> **⚠️ MIGRATION IN PROGRESS (2026-02-23):**
> CritterSupply is migrating from markdown-based planning to **GitHub Projects + Issues**.
> - **New tracking:** GitHub Milestone per cycle, GitHub Issues per task, GitHub Project board
> - **Migration plan:** [GITHUB-MIGRATION-PLAN.md](./GITHUB-MIGRATION-PLAN.md)
> - **ADR:** [docs/decisions/0011-github-projects-issues-migration.md](../decisions/0011-github-projects-issues-migration.md)
> - **AI agent fallback:** [CURRENT-CYCLE.md](./CURRENT-CYCLE.md)
>
> This file will become a **read-only historical archive** after Cycle 19 setup is complete.
> Future cycles will be tracked in GitHub. Past cycle notes in `cycles/` remain as reference documentation.

This document tracks active and recent development cycles. For complete historical details, see individual cycle plans in [`cycles/`](./cycles/).

---

## Current Cycle

*Cycle 24 complete — ready to begin Cycle 25 (Returns BC Phase 1)*

---

## Recently Completed (Last 5 Cycles)

### Cycle 24: Fulfillment Integrity + Returns Prerequisites - ✅ Complete (2026-03-12)

**Objective:** Fix critical Fulfillment BC bugs and add Orders saga prerequisites required before the Returns BC can ship.

**Key Deliverables:**
- RabbitMQ transport wired in Fulfillment.Api (messages now cross process boundaries)
- `RecordDeliveryFailure` endpoint + `ShipmentDeliveryFailed` cascade to Orders + Storefront
- `shipment-delivery-failed` SSE case in OrderConfirmation.razor
- UUID v5 idempotent shipment creation, clean ShipmentStatus enum
- `SharedShippingAddress` with dual JSON annotations (Phase A migration)
- Orders saga return handlers: `ReturnRequested`, `ReturnCompleted`, `ReturnDenied`
- `IsReturnInProgress` guard on `ReturnWindowExpired` (critical bug fix)
- `GET /api/orders/{orderId}/returnable-items` endpoint
- Returns integration message contracts + RabbitMQ routing

**Results:** All deliverables complete. Build clean (0 warnings). Sign-offs: PO ✅, UXE ✅, PSA ✅

**Key Learnings:**
- Event modeling exercise confirmed feature file coverage is comprehensive
- `ReturnWindowExpired` guard pattern (boolean flag) is simpler than attempting to cancel scheduled messages
- SharedShippingAddress dual-annotation approach avoids Marten event stream migration

**Details:** [cycle-24-fulfillment-integrity-returns-prerequisites.md](./cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

---

### Cycle 23: Vendor Portal E2E Testing - ✅ Complete (2026-03-11)

**Objective:** Build Playwright + Reqnroll E2E browser tests for Vendor Portal with 3-server test fixture.

**Key Deliverables:**
- 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + Blazor WASM static host)
- 12 BDD scenarios across 3 feature files (P0 + P1a coverage)
- Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
- SignalR hub message injection testing via IHubContext
- Playwright tracing for CI failure diagnosis

**Results:** All E2E scenarios passing. Collaborative design: PA + QA + PO.

**Key Learnings:**
- WASM static host requires special handling vs standard WebApplicationFactory
- SignalR hub testing via IHubContext injection works well for push verification
- Page Object Model pattern proved directly reusable across Storefront and Vendor Portal E2E projects

**Details:** [cycle-23-vendor-portal-e2e-testing.md](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Retrospective](./cycles/cycle-23-retrospective.md)

---

### Cycle 22: Vendor Portal + Vendor Identity Phase 1 - ✅ Complete (2026-03-08 to 2026-03-10)

**Objective:** Build complete Vendor Portal ecosystem with JWT auth, multi-tenant API, Blazor WASM frontend, and admin tooling.

**Key Deliverables:**
- Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
- Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
- Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
- Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
- Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
- Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler, last-admin protection)

**Results:** 143 integration tests across Vendor Portal + Identity (100% pass rate).

**Key Learnings:**
- Blazor WASM requires named HttpClient registrations with explicit BaseAddress
- In-memory JWT storage safer than localStorage (XSS risk)
- Background token refresh via System.Threading.Timer (no IHostedService in WASM)

**Details:** [cycle-22-retrospective.md](./cycles/cycle-22-retrospective.md)

---

### Cycle 21: Pricing BC Phase 1 - ✅ Complete (2026-03-07 to 2026-03-08)

**Objective:** Build Pricing BC with event-sourced ProductPrice aggregate and Money value object.

**Key Deliverables:**
- ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
- Money value object (140 unit tests for arithmetic, rounding, currency safety)
- CurrentPriceView inline projection (zero-lag queries)
- Shopping BC security fix (server-authoritative pricing)
- 5 ADRs written (UUID v5, price freeze, Money VO, bulk jobs, MAP vs Floor)

**Results:** 151 Pricing tests + 56 Shopping tests (all passing). 11 issues closed.

**Key Learnings:**
- Money value object eliminates entire class of decimal arithmetic bugs
- Server-authoritative pricing prevents client-side price tampering
- UUID v5 deterministic IDs enable idempotent cross-BC event handling

**Details:** [cycle-21-retrospective.md](./cycles/cycle-21-retrospective.md)

---

### Cycle 20: Automated Browser Testing - ✅ Complete (2026-03-04 to 2026-03-07)

**Objective:** Establish Playwright + Reqnroll E2E testing infrastructure for Storefront.

**Key Deliverables:**
- Playwright + Reqnroll E2E testing infrastructure
- Real Kestrel servers (not TestServer) for SignalR testing
- Page Object Model with data-testid selectors
- MudBlazor component interaction patterns (MudSelect)
- Stub coordination via TestIdProvider (deterministic IDs)
- Playwright tracing for CI failure diagnosis

**Results:** Full coverage: product browsing, cart, checkout wizard, order history, SignalR real-time updates.

**Key Learnings:**
- TestServer doesn't support WebSocket/SignalR — must use real Kestrel
- data-testid selectors are more stable than CSS class selectors for MudBlazor
- Playwright tracing is essential for diagnosing CI-only failures

**Details:** [cycle-20-automated-browser-testing.md](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md)

---

### Cycle 14: Product Catalog BC (Phase 1 - Core CRUD) - ✅ Complete (2026-02-02)

**Objective:** Build Product Catalog BC with CRUD operations using Marten document store (non-event-sourced)

**Key Deliverables:**
- Product document model with factory methods (`Create()`, `Update()`, `ChangeStatus()`)
- `Sku` and `ProductName` value objects with JSON converters
- Category as primitive string (queryable with Marten LINQ)
- HTTP endpoints: POST, GET, GET (list), PUT, PATCH status
- 24/24 integration tests passing

**Key Decision:** [ADR 0003: Value Objects vs Primitives for Queryable Fields](../decisions/0003-value-objects-vs-primitives-queryable-fields.md)

**Architecture Signal:** Changed `Category` from value object to primitive string after 19/24 test failures. Marten LINQ couldn't translate `p.Category.Value == "Dogs"`. Pattern: primitives for queryable fields, value objects for complex structures.

**Details:** [cycle-14-product-catalog-core.md](./cycles/cycle-14-product-catalog-core.md)

---

### Cycle 13: Customer Identity BC - EF Core Migration - ✅ Complete (2026-01-19)

**Objective:** Migrate Customer Identity from Marten to EF Core to demonstrate traditional DDD + Wolverine integration

**Key Deliverables:**
- `Customer` aggregate root with navigation properties to `CustomerAddress` entities
- `CustomerIdentityDbContext` with fluent API configuration
- EF Core migrations for schema evolution
- Foreign key relationships with cascade delete
- All 12 integration tests passing (behavior preserved during migration)

**Key Decision:** [ADR 0002: EF Core for Customer Identity](../decisions/0002-ef-core-for-customer-identity.md)

**Rationale:** Relational model fits naturally (Customer → Addresses), no event sourcing needed (current state only), demonstrates Wolverine works with existing EF Core codebases (pedagogical value).

**Details:** [cycle-13-customer-identity-ef-core.md](./cycles/cycle-13-customer-identity-ef-core.md)

---

### Cycle 9: Checkout-to-Orders Integration - ✅ Complete (2026-01-15)

**Objective:** Complete integration between Shopping BC checkout completion and Orders BC saga initialization

**Key Deliverables:**
- Single entry point pattern: `Order.Start(Shopping.CheckoutCompleted)`
- Removed dual `Start()` overloads (kept only integration message entry point)
- Renamed local command from `CheckoutCompleted` (past tense) to `PlaceOrder` (imperative)
- Clear flow: `Shopping.CheckoutCompleted` (integration) → `Orders.PlaceOrder` (command) → `OrderPlaced` (event)
- 25/25 integration tests passing

**Key Learnings:**
- Having multiple `Start()` overloads creates confusion
- Past-tense command names misleading - commands should be imperative verbs
- Single entry point principle: Aggregates have `Create()`, Sagas have `Start()` - ONE method only

**Details:** [cycle-09-checkout-to-orders-integration.md](./cycles/cycle-09-checkout-to-orders-integration.md)

---

### Cycle 8: Checkout Migration - Moving to Orders BC - ✅ Complete (2026-01-13)

**Objective:** Migrate Checkout aggregate from Shopping BC to Orders BC for clearer bounded context boundaries

**Strategy:** Move only Checkout aggregate; Cart remains in Shopping BC

**Key Decision:** [ADR 0001: Checkout Migration to Orders](../decisions/0001-checkout-migration-to-orders.md)

**Rationale:**
- Shopping BC focuses on pre-purchase experience (browsing, cart management, future: product search, wishlists)
- Orders BC owns order lifecycle from checkout through delivery
- Natural workflow split: Cart (exploratory) vs Checkout (transactional)

**Integration Pattern:** `Shopping.CheckoutInitiated` (published by Cart) → `Orders.CheckoutStarted` (handled by Orders)

**Results:** All 6 checkout tests passing in Orders BC. 98/98 tests passing solution-wide.

**Details:** [cycle-08-checkout-migration.md](./cycles/cycle-08-checkout-migration.md)

---

## Upcoming Cycles (Planned)

**Cycle 25:** Returns BC Phase 1 — Self-Service Returns + Order History page
- Domain spec ready: `docs/features/returns/` (4 feature files)
- Prerequisite: Cycle 24 Fulfillment + saga work complete ✅
- Pre-implementation tasks from stakeholder observations documented in Cycle 24 plan

**Cycle 26:** Notifications BC Phase 1 — Transactional email
- Phase 1a: `OrderPlaced`, `ShipmentDispatched` (existing BC events)
- Phase 1b: Returns events (`ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnExpired`)

**Cycle 27:** Promotions BC Phase 1 — Coupons and discounts
- Shopping BC already has `CouponApplied`/`CouponRemoved` placeholder events

**Cycle 28+:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling
- Event Modeling: `docs/planning/admin-portal-event-modeling.md`
- RBAC ADR must exist before implementation begins

---

## Historical Cycles (Cycles 1-18)

For brevity, early cycles are summarized here. Full details available in individual cycle docs under [cycles/](./cycles/).

**Cycle 1-3:** Core BC development (Payments, Inventory, Fulfillment) with Orders saga integration
**Cycle 4:** Shopping BC (Cart + Checkout)
**Cycle 5-7:** Modern Critter Stack refactoring (write-only aggregates, `[WriteAggregate]` pattern)
**Cycle 8-9:** Checkout Migration to Orders BC + Checkout-to-Orders integration
**Cycle 13:** Customer Identity BC — EF Core migration
**Cycle 14:** Product Catalog BC Phase 1 — Core CRUD
**Cycle 15:** Customer Experience Prerequisites — Query endpoints + standardization
**Cycle 16:** Customer Experience BFF + Blazor — Storefront with SSE real-time
**Cycle 17:** Customer Identity Integration — Real customer data flow
**Cycle 18:** Customer Experience Phase 2 — Shopping commands, real data, UI polish
**Cycle 19:** Authentication & Authorization — Cookie-based auth, protected routes
**Cycle 19.5:** Complete Checkout Workflow — Wired checkout stepper to backend APIs

---

## Key Metrics

**Solution-Wide Test Results:** ✅ ~895 tests ([Fact] + [Theory] attributes)
- Payments: 30 tests (11 unit + 19 integration)
- Inventory: 16 integration tests
- Fulfillment: 38 tests (14 unit + 24 integration)
- Shopping: ~56 integration tests
- Orders: ~45 tests (unit + integration)
- Customer Identity: 12 integration tests
- Product Catalog: 24 integration tests
- Customer Experience (Storefront): 13 integration tests
- Pricing: ~151 tests (140 Money unit + 11 integration)
- Vendor Identity: ~57 integration tests
- Vendor Portal: ~86 integration tests

**Bounded Contexts Complete:** 10/10 Phase 1 (100%)
- ✅ Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience, Pricing, Vendor Portal + Vendor Identity

---

## Workflow Reference

**Before Starting a Cycle:**
1. Create cycle plan in `cycles/cycle-NN-name.md`
2. Write 2-3 Gherkin `.feature` files for key user stories
3. Review CONTEXTS.md for integration requirements
4. Update this file (move cycle from "Upcoming" to "Current")

**During a Cycle:**
1. Implement features using `.feature` files as acceptance criteria
2. Write integration tests verifying Gherkin scenarios
3. Update cycle plan with "Implementation Notes"
4. Create ADRs for architectural decisions

**After Completing a Cycle:**
1. Mark cycle complete in this file (add completion date + summary)
2. Update CONTEXTS.md with new integration flows
3. Archive detailed notes in cycle-specific doc
4. Plan next cycle based on backlog

---

## Infrastructure Initiatives

For non-feature development work (CI/CD, monitoring, tooling):

**[CI/CD Workflow Modernization](./infrastructure/ci-cd/)** - 6-phase roadmap to modernize GitHub Actions pipeline
- [ADR 0007](../decisions/0007-github-workflow-improvements.md) - Full architectural decision
- [Quick Start](./infrastructure/ci-cd/README.md) - Overview and current status

---

**Last Updated:** 2026-03-12 (Cycle 24 Complete — Fulfillment Integrity + Returns Prerequisites)
**Maintained By:** Erik Shafer / Claude AI Assistant
