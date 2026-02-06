# Development Cycles

This document tracks active and recent development cycles. For complete historical details, see individual cycle plans in [`cycles/`](./cycles/).

---

## Current Cycle

**None** - Ready for next cycle

Cycle 16 completed 2026-02-05. See "Recently Completed" below.

---

## Recently Completed (Last 5 Cycles)

### Cycle 16: Customer Experience BC (BFF + Blazor) - ✅ Complete (2026-02-05)

**Objective:** Build customer-facing storefront using Backend-for-Frontend (BFF) pattern with Blazor Server and Server-Sent Events (SSE)

**Key Deliverables:**
- 3-project BFF structure (Storefront domain, Storefront.Api, Storefront.Web)
- EventBroadcaster pattern with `Channel<T>` for in-memory pub/sub
- SSE real-time integration (discriminated unions, customer isolation)
- Blazor Server frontend with MudBlazor Material Design components
- 4 pages: Home, Cart (SSE-enabled), Checkout (MudStepper), Order History (MudTable)
- JavaScript EventSource client for SSE subscriptions
- Interactive component pattern (solving Blazor render mode limitation)
- Comprehensive skills documentation (`skills/bff-realtime-patterns.md`)

**Results:** 13/17 tests passing (4 deferred - real data integration). Manual browser testing passed all acceptance criteria.

**Key Decisions:**
- [ADR 0004: SSE over SignalR](../decisions/0004-sse-over-signalr.md)
- [ADR 0005: MudBlazor UI Framework](../decisions/0005-mudblazor-ui-framework.md)
- [ADR 0006: Reqnroll BDD Framework](../decisions/0006-reqnroll-bdd-framework.md)

**Deferred to Future Cycles:**
- Backend RabbitMQ integration (end-to-end SSE flow)
- Real cart/checkout data integration
- Authentication with Customer Identity BC
- Automated browser testing (Playwright/Selenium/bUnit)

**Details:** [cycle-16-customer-experience.md](./cycles/cycle-16-customer-experience.md)

---

### Cycle 15: Customer Experience Prerequisites - ✅ Complete (2026-02-03)

**Objective:** Add query endpoints and standardize configuration for BFF readiness

**Key Deliverables:**
- Query endpoints: `GET /api/carts/{cartId}`, `GET /api/checkouts/{checkoutId}`, `GET /api/orders?customerId={customerId}`
- Connection string standardization across all API projects (port 5433, single database)
- Port allocation table documented in CLAUDE.md

**Results:** All 133 tests passing. All APIs start cleanly with docker-compose.

**Details:** [cycle-15-customer-experience-prerequisites.md](./cycles/cycle-15-customer-experience-prerequisites.md)

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

**Cycle 17:** Product Catalog Phase 2 (Category Management)
- Full Category subdomain with marketplace mappings
- Category hierarchy (parent/child relationships)
- Many-to-many relationships (internal categories → marketplace categories)

**Cycle 18:** Vendor Identity + Vendor Portal Phase 1
- Vendor tenant and user authentication (EF Core)
- Read-only analytics (sales projections, inventory snapshots)
- Multi-tenancy with Marten

**Cycle 19:** Returns BC
- Return authorization and eligibility windows
- Reverse logistics workflow
- Integration with Orders saga (return states)

**Cycle 20+:** Product Catalog Phase 3 (Search), Vendor Portal Phase 2 (Change Requests)

[View Backlog](./BACKLOG.md)

---

## Historical Cycles (Cycles 1-7)

For brevity, early cycles (Payments, Inventory, Fulfillment, Shopping, refactoring cycles) are summarized here. Full details available in [ARCHIVE.md](./cycles/ARCHIVE.md).

**Cycle 1-3:** Core BC development (Payments, Inventory, Fulfillment) with Orders saga integration
**Cycle 4:** Shopping BC (Cart + Checkout)
**Cycle 5-7:** Modern Critter Stack refactoring (write-only aggregates, `[WriteAggregate]` pattern)

---

## Key Metrics

**Solution-Wide Test Results:** ✅ 146/150 tests passing (97.3% success rate)
- Payments: 30 tests (11 unit + 19 integration)
- Inventory: 16 integration tests
- Fulfillment: 6 integration tests
- Shopping: 13 integration tests
- Orders: 32 integration tests
- Customer Identity: 12 integration tests
- Product Catalog: 24 integration tests
- Customer Experience (Storefront): 13 integration tests (4 deferred to Phase 3)

**Bounded Contexts In Progress:** 0/10
- (None - ready for next cycle)

**Bounded Contexts Complete:** 8/10 (80%)
- ✅ Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience
- 📋 Vendor Identity, Vendor Portal (Future)

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

**Last Updated:** 2026-02-05 (Cycle 16 Complete)
**Maintained By:** Erik Shafer / Claude AI Assistant
