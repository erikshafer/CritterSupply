# M32.0 Session 1 — Preparation & Discovery

**Date:** 2026-03-16
**Status:** ✅ Planning Complete
**Duration:** ~1 hour (discovery + planning)

---

## Session Goal

Prepare for Backoffice Phase 1 implementation by reviewing prerequisites, creating detailed milestone plan, and documenting discoveries.

---

## What Was Accomplished

### 1. Prerequisites Review

**M31.5 Status Verified:** ✅ ALL COMPLETE
- All 5 sessions delivered (GetCustomerByEmail, Inventory endpoints, Fulfillment endpoint, multi-issuer JWT, endpoint authorization)
- 8 Phase 0.5 blockers closed
- 38 fully defined endpoints ready for integration
- 17 endpoints secured with role-based authorization

**Domain BCs Ready:**
- Customer Identity, Orders, Returns, Payments, Inventory, Fulfillment, Correspondence, Product Catalog

### 2. Planning Documents Reviewed

Comprehensive review of:
- `CURRENT-CYCLE.md` (M31.5 completion status)
- `CONTEXTS.md` (BC ownership and integration directions)
- `backoffice-event-modeling-revised.md` (2,642 lines — complete domain analysis)
- `backoffice-integration-gap-register.md` (endpoint inventory, Phase 0.5 completion status)
- ADR 0031 (Backoffice RBAC model)
- ADR 0032 (Multi-issuer JWT strategy)
- ADR 0033 (Backoffice rename decision)
- M31.5 Session 5 retrospective

**Key Discovery:** Backoffice event modeling document is comprehensive and current (updated 2026-03-14 post-Cycle 29 reconciliation). All integration points verified against codebase. No gaps in domain analysis.

### 3. Milestone Plan Created

**Created:** `docs/planning/milestones/m32-0-backoffice-phase-1-plan.md` (4,500+ lines)

**Contents:**
- Mission statement
- Prerequisites status (all ✅)
- Phase 1 scope (what's in, what's deferred)
- Architecture decisions (4 ADRs to write)
- Technology stack
- Project structure
- 11 implementation sessions with detailed task lists
- Success criteria (functional + technical)
- Risks & mitigations (5 identified)
- References (planning docs, ADRs, skills, examples)

**Estimated Duration:** 2-3 cycles (10-15 sessions)

### 4. Session Breakdown

**Session 1:** Project scaffolding & infrastructure (2-3 hours)
**Session 2:** HTTP client abstractions (2-3 hours)
**Session 3:** CS workflows Part 1 - Search & Orders (2-3 hours)
**Session 4:** CS workflows Part 2 - Returns & Correspondence (2-3 hours)
**Session 5:** OrderNote aggregate (1-2 hours)
**Session 6:** BFF projections - AdminDailyMetrics (2-3 hours)
**Session 7:** BFF projections - AlertFeedView (2-3 hours)
**Session 8:** SignalR hub (2-3 hours)
**Session 9:** Warehouse clerk dashboard (2-3 hours)
**Session 10:** Integration testing & CI (3-4 hours)
**Session 11:** Documentation & retrospective (2-3 hours)

**Total Estimated Time:** 24-32 hours

---

## Key Discoveries

### 1. BackofficeIdentity BC Already Implemented

