# Current Development Milestone

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development milestone.
> It is the fallback when GitHub Issues/Projects are not directly accessible.
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="M31.5", state="open")`
> This works identically on any machine — MacBook, Windows PC, Linux laptop.
>
> **⚡ New:** We've migrated from "Cycle N Phase M" to **Milestone-Based Versioning** (M.N format).
> See [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) and [Milestone Mapping](milestone-mapping.md) for details.

---

## 🤖 LLM Navigation Guide

**Choose your section based on your task:**

| **Your Task** | **Go To Section** | **Purpose** |
|---------------|-------------------|-------------|
| 📊 **Quick status check** | [Quick Status](#quick-status) | One-table snapshot of current state |
| 🚀 **Starting new work** | [Active Milestone](#active-milestone) | Detailed info on current milestone only |
| ✅ **Recording completion** | [Recent Completions](#recent-completions) | Add to top of list (keep last 3 milestones) |
| 📦 **Looking up old milestone** | [Milestone Archive](#milestone-archive) | Full historical record (M29.1 and earlier) |
| 🗺️ **Planning next work** | [Roadmap](#roadmap) | Next 3-4 milestones + future BCs |
| 🔗 **Finding references** | [Quick Links](#quick-links) | GitHub, docs, ADRs |

**Update Instructions:**
- **Milestone starts:** Update [Active Milestone](#active-milestone) section + Quick Status table
- **Milestone completes:** Move Active → [Recent Completions](#recent-completions), update Quick Status, add retrospective link
- **Archiving old milestones:** After 3 milestones in Recent Completions, move oldest to [Milestone Archive](#milestone-archive)
- **Roadmap changes:** Update [Roadmap](#roadmap) with new priorities (keep focused on next 3-4 only)

---

## Quick Status

| Aspect | Status |
|--------|--------|
| **Current Milestone** | M32.1 — Backoffice Phase 2: Write Operations |
| **Status** | 🚀 IN PROGRESS — Sessions 1-5 completed, endpoint testing complete |
| **Deliverables** | Blazor WASM frontend, write operations (Product Catalog, Pricing, Inventory), E2E tests |
| **Next Session** | Session 6: Blazor WASM scaffolding (Backoffice.Web project) |
| **Active BCs** | 18 total (including Backoffice BFF) |

*Last Updated: 2026-03-17*

---

## Active Milestone

### 🚀 M32.1: Backoffice Phase 2 — Write Operations

**Status:** 🚀 **IN PROGRESS** — Sessions 1-5 completed, endpoint testing complete (all 97 tests passing)
**Duration Estimate:** 3-4 cycles (12-18 sessions)
**Current Phase:** Ready for Blazor WASM frontend scaffolding (Session 6)

**What's Shipping:**
- **Phase 2 Prerequisite (Sessions 1-3):** Domain BC endpoint gaps closed (Product Catalog write, Pricing write, Inventory write, Payments query)
- **Blazor WASM Frontend (Sessions 4-8):** Backoffice.Web with JWT auth, role-based navigation, dashboard UI, CS workflows UI
- **Write Operations UI (Sessions 9-12):** Product admin, pricing admin, warehouse admin, user management
- **E2E Testing (Sessions 13-15):** Playwright tests for critical workflows
- **Documentation (Session 16):** Retrospectives, skills updates, gap register closure

**Phase 2 Approach:**
1. **Sessions 1-3:** Close 9 endpoint gaps in domain BCs (prerequisite for write operations)
2. **Sessions 4-8:** Build Blazor WASM frontend shell with JWT auth and read-only views
3. **Sessions 9-12:** Add write operations UI (product, pricing, inventory, users)
4. **Sessions 13-15:** Write E2E tests with Playwright + Reqnroll
5. **Session 16:** Documentation and retrospective

**Key Decisions:**
- Session 1 will write **4 ADRs documenting M32.0 decisions** (0034-0037: BFF Architecture, SignalR Hub, Projections Strategy, OrderNote Ownership)
- Blazor WASM follows Vendor Portal pattern (in-memory JWT, background token refresh, SignalR with JWT Bearer)
- E2E tests use real Kestrel servers (not TestServer) for SignalR testing
- Gap closure first (Sessions 1-3) prevents mid-cycle blockers

**Session 1 Goals:** ✅ COMPLETED
- ✅ Write ADRs 0034-0037 (M32.0 architectural decisions)
- ✅ Close Product Catalog admin write endpoint gaps (update description, update display name, delete product)
- ✅ Add multi-issuer JWT to Product Catalog BC (Backoffice scheme)
- ✅ 10+ integration tests for Product Catalog write endpoints

**Session 2 Goals:** ✅ COMPLETED (with deferred tests)
- ✅ Close Pricing BC write endpoint gaps (set base price, schedule price change, cancel schedule)
- ✅ Add multi-issuer JWT to Pricing BC (Backoffice scheme)
- ✅ Implement floor/ceiling constraint enforcement
- ⚠️ Integration tests (deferred to Session 4 due to timeout)

**Session 3 Goals:** ✅ COMPLETED
- ✅ Close Inventory BC write endpoints (adjust inventory, receive inbound stock)
- ✅ Close Payments BC query endpoint (list payments for order)
- ✅ Update Gap Register (9 Phase 2 blockers → 1 blocker)
- ✅ Session 2 and Session 3 retrospectives completed

**Session 4 Goals:** ✅ COMPLETED
- ✅ Fix Pricing BC integration tests (25 tests, all passing)
- ✅ Add authorization bypass pattern to test fixtures
- ✅ Fix missing Apply method for ProductRegistered event
- ✅ Session 4 retrospective completed

**Session 5 Goals:** ✅ COMPLETED
- ✅ Fix Inventory BC integration tests (48 tests, all passing)
- ✅ Fix Payments BC integration tests (24 tests, all passing)
- ✅ Add AdjustInventoryRequestValidator for HTTP endpoint validation
- ✅ Multi-policy authorization bypass (CustomerService + FinanceClerk)
- ✅ Session 5 retrospective completed

**Session 6 Goals:** (Next)
- Begin Blazor WASM scaffolding (Backoffice.Web project)
- Basic project structure following Vendor Portal pattern
- JWT authentication infrastructure (in-memory token storage)
- Login page + authentication state provider
- Stub navigation shell (AppBar, Drawer, role-based menu)

**References:**
- [M32.1 Plan](./milestones/m32-1-backoffice-phase-2-plan.md)
- [M32.0 Retrospective](./milestones/m32-0-retrospective.md)
- [Session 1 Retrospective](./milestones/m32-1-session-1-retrospective.md)
- [Session 2 Retrospective](./milestones/m32-1-session-2-retrospective.md)
- [Session 3 Retrospective](./milestones/m32-1-session-3-retrospective.md)
- [Session 4 Retrospective](./milestones/m32-1-session-4-retrospective.md)
- [Session 5 Retrospective](./milestones/m32-1-session-5-retrospective.md)
- [Backoffice Event Modeling](./backoffice-event-modeling-revised.md)
- [Backoffice Frontend Design](./backoffice-frontend-design.md)
- [Frontend Design Alignment Analysis](./backoffice-frontend-design-alignment-analysis.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md)
- [ADR 0034: Backoffice BFF Architecture](../decisions/0034-backoffice-bff-architecture.md)
- [ADR 0035: Backoffice SignalR Hub Design](../decisions/0035-backoffice-signalr-hub-design.md)
- [ADR 0036: BFF-Owned Projections Strategy](../decisions/0036-bff-projections-strategy.md)
- [ADR 0037: OrderNote Aggregate Ownership](../decisions/0037-ordernote-aggregate-ownership.md)

**Deferred to Phase 3:**
- Promotions management UI
- CSV/Excel exports
- Bulk operations pattern
- Returns analytics dashboard
- Audit log viewer

---

## Recent Completions

> **Contains:** Last 3 completed milestones for quick reference.
> **Archive Policy:** After 3 milestones accumulate, move oldest to [Milestone Archive](#milestone-archive).

### ✅ M32.0: Backoffice Phase 1 — Read-Only Dashboards (2026-03-16)

**What shipped:**
- Backoffice BFF (Backend-for-Frontend) for internal operations portal
- CS agent workflows: customer search, order lookup, return management, correspondence history, order notes
- Executive dashboard with 5 real-time KPIs (order count, revenue, AOV, payment failure rate)
- Operations alert feed with SignalR push notifications
- Warehouse clerk tools: stock visibility, low-stock alerts, alert acknowledgment
- BFF-owned Marten projections (AdminDailyMetrics, AlertFeedView)
- OrderNote aggregate (BFF-owned internal CS comments)
- 75 integration tests (Alba + TestContainers) — all passing
- 14+ RabbitMQ event subscriptions from 7 domain BCs

**Key Technical Wins:**
- BFF pattern consistency (3rd successful implementation: Storefront, Vendor Portal, Backoffice)
- Multi-issuer JWT integration (domain BCs accept tokens from 2+ identity providers)
- BFF-owned projections for real-time dashboards (alternative to Analytics BC)
- Integration testing pattern for multi-BC BFFs (stub client fixture design)
- OrderNote aggregate ownership decision (ADR 0037 — operational metadata belongs in BFF)

**Key Decisions:**
- Pre-wired SignalR configuration accelerated Session 8 (3h → 2h)
- Inline projections require explicit `SaveChangesAsync()` before querying
- Role-based SignalR groups scale better than user-specific groups for internal portals
- Stub clients must mirror real BC API design (separate list vs detail storage)

**Build Status:** 0 errors, 7 pre-existing warnings (OrderNoteTests nullable false positives)

**Duration:** 11 sessions (~28 hours) — within estimate (26-32 hours)

**References:**
- [Milestone Plan](./milestones/m32-0-backoffice-phase-1-plan.md)
- [Milestone Retrospective](./milestones/m32-0-retrospective.md)
- [Session 11 Retrospective](./milestones/m32-0-session-11-retrospective.md)
- [ADR 0031: Backoffice RBAC Model](../decisions/0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)
- [ADR 0033: Backoffice Rename](../decisions/0033-admin-portal-to-backoffice-rename.md)

**ADRs to Write (Phase 2):**
- ADR 0034: Backoffice BFF Architecture
- ADR 0035: Backoffice SignalR Hub Design
- ADR 0036: BFF-Owned Projections Strategy
- ADR 0037: OrderNote Aggregate Ownership

**Deferred to M32.1 (Phase 2):**
- 9 endpoint gaps (Product Catalog write, Pricing write, Inventory write, Payments order query)
- Blazor WASM frontend (Backoffice.Web)
- Write operations (product admin, pricing adjustments, inventory adjustments)
- E2E tests (Playwright)

*Completed: 2026-03-16*

---

### ✅ M31.5: Backoffice Prerequisites (2026-03-16)

**What shipped:**
- 8 Phase 0.5 blocking gaps closed across 5 sessions
- GetCustomerByEmail endpoint (Customer Identity BC)
- Inventory BC HTTP query endpoints (GetStockLevel, GetLowStock)
- Fulfillment BC GetShipmentsForOrder endpoint
- Multi-issuer JWT configuration (5 domain BCs: Orders, Payments, Inventory, Fulfillment, Correspondence)
- Endpoint authorization with `[Authorize]` attributes (17 endpoints across 7 BCs)
- 38 fully defined endpoints ready for Backoffice Phase 1

**Key Decisions:**
- Multi-issuer JWT uses named schemes (`"Backoffice"`, `"Vendor"`)
- Policy-based authorization aligned with ADR 0031 roles
- Product Catalog policy already named "VendorAdmin" (no rename needed)
- GetAddressSnapshot deliberately left unprotected (BC-to-BC integration)

**Build Status:** 0 errors, 7 pre-existing warnings (Correspondence BC unused variables)

**References:**
- [Milestone Plan](./milestones/m31-5-backoffice-prerequisites.md)
- [Session 5 Retrospective](./milestones/m31-5-session-5-retrospective.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md) (updated)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)

*Completed: 2026-03-16*

---

### ✅ M31.0: Correspondence BC Extended (2026-03-15)

**What shipped:**
- 5 new integration handlers: ShipmentDeliveredHandler, ShipmentDeliveryFailedHandler, ReturnDeniedHandler, ReturnExpiredHandler, RefundCompletedHandler
- SMS channel infrastructure: ISmsProvider interface, StubSmsProvider with fake Twilio SID generation
- RabbitMQ Payments BC queue added (correspondence-payments-events)
- All 4 BC integration queues configured: Orders, Fulfillment, Returns, Payments
- 8 total handlers (4 from M28.0 + 4 new from M31.0)

**Key Decisions:**
- Pure choreography pattern scales well (no sagas needed)
- Defer template system and Customer Identity queries to Phase 3+
- Inline HTML templates in handlers for now

**Build Status:** 0 errors, 7 expected warnings (TODO placeholders)

**References:**
- [Retrospective](./cycles/m31-0-retrospective.md)
- CONTEXTS.md updated with M31.0 integration matrix

*Completed: 2026-03-15*

---

### ✅ M30.1: Shopping BC Coupon Integration (2026-03-15)

**What shipped:**
- ApplyCouponToCart + RemoveCouponFromCart command handlers
- Real PromotionsClient integration (ValidateCoupon + CalculateDiscount HTTP calls)
- GetCart enrichment with discount information
- Dual handler pattern (command handler + HTTP endpoint handler classes)
- 11 integration tests covering valid/invalid coupons, empty/terminal carts, discount calculations

**Key Patterns:**
- Wolverine Railway Programming with async external service calls requires separate handler classes
- Alba test fixture DI replacement: RemoveAll + AddSingleton pattern for stub injection
- Single coupon per cart (stacking deferred to M30.3+)

**Skills Refresh:**
- Propagated M30.1 learnings to `wolverine-message-handlers.md` (Railway Programming with async validation)

**References:**
- [Retrospective](./cycles/m30-1-shopping-bc-coupon-retrospective.md)
- CONTEXTS.md updated with Shopping ↔ Promotions bidirectional integration

*Completed: 2026-03-15*

---

### ✅ M30.0: Promotions BC Redemption (2026-03-15)

**What shipped:**
- RedeemCoupon, RevokeCoupon, RecordPromotionRedemption command handlers
- GenerateCouponBatch fan-out pattern (PREFIX-XXXX format)
- CalculateDiscount query with stub CartView
- RecordPromotionRedemptionHandler choreography integration with Orders BC
- ExpireCoupon scheduled message (promotion end date expiry)
- 29 integration tests across lifecycle, validation, redemption, discount calculation

**Key Patterns:**
- Handlers manually loading aggregates must use `session.Events.Append()` (not tuple returns)
- Draft promotions can issue coupons (enables batch generation before activation)
- **Banker's Rounding:** `Math.Round(6.825, 2)` → 6.82 (even), not 6.83 — affects discount calculations

**Skills Refresh:**
- Updated `wolverine-message-handlers.md` (anti-pattern #8)
- Updated `modern-csharp-coding-standards.md` (banker's rounding)
- Updated `critterstack-testing-patterns.md` (fan-out timing)

**Deferred:**
- Full Shopping BC integration (completed in M30.1)
- Pricing BC floor price enforcement (future)

**References:**
- [Retrospective](./milestones/m30-0-retrospective.md)
- CONTEXTS.md updated with M30.0 implementation status

*Completed: 2026-03-15*

---

## Milestone Archive

> **Contains:** Completed milestones older than the last 3 (M29.1 and earlier).
> **Purpose:** Historical reference without cluttering recent work context.

<details>
<summary><strong>M29.1: Promotions BC Core — MVP (2026-03-14 to 2026-03-15)</strong></summary>

**What shipped:**
- Event-sourced Promotion aggregate (UUID v7) with 6 domain events
- Event-sourced Coupon aggregate (UUID v5 from code) with 4 domain events
- Command handlers: CreatePromotion, ActivatePromotion, IssueCoupon
- CouponLookupView projection (case-insensitive coupon validation)
- ValidateCoupon query endpoint with business rules
- Marten snapshot projections (Promotion + Coupon)
- 11 integration tests (all passing)
- Port 5250 allocated

**Pattern Discoveries:**
- IStartStream return type for event stream creation
- Snapshot projection requirement for queryability

**Deferred to M30.0:** Redemption tracking, batch generation, Shopping/Pricing integration

[Retrospective](./cycles/cycle-29-phase-2-retrospective-notes.md)

</details>

<details>
<summary><strong>M29.0: Backoffice Identity BC (2026-03-14)</strong></summary>

**What shipped:**
- ADR 0031: RBAC model (7 roles, policy-based authorization)
- EF Core entity model: AdminUser, AdminRole, AdminUserStatus, BackofficeIdentityDbContext
- Authentication handlers: Login, RefreshToken, Logout (JWT + refresh token rotation)
- User management handlers: CreateAdminUser, GetAdminUsers, ChangeAdminUserRole, DeactivateAdminUser
- JWT token generation with 7 authorization policies
- API endpoints: 3 auth + 4 user management (Wolverine HTTP)
- Infrastructure: Docker Compose, Aspire, database, port 5249

[Retrospective](./cycles/cycle-29-admin-identity-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M28.0: Correspondence BC Core (2026-03-13 to 2026-03-14)</strong></summary>

**What shipped:**
- Message aggregate (event-sourced) — 4 domain events, retry lifecycle
- Provider interfaces (IEmailProvider, StubEmailProvider)
- OrderPlacedHandler — email order confirmations
- SendMessage handler — exponential backoff retry (5min, 30min, 2hr)
- MessageListView projection (inline)
- HTTP query endpoints (GetMessagesForCustomer, GetMessageDetails)
- 12 unit tests + 5 integration tests

[Retrospective](./cycles/cycle-28-correspondence-bc-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M25.2: Returns BC Exchanges (2026-03-13)</strong></summary>

**What shipped:**
- Exchange workflow (UC-11) — ReturnType enum, ExchangeRequest, 5 exchange domain events, 3 command handlers
- 6 integration messages for exchange workflow
- CE SignalR handlers — 7 handlers, ReturnStatusChanged discriminated union event
- Sequential returns — IsReturnInProgress → ActiveReturnIds saga refactor
- Anticorruption layer — EnumTranslations static class
- Cross-BC smoke tests (3-host Alba fixture)

[Plan](./cycles/cycle-27-returns-bc-phase-3.md) | [Retrospective](./cycles/cycle-27-returns-bc-phase-3-retrospective.md)

</details>

<details>
<summary><strong>M25.1: Returns BC Mixed Inspection (2026-03-12 to 2026-03-13)</strong></summary>

**What shipped:**
- ReturnCompleted expanded with per-item disposition
- 5 new integration events (ReturnApproved, ReturnRejected, ReturnExpired, ReturnReceived, ReturnedItem)
- Mixed inspection three-way logic
- GetReturnsForOrder query (Marten inline snapshots)
- RabbitMQ dual-queue routing + Fulfillment queue wiring fix
- ~99 total return-related tests

[Plan](./cycles/cycle-26-returns-bc-phase-2.md) | [Retrospective](./cycles/cycle-26-returns-bc-phase-2-retrospective.md)

</details>

<details>
<summary><strong>M25.0: Returns BC Core (2026-03-12)</strong></summary>

**What shipped:**
- Event-sourced Return aggregate (10 lifecycle states, 9 domain events)
- 6 command handlers + 7 API endpoints (port 5245)
- ReturnEligibilityWindow from Fulfillment.ShipmentDelivered
- Auto-approval logic + restocking fee calculation
- 48 unit tests + 5 integration tests

[Plan & Retrospective](./cycles/cycle-25-returns-bc-phase-1.md)

</details>

<details>
<summary><strong>Cycle 24: Fulfillment Integrity + Returns Prerequisites (2026-03-12)</strong></summary>

**What shipped:**
- RabbitMQ transport wired in Fulfillment.Api
- RecordDeliveryFailure endpoint + ShipmentDeliveryFailed cascade
- UUID v5 idempotent shipment creation
- SharedShippingAddress with dual JSON annotations
- Orders saga return handlers + IsReturnInProgress guard
- GET /api/orders/{orderId}/returnable-items endpoint

[Plan](./cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

</details>

<details>
<summary><strong>Cycle 23: Vendor Portal E2E Testing (2026-03-11)</strong></summary>

**What shipped:**
- 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host)
- 12 BDD scenarios (P0 + P1a) across 3 feature files
- Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
- SignalR hub message injection testing

[Plan](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Skills Update](../skills/e2e-playwright-testing.md)

</details>

<details>
<summary><strong>Cycle 22: Vendor Portal + Vendor Identity Phase 1 (2026-03-08 to 2026-03-10)</strong></summary>

**What shipped (6 phases):**
- Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
- Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
- Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
- Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
- Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
- Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler)
- 143 integration tests (100% pass rate)

[Event Modeling](vendor-portal-event-modeling.md) | [Retrospective](./cycles/cycle-22-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/16)

</details>

<details>
<summary><strong>Cycle 21: Pricing BC Phase 1 (2026-03-07 to 2026-03-08)</strong></summary>

**What shipped:**
- ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
- Money value object (140 unit tests)
- CurrentPriceView inline projection (zero-lag queries)
- Shopping BC security fix (server-authoritative pricing)
- 5 ADRs written
- 151 Pricing tests + 56 Shopping tests

[Plan](pricing-event-modeling.md) | [Retrospective](./cycles/cycle-21-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

</details>

<details>
<summary><strong>Cycle 20: Automated Browser Testing (2026-03-04 to 2026-03-07)</strong></summary>

**What shipped:**
- Playwright + Reqnroll E2E testing infrastructure
- Real Kestrel servers (not TestServer) for SignalR testing
- Page Object Model with data-testid selectors
- MudBlazor component interaction patterns
- Playwright tracing for CI failure diagnosis

[Plan](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/2)

</details>

<details>
<summary><strong>Cycle 19.5: Complete Checkout Workflow (2026-03-04)</strong></summary>

**What shipped:**
- Wired checkout stepper to backend APIs
- Checkout initialization + CheckoutId persistence
- Error handling with MudSnackbar toasts
- End-to-end manual testing

[Milestone](https://github.com/erikshafer/CritterSupply/milestone/13)

</details>

<details>
<summary><strong>Cycle 19: Authentication & Authorization (2026-02-25 to 2026-02-26)</strong></summary>

**What shipped:**
- Cookie-based authentication (ASP.NET Core middleware)
- Login/Logout pages with MudBlazor
- Protected routes (Cart, Checkout)
- AppBar authentication UI
- Cart persistence via browser localStorage
- Swagger UI + seed data for ProductCatalog.Api

[Plan](./cycles/cycle-19-authentication-authorization.md) | [Retrospective](./cycles/cycle-19-retrospective.md)

</details>

*Archive Last Updated: 2026-03-15*

---

## Roadmap

> **Contains:** Next 3-4 milestones + future BCs (high-level only).
> **Purpose:** Forward-looking planning without excessive detail.

### Next 3-4 Milestones

- **M32.0+:** Backoffice Phase 1 — Read-Only Dashboards
  - Prerequisites: Multi-issuer JWT (M31.5), endpoint gaps closed (M31.5)
  - Read-only dashboards: Orders, Returns, Customers, Inventory
  - Customer Service tooling: Return approval/denial, correspondence history
  - Event Modeling: [backoffice-event-modeling-revised.md](backoffice-event-modeling-revised.md)
  - Integration Gap Register: [backoffice-integration-gap-register.md](backoffice-integration-gap-register.md)

### Future BCs (Priority Roadmap)

**High Priority (Active Development or Near-Term):**
- 🟢 **Backoffice (M32.0+)** — Internal operations portal

**Medium Priority (Customer-Facing Features):**
- 🟡 **Exchange v2** — Cross-product exchanges, upcharge payment collection
- 🟡 **Product Catalog Evolution** — Variants, Listings, Marketplaces ([plan](catalog-listings-marketplaces-cycle-plan.md) approved — M33–M39 estimated)
- 🟡 **Search BC** — Full-text product search, faceted navigation
- 🟡 **Recommendations BC** — Personalized product recommendations

**Lower Priority (Strategic/Retention):**
- 🔵 **Analytics BC** — Business intelligence, reporting, dashboards
- 🔵 **Store Credit BC** — Gift cards, store credit issuance
- 🔵 **Loyalty BC** — Rewards program, points accumulation
- 🔵 **Operations Dashboard** — Developer/SRE event stream visualization (React + SignalR)

See [CONTEXTS.md — Future Considerations](../../CONTEXTS.md) for full specifications.

*Roadmap Last Updated: 2026-03-15*

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [GitHub Issues](https://github.com/erikshafer/CritterSupply/issues) — Issue tracking
- [GitHub Project Board](https://github.com/users/erikshafer/projects/9) — Kanban board
- [Historical Cycles](./cycles/) — Markdown retrospectives
- [Milestone Mapping](./milestone-mapping.md) — Legacy "Cycle N" → "M.N" translation
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change
- [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) — Milestone-based planning schema

---

*Document Last Updated: 2026-03-17*
*Active Milestone: M32.1 (Backoffice Phase 2) — Sessions 1-3 Completed (Gap Closure Phase Complete)*
*Update Policy: At milestone start, milestone end, and significant task changes*