**Folder:** `src/Backoffice Identity/`
**Files:** 44 C# files
**Status:** ✅ Delivered in Cycle 29 Phase 1 (PR #375)
**Port:** 5249

**Endpoints Available:**
- `POST /api/backoffice-identity/auth/login` (JWT + refresh cookie)
- `POST /api/backoffice-identity/auth/refresh` (rotate refresh token)
- `POST /api/backoffice-identity/auth/logout` (revoke refresh token)
- `GET /api/backoffice-identity/users` (SystemAdmin only)
- `POST /api/backoffice-identity/users` (SystemAdmin only)
- `PUT /api/backoffice-identity/users/{id}/role` (SystemAdmin only)
- `DELETE /api/backoffice-identity/users/{id}` (SystemAdmin only)

**Integration Ready:** Phase 1 can begin immediately with BackofficeIdentity login flow.

### 2. Naming Already Consistent

**BC Folder Names:**
- `src/Backoffice Identity/` (identity BC)
- `src/Backoffice/` (BFF — to be created)

**Rationale from ADR 0033:**
- "Backoffice" is self-contained compound noun (like "storefront")
- Shorter identifiers everywhere (no "BackofficePortal" verbosity)
- Business users say "the backoffice," not "the backoffice portal"
- 3/3 consensus (PSA, PO, UXE)

### 3. BFF Pattern Established

**Existing BFFs:**
- `src/Customer Experience/Storefront/` + `Storefront.Api/` (customer-facing)
- `src/Vendor Portal/VendorPortal/` + `VendorPortal.Api/` (partner-facing)
- `src/Backoffice/` + `Backoffice.Api/` (internal staff) — **to be created**

**Pattern Consistency:**
- Domain project (regular SDK) + API project (Web SDK)
- Wolverine handler discovery includes both assemblies
- SignalR hub in API project
- Typed HTTP client interfaces in domain project
- Real-time marker interface + discriminated union in domain project

**Lesson:** Backoffice follows exact same pattern as Customer Experience and Vendor Portal. No architectural invention needed — copy proven patterns.

### 4. Phase 1 Scope is Well-Defined

**P0 Features (Must Ship):**
- Customer service workflows (search, orders, returns, correspondence, order notes)
- Executive dashboard KPIs (5 metrics with real-time updates)
- Operations alert feed (4 alert types with SignalR push)
- Warehouse clerk dashboard (stock visibility, alert acknowledgment)

**P1 Features (Should Ship):**
- Real-time SignalR updates
- Week-over-week metric comparisons
- Alert severity tagging

**Deferred to Phase 2:**
- Blazor WASM frontend (Phase 1 is API-only)
- Write operations (Product Catalog, Pricing, Inventory admin endpoints)
- Warehouse return operations (receive, inspect)
- Promotions read-only view
- CSV/Excel exports

### 5. Integration Message Handlers Needed

**BFF Must Subscribe to 14+ Integration Messages:**

From Orders BC:
- `OrderPlaced` (dashboard metrics, alert feed)
- `OrderCancelled` (dashboard metrics)

From Payments BC:
- `PaymentCaptured` (dashboard metrics)
- `PaymentFailed` (alert feed, dashboard metrics)
- `RefundCompleted` (dashboard metrics)

From Fulfillment BC:
- `ShipmentDispatched` (fulfillment pipeline)
- `ShipmentDelivered` (fulfillment pipeline)
- `ShipmentDeliveryFailed` (alert feed)

From Inventory BC:
- `LowStockDetected` (alert feed)
- `StockReplenished` (alert feed auto-resolve)

From Returns BC:
- `ReturnRequested` (dashboard metrics, CS alert)
- `ReturnCompleted` (dashboard metrics)
- `ReturnExpired` (alert feed)

From Correspondence BC:
- `CorrespondenceQueued` (dashboard metrics)
- `CorrespondenceDelivered` (dashboard metrics)
- `CorrespondenceFailed` (alert feed)

**Lesson:** RabbitMQ queue wiring is critical. Use explicit queue names per BC to avoid Cycle 26 Fulfillment incident. Test each subscription independently.

---

## Technical Decisions Made

### 1. Port Allocation

**Backoffice.Api:** 5243 (from CLAUDE.md port allocation table)
**BackofficeIdentity.Api:** 5249 (already allocated and implemented)

**Rationale:** CLAUDE.md port allocation table is the source of truth. 5243 was reserved for Backoffice API since M29.0 planning.

### 2. Schema Naming

**Backoffice.Api:** `backoffice` schema in Postgres

**Rationale:** Matches pattern of other BCs (orders, payments, shopping, etc.). Single-word, lowercase, no hyphens.

### 3. SignalR Hub Path

**Backoffice.Api:** `/hub/backoffice`

**Rationale:** Consistent with Customer Experience (`/hub/storefront`) and Vendor Portal (`/hub/vendorportal`).

### 4. JWT Scheme Name

**Backoffice.Api:** Accept tokens from `"Backoffice"` scheme (issued by BackofficeIdentity BC at port 5249)

**Rationale:** ADR 0032 Multi-Issuer JWT Strategy. Scheme name matches identity BC name.

### 5. Authorization Policy Names

**Backoffice.Api:** Use exact policy names from ADR 0031:
- `CustomerService`
- `WarehouseClerk`
- `OperationsManager`
- `Executive`
- `SystemAdmin`

**Rationale:** Consistency with domain BCs that already accept BackofficeIdentity tokens (M31.5 Session 5).

---

## ADRs to Write (Before Implementation)

### ADR 0034: Backoffice BFF Architecture

**Decision:** BFF pattern (not API gateway)
**Rationale:** Consistency with Customer Experience and Vendor Portal
**Key Points:**
- Composition strategy (HTTP clients + RabbitMQ subscriptions)
- BFF-owned aggregates (OrderNote, AlertAcknowledgment)
- No direct domain BC API exposure

### ADR 0035: Backoffice SignalR Hub Design

**Decision:** Role-based hub groups + JWT Bearer auth
**Key Points:**
- Hub groups: `role:executive`, `role:operations`, `admin-user:{userId}`
- Marker interface: `IBackofficeWebSocketMessage`
- Discriminated union: `BackofficeEvent`

### ADR 0036: BFF-Owned Projections Strategy

**Decision:** Marten inline projections (not Analytics BC)
**Rationale:** Analytics BC does not exist and is low priority
**Key Points:**
- Projections: `AdminDailyMetrics`, `AlertFeedView`, `FulfillmentPipelineView`, `ReturnMetricsView`, `CorrespondenceMetricsView`
- Source: RabbitMQ events from domain BCs
- Lifecycle: Inline (zero lag)

### ADR 0037: OrderNote Aggregate Ownership

**Decision:** OrderNote lives in Backoffice BC (not Orders BC)
**Rationale:** Internal CS notes are operational tooling metadata, not order lifecycle
**Key Points:**
- Storage: Marten document store
- Keying: `{orderId}:{noteId}`
- Boundary: CS internal comments are a BFF concern

---

## Risks Identified

### Risk 1: RabbitMQ Event Subscription Complexity
**Mitigation:** Explicit queue names, independent testing, Alba integration tests

### Risk 2: BFF-Owned Projection Performance
**Mitigation:** Use Marten inline projections (zero lag), monitor via Aspire

### Risk 3: Multi-BC Composition Complexity
**Mitigation:** Parallel HTTP calls, circuit breaker pattern, cache stable data

### Risk 4: OrderNote Aggregate Boundary Confusion
**Mitigation:** Write ADR 0037 clearly, code review enforcement

### Risk 5: Phase 2 Endpoint Gaps
**Mitigation:** Document Phase 2 blockers, create GitHub Issues, estimate 4-5 sessions

---

## What's Next

### Immediate:
- **Commit this planning session** via `report_progress`
- **Begin Session 1:** Project scaffolding & infrastructure

### Session 1 Checklist:
1. Create `Backoffice` and `Backoffice.Api` projects
2. Add to solution files (.sln and .slnx)
3. Configure appsettings.json (Postgres connection string)
4. Configure launchSettings.json (port 5243)
5. Wire Marten (backoffice schema)
6. Wire Wolverine (include both assemblies)
7. Configure multi-issuer JWT (accept BackofficeIdentity tokens)
8. Add authorization policies
9. Add Docker Compose service
10. Verify build

---

## Key Takeaways

### ✅ What Went Well

1. **Prerequisites Complete:** M31.5 delivered all Phase 0.5 blockers. Zero gaps blocking Phase 1.
2. **Comprehensive Domain Analysis:** backoffice-event-modeling-revised.md is current and detailed (2,642 lines).
3. **Clear Examples:** Customer Experience and Vendor Portal BFFs provide proven patterns to copy.
4. **Backoffice Identity Ready:** 44 C# files, 7 endpoints, JWT issuer working.
5. **Detailed Planning:** M32.0 plan is comprehensive (11 sessions, 4 ADRs, risks, success criteria).

### 🔄 What Could Be Improved

1. **ADR Backlog:** 4 ADRs to write before implementation. Should write ADR 0034 (BFF Architecture) in Session 1.
2. **Integration Test Planning:** Need to clarify Alba + TestContainers setup early (Session 2 or 3, not Session 10).

### 💡 Lessons Learned

1. **Planning Before Coding:** Spending 1 hour on planning saves 5+ hours of rework.
2. **Event Modeling is Gold:** backoffice-event-modeling-revised.md eliminated ambiguity about what to build.
3. **Copy Proven Patterns:** BFF pattern is identical to Customer Experience and Vendor Portal. No invention needed.
4. **Prerequisites Matter:** M31.5 closed 8 gaps. Without it, Phase 1 would be blocked immediately.

---

## Next Session Preview

**Session 1: Project Scaffolding & Infrastructure**
**Estimated Time:** 2-3 hours
**Goal:** Create Backoffice projects, wire Marten + Wolverine + JWT, verify build

---

*Planning session completed: 2026-03-16*
