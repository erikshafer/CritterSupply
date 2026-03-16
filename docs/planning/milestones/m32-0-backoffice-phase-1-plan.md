# M32.0 — Backoffice Phase 1: Read-Only Dashboards & CS Tooling

**Date:** 2026-03-16
**Status:** 🚀 Active
**Prerequisites:** ✅ All complete (M31.5 — Multi-issuer JWT + endpoint gaps)
**Estimated Duration:** 2-3 cycles (10-15 sessions)

---

## Table of Contents

1. [Mission](#mission)
2. [Prerequisites Status](#prerequisites-status)
3. [Phase 1 Scope](#phase-1-scope)
4. [Architecture Decisions](#architecture-decisions)
5. [Implementation Sessions](#implementation-sessions)
6. [Success Criteria](#success-criteria)
7. [Risks & Mitigations](#risks--mitigations)
8. [References](#references)

---

## Mission

Build the **Backoffice BFF** (Backend-for-Frontend) to provide internal employees with:
- **Read-only operational dashboards** for executives and operations managers
- **Customer service tooling** for order lookup, return management, and WISMO tickets
- **Real-time operational alerts** via SignalR for payment failures, low stock, and delivery issues
- **Warehouse clerk dashboards** for inventory visibility and alert acknowledgment
- **Internal audit trails** with OrderNote aggregate for CS comments

This is the foundation for all future Backoffice phases (Phase 2: write operations, Phase 3: advanced features).

---

## Prerequisites Status

### M31.5 — Backoffice Prerequisites ✅ COMPLETE

All Phase 0.5 blockers closed across 5 sessions:

| Session | Deliverable | Status |
|---------|------------|--------|
| 1 | GetCustomerByEmail endpoint (Customer Identity) | ✅ Complete |
| 2 | Inventory BC HTTP query endpoints (GetStockLevel, GetLowStock) | ✅ Complete |
| 3 | Fulfillment BC GetShipmentsForOrder endpoint | ✅ Complete |
| 4 | Multi-issuer JWT configuration (5 domain BCs) | ✅ Complete |
| 5 | Endpoint authorization with [Authorize] (17 endpoints, 7 BCs) | ✅ Complete |

**Domain BCs Ready for Integration:**
- Customer Identity, Orders, Returns, Payments, Inventory, Fulfillment, Correspondence, Product Catalog

**Fully Defined Endpoints:** 38 (sufficient for Phase 1)

---

## Phase 1 Scope

### What's In Scope (P0 - Must Ship)

**1. Backoffice BFF Infrastructure**
- BFF projects: `Backoffice` (domain), `Backoffice.Api` (Web SDK)
- Marten document store for BFF-owned projections
- Multi-issuer JWT validation (accept BackofficeIdentity tokens)
- Authorization policies: `CustomerService`, `WarehouseClerk`, `OperationsManager`, `Executive`, `SystemAdmin`
- SignalR hub at `/hub/backoffice` with role-based groups

**2. Customer Service Workflows (P0)**
- Customer search by email (→ Customer Identity)
- Full customer order history (→ Orders)
- Order detail view with saga timeline
- Order cancellation with admin attribution
- Return lookup by order (→ Returns)
- Return detail view with lifecycle state
- Return approval/denial (CS workflow)
- Correspondence history view (→ Correspondence)
- Order notes aggregate (BFF-owned, internal CS comments)

**3. Executive Dashboard KPIs (P1)**
- BFF-owned Marten projections:
  - `AdminDailyMetrics` (order count, revenue, AOV, payment failure rate)
  - `FulfillmentPipelineView` (order distribution by saga state)
  - `ReturnMetricsView` (active return rate)
  - `CorrespondenceMetricsView` (delivery success rate)
- Real-time updates via RabbitMQ → Wolverine → SignalR
- Week-over-week comparison for key metrics

**4. Operations Alert Feed (P1)**
- BFF projection: `AlertFeedView` (sourced from RabbitMQ events)
- Alert types: payment failures, low stock, delivery failures, return expirations
- SignalR push to `role:operations` group
- Alert severity tagging (⚠️ warning, 🔴 critical)

**5. Warehouse Clerk Dashboard (P1)**
- Stock level visibility (→ Inventory `GetStockLevel`)
- Low-stock alert viewing (→ Inventory `GetLowStock`)
- Alert acknowledgment (BFF-owned `AlertAcknowledgment` aggregate)

**6. Integration Testing**
- Alba + TestContainers test fixture
- Multi-BC composition tests (customer → orders → returns)
- SignalR hub authorization tests
- Projection update tests (RabbitMQ event → Marten → SignalR)

### What's Out of Scope (Deferred)

**Phase 2 Blockers (9 gaps remaining):**
- Product Catalog admin write endpoints (CopyWriter role)
- Pricing BC admin write endpoints (PricingManager role)
- Inventory BC write endpoints (WarehouseClerk adjust/receive stock)
- Payments BC order query (list payments for order)

**Phase 2+ Features:**
- Blazor WASM frontend (Phase 2+)
- Warehouse return operations (receive, inspect)
- Exchange approval workflows
- Promotions read-only view
- CSV/Excel report exports

**Blocked by Missing BCs:**
- Store credit issuance (Store Credit BC does not exist)
- Full analytics dashboards (Analytics BC does not exist)

---

## Architecture Decisions

### ADRs to Write

**1. ADR 0034: Backoffice BFF Architecture**
- Decision: BFF pattern (not API gateway)
- Composition strategy: HTTP clients + RabbitMQ subscriptions
- BFF-owned aggregates: `OrderNote`, `AlertAcknowledgment`
- Rationale: Consistency with Customer Experience and Vendor Portal patterns

**2. ADR 0035: Backoffice SignalR Hub Design**
- Decision: Role-based hub groups (`role:executive`, `role:operations`, `admin-user:{userId}`)
- Marker interface: `IBackofficeWebSocketMessage`
- Discriminated union: `BackofficeEvent` (similar to `StorefrontEvent`, `VendorPortalEvent`)
- Authentication: JWT Bearer (not session cookies)

**3. ADR 0036: BFF-Owned Projections Strategy**
- Decision: Marten inline projections for dashboard metrics (not Analytics BC)
- Source: RabbitMQ events from domain BCs
- Rationale: Analytics BC does not exist and is low priority; BFF projections are sufficient

**4. ADR 0037: OrderNote Aggregate Ownership**
- Decision: OrderNote lives in Backoffice BC, NOT Orders BC
- Rationale: Internal CS notes are operational tooling metadata, not part of order lifecycle
- Storage: Marten document store, keyed by `{orderId}:{noteId}`

### Technology Stack

| Component | Technology | Port |
|-----------|-----------|------|
| Backoffice (domain) | Class library (regular SDK) | N/A |
| Backoffice.Api (BFF) | ASP.NET Core Web API | 5243 |
| Database | PostgreSQL (`backoffice` schema) | 5433 |
| Event Store | Marten 8+ (document + event sourcing) | — |
| Message Bus | Wolverine 5+ + RabbitMQ | 5672 |
| Real-Time | SignalR via `opts.UseSignalR()` | — |
| Authentication | JWT Bearer (BackofficeIdentity BC) | — |
| HTTP Clients | Typed HttpClient implementations | — |
| Testing | Alba + TestContainers | — |

### Project Structure

```
src/Backoffice/
├── Backoffice/                          # Domain project
│   ├── Clients/                         # HTTP client interfaces
│   │   ├── IOrdersClient.cs
│   │   ├── IReturnsClient.cs
│   │   ├── ICustomerIdentityClient.cs
│   │   ├── ICorrespondenceClient.cs
│   │   ├── IInventoryClient.cs
│   │   └── IFulfillmentClient.cs
│   ├── Composition/                     # View models for UI
│   │   ├── CustomerServiceView.cs
│   │   ├── OrderDetailView.cs
│   │   ├── ReturnDetailView.cs
│   │   └── DashboardMetrics.cs
│   ├── Notifications/                   # Integration message handlers
│   │   ├── OrderPlacedAdminHandler.cs
│   │   ├── PaymentFailedAdminHandler.cs
│   │   ├── LowStockAdminHandler.cs
│   │   └── ShipmentDeliveryFailedAdminHandler.cs
│   ├── RealTime/                        # SignalR transport types
│   │   ├── IBackofficeWebSocketMessage.cs
│   │   └── BackofficeEvent.cs
│   ├── OrderNote/                       # BFF-owned aggregate
│   │   ├── OrderNote.cs
│   │   ├── AddOrderNote.cs
│   │   └── GetOrderNotes.cs
│   └── Projections/                     # Marten projection definitions
│       ├── AdminDailyMetrics.cs
│       ├── AlertFeedView.cs
│       └── FulfillmentPipelineView.cs
│
└── Backoffice.Api/                      # API project
    ├── Program.cs                       # Wolverine + Marten + SignalR + DI
    ├── appsettings.json                 # Connection strings
    ├── Properties/launchSettings.json   # Port 5243
    ├── Queries/                         # HTTP endpoints
    │   ├── GetCustomerServiceView.cs
    │   ├── GetOrderDetails.cs
    │   ├── GetReturnDetails.cs
    │   ├── GetDashboardMetrics.cs
    │   └── GetAlertFeed.cs
    ├── Commands/                        # HTTP endpoints
    │   ├── CancelOrder.cs
    │   ├── ApproveReturn.cs
    │   ├── DenyReturn.cs
    │   └── AddOrderNote.cs
    ├── Clients/                         # HTTP client implementations
    │   ├── OrdersClient.cs
    │   ├── ReturnsClient.cs
    │   └── CustomerIdentityClient.cs
    └── BackofficeHub.cs                 # SignalR hub
```

---

## Implementation Sessions

### Session 1: Project Scaffolding & Infrastructure

**Goal:** Set up Backoffice projects, Marten, Wolverine, multi-issuer JWT, authorization policies

**Tasks:**
1. Create `src/Backoffice/Backoffice/Backoffice.csproj` (regular SDK)
2. Create `src/Backoffice/Backoffice.Api/Backoffice.Api.csproj` (Web SDK)
3. Add projects to `CritterSupply.sln` and `CritterSupply.slnx`
4. Configure `Backoffice.Api/appsettings.json` (Postgres connection string)
5. Configure `Backoffice.Api/Properties/launchSettings.json` (port 5243)
6. Wire Marten in `Program.cs` (`backoffice` schema)
7. Wire Wolverine in `Program.cs` (include both assemblies in discovery)
8. Configure multi-issuer JWT (accept BackofficeIdentity tokens)
9. Add authorization policies (CustomerService, WarehouseClerk, OperationsManager, Executive, SystemAdmin)
10. Add Docker Compose service for Backoffice.Api
11. Verify build: `dotnet build "src/Backoffice/Backoffice.Api/Backoffice.Api.csproj"`

**Deliverables:**
- 2 projects created and building
- Marten + Wolverine + JWT configured
- Docker Compose service added

**Estimated Time:** 2-3 hours

---

### Session 2: HTTP Client Abstractions

**Goal:** Define typed HTTP client interfaces and implementations for domain BC integration

**Tasks:**
1. Define client interfaces in `Backoffice/Clients/`:
   - `IOrdersClient.cs` (GetOrders, GetOrderDetail, CancelOrder, GetReturnableItems)
   - `IReturnsClient.cs` (GetReturns, GetReturnDetail, ApproveReturn, DenyReturn)
   - `ICustomerIdentityClient.cs` (GetCustomerByEmail, GetCustomer, GetCustomerAddresses)
   - `ICorrespondenceClient.cs` (GetMessagesForCustomer, GetMessageDetail)
   - `IInventoryClient.cs` (GetStockLevel, GetLowStock)
   - `IFulfillmentClient.cs` (GetShipmentsForOrder)
2. Implement clients in `Backoffice.Api/Clients/`
3. Register typed HttpClient in `Program.cs` DI
4. Add stub implementations for integration testing
5. Create unit tests for client request/response mapping

**Deliverables:**
- 6 client interfaces + implementations
- DI registration complete
- Stub clients for testing

**Estimated Time:** 2-3 hours

---

### Session 3: Customer Service Workflows (Part 1: Search & Orders)

**Goal:** Implement customer search and order management for CS agents

**Tasks:**
1. Create composition types in `Backoffice/Composition/`:
   - `CustomerServiceView.cs` (customer info + order list)
   - `OrderDetailView.cs` (order + saga state + returnable items)
2. Implement queries in `Backoffice.Api/Queries/`:
   - `GetCustomerServiceView.cs` — `GET /api/backoffice/customers?email={email}`
   - `GetOrderDetails.cs` — `GET /api/backoffice/orders/{orderId}`
3. Implement commands in `Backoffice.Api/Commands/`:
   - `CancelOrder.cs` — `POST /api/backoffice/orders/{orderId}/cancel`
4. Extract `adminUserId` from JWT claims for audit trails
5. Add `[Authorize(Policy = "CustomerService")]` to all endpoints
6. Create Alba integration tests for CS workflows

**Deliverables:**
- 3 HTTP endpoints (1 query, 1 detail, 1 command)
- JWT claims extraction for admin attribution
- Integration tests passing

**Estimated Time:** 2-3 hours

---

### Session 4: Customer Service Workflows (Part 2: Returns & Correspondence)

**Goal:** Implement return management and correspondence history for CS agents

**Tasks:**
1. Create composition types:
   - `ReturnDetailView.cs` (return + lifecycle state + inspection results)
   - `CorrespondenceHistoryView.cs` (message list with delivery status)
2. Implement queries:
   - `GetReturnDetails.cs` — `GET /api/backoffice/returns/{returnId}`
   - `GetCorrespondenceHistory.cs` — `GET /api/backoffice/customers/{customerId}/messages`
3. Implement commands:
   - `ApproveReturn.cs` — `POST /api/backoffice/returns/{returnId}/approve`
   - `DenyReturn.cs` — `POST /api/backoffice/returns/{returnId}/deny`
4. Add `[Authorize(Policy = "CustomerService")]` to all endpoints
5. Create Alba integration tests

**Deliverables:**
- 4 HTTP endpoints (2 queries, 2 commands)
- Return approval/denial workflows complete
- Integration tests passing

**Estimated Time:** 2-3 hours

---

### Session 5: OrderNote Aggregate (CS Internal Comments)

**Goal:** Implement BFF-owned OrderNote aggregate for CS internal comments

**Tasks:**
1. Define `OrderNote` aggregate in `Backoffice/OrderNote/`:
   - `OrderNote.cs` (document model with orderId, noteId, adminUserId, content, timestamp)
   - `AddOrderNote.cs` (command + handler)
   - `GetOrderNotes.cs` (query + handler)
2. Configure Marten document store for `OrderNote`
3. Implement commands:
   - `AddOrderNote.cs` — `POST /api/backoffice/orders/{orderId}/notes`
4. Implement queries:
   - `GetOrderNotes.cs` — `GET /api/backoffice/orders/{orderId}/notes`
5. Add `[Authorize(Policy = "CustomerService")]` to endpoints
6. Create integration tests

**Deliverables:**
- OrderNote aggregate implemented
- 2 HTTP endpoints (1 command, 1 query)
- Integration tests passing

**Estimated Time:** 1-2 hours

---

### Session 6: BFF Projections (AdminDailyMetrics)

**Goal:** Implement BFF-owned Marten projections for executive dashboard KPIs

**Tasks:**
1. Define `AdminDailyMetrics` projection in `Backoffice/Projections/`:
   - Document schema (date, orderCount, revenue, paymentFailureCount, etc.)
   - Multi-stream projection consuming `OrderPlaced`, `OrderCancelled`, `PaymentCaptured`, `PaymentFailed`
2. Configure projection in `Program.cs` (inline lifecycle)
3. Create integration message handlers in `Backoffice/Notifications/`:
   - `OrderPlacedAdminHandler.cs` (increments orderCount, updates revenue)
   - `PaymentFailedAdminHandler.cs` (increments failureCount)
4. Implement query:
   - `GetDashboardMetrics.cs` — `GET /api/backoffice/dashboard/metrics`
5. Add `[Authorize(Policy = "Executive")]` to endpoint
6. Create integration tests with RabbitMQ message injection

**Deliverables:**
- AdminDailyMetrics projection implemented
- 4 integration message handlers
- 1 HTTP query endpoint
- Integration tests passing

**Estimated Time:** 2-3 hours

---

### Session 7: BFF Projections (AlertFeedView)

**Goal:** Implement operations alert feed with real-time updates

**Tasks:**
1. Define `AlertFeedView` projection in `Backoffice/Projections/`:
   - Document schema (alertId, alertType, severity, timestamp, orderId?, message)
   - Multi-stream projection consuming payment failures, low stock, delivery failures, return expirations
2. Create integration message handlers:
   - `PaymentFailedAdminHandler.cs` (creates alert)
   - `LowStockAdminHandler.cs` (creates alert)
   - `ShipmentDeliveryFailedAdminHandler.cs` (creates alert)
   - `ReturnExpiredAdminHandler.cs` (creates alert)
3. Implement query:
   - `GetAlertFeed.cs` — `GET /api/backoffice/alerts?severity={severity}&limit={n}`
4. Add `[Authorize(Policy = "OperationsManager")]` to endpoint
5. Create integration tests

**Deliverables:**
- AlertFeedView projection implemented
- 4 integration message handlers
- 1 HTTP query endpoint
- Integration tests passing

**Estimated Time:** 2-3 hours

---

### Session 8: SignalR Hub (Real-Time Push)

**Goal:** Configure SignalR hub for real-time admin updates

**Tasks:**
1. Define marker interface in `Backoffice/RealTime/IBackofficeWebSocketMessage.cs`
2. Define discriminated union in `Backoffice/RealTime/BackofficeEvent.cs`:
   - `LiveMetricUpdated` (orderCount, revenue, paymentFailureRate)
   - `AlertCreated` (alertType, severity, message)
3. Create `BackofficeHub.cs` in `Backoffice.Api/`
4. Configure SignalR in `Program.cs`:
   - `builder.Services.AddSignalR();`
   - `builder.Host.UseWolverine(opts => opts.UseSignalR());`
   - `app.MapHub<BackofficeHub>("/hub/backoffice");`
5. Update integration message handlers to publish SignalR events:
   - `OrderPlacedAdminHandler` → publish `LiveMetricUpdated` to `role:executive`
   - `PaymentFailedAdminHandler` → publish `AlertCreated` to `role:operations`
6. Create SignalR Client transport integration tests

**Deliverables:**
- SignalR hub configured and mapped
- Real-time event publishing working
- Integration tests with SignalR Client transport

**Estimated Time:** 2-3 hours

---

### Session 9: Warehouse Clerk Dashboard

**Goal:** Implement inventory visibility and alert acknowledgment for warehouse clerks

**Tasks:**
1. Implement queries:
   - `GetStockLevel.cs` — `GET /api/backoffice/inventory/{sku}`
   - `GetLowStockAlerts.cs` — `GET /api/backoffice/inventory/low-stock`
2. Define `AlertAcknowledgment` aggregate in `Backoffice/`:
   - Document model (alertId, adminUserId, acknowledgedAt)
   - `AcknowledgeAlert.cs` (command + handler)
3. Implement command:
   - `AcknowledgeAlert.cs` — `POST /api/backoffice/alerts/{alertId}/acknowledge`
4. Add `[Authorize(Policy = "WarehouseClerk")]` to endpoints
5. Create integration tests

**Deliverables:**
- 3 HTTP endpoints (2 queries, 1 command)
- AlertAcknowledgment aggregate implemented
- Integration tests passing

**Estimated Time:** 2-3 hours

---

### Session 10: Integration Testing & CI

**Goal:** Comprehensive integration testing with Alba + TestContainers

**Tasks:**
1. Create `tests/Backoffice/Backoffice.IntegrationTests/BackofficeTestFixture.cs`
2. Configure TestContainers for Postgres + RabbitMQ
3. Add test projects to `CritterSupply.sln` and `CritterSupply.slnx`
4. Implement multi-BC composition tests:
   - Customer search → order lookup → return approval workflow
   - Dashboard metrics update on OrderPlaced event
   - Alert feed update on PaymentFailed event
5. Implement SignalR hub authorization tests
6. Verify CI workflow compatibility (dotnet.yml integration-tests job)
7. Run full test suite: `dotnet test`

**Deliverables:**
- Integration test project created
- 10+ integration tests covering all workflows
- All tests passing in CI

**Estimated Time:** 3-4 hours

---

### Session 11: Documentation & Retrospective

**Goal:** Document learnings, update planning docs, create retrospective

**Tasks:**
1. Update `CURRENT-CYCLE.md`:
   - Move M31.5 to Recent Completions
   - Add M32.0 to Active Milestone
2. Update `backoffice-integration-gap-register.md`:
   - Mark Phase 1 endpoints as integrated
3. Create `docs/planning/milestones/m32-0-retrospective.md`
4. Update `CONTEXTS.md`:
   - Add Backoffice BC entry
   - Document integration patterns
5. Create skills doc: `docs/skills/backoffice-bff-patterns.md` (composition, projections, SignalR)
6. Commit final changes via `report_progress`

**Deliverables:**
- All documentation updated
- Retrospective completed
- M32.0 milestone closed

**Estimated Time:** 2-3 hours

---

## Success Criteria

### Functional Requirements

**Must Have (P0):**
- ✅ CS agent can search customer by email and view order history
- ✅ CS agent can view order details and cancel orders with reason
- ✅ CS agent can view return details and approve/deny returns
- ✅ CS agent can view correspondence history for a customer
- ✅ CS agent can add internal notes to orders
- ✅ Executive can view daily dashboard metrics (order count, revenue, AOV, payment failure rate)
- ✅ Operations manager can view real-time alert feed
- ✅ Warehouse clerk can view stock levels and low-stock alerts
- ✅ Warehouse clerk can acknowledge alerts
- ✅ All endpoints protected with role-based authorization

**Should Have (P1):**
- ✅ Real-time SignalR updates for dashboard metrics
- ✅ Real-time SignalR alerts for operations
- ✅ Week-over-week comparison for executive KPIs
- ✅ Alert severity tagging (⚠️ warning, 🔴 critical)

**Could Have (P2 - Deferred):**
- Dashboard metric charts/visualization (Phase 2 with Blazor frontend)
- CSV export functionality (Phase 3)
- Full audit log viewer (Phase 3)

### Technical Requirements

**Must Have:**
- ✅ BFF pattern (not API gateway)
- ✅ Multi-issuer JWT (accept BackofficeIdentity tokens)
- ✅ Authorization policies aligned with ADR 0031
- ✅ Marten inline projections for dashboard metrics
- ✅ SignalR hub with role-based groups
- ✅ Typed HttpClient abstractions for domain BC integration
- ✅ BFF-owned aggregates (OrderNote, AlertAcknowledgment)
- ✅ Integration tests with Alba + TestContainers
- ✅ Zero build errors, zero warnings (or documented as acceptable)

### Exit Gate

**M32.0 Phase 1 is complete when:**
1. All 10+ implementation sessions delivered
2. All P0 functional requirements met
3. All P0 technical requirements met
4. Integration test suite passes (>90% coverage)
5. Documentation updated (CURRENT-CYCLE, CONTEXTS, retrospective)
6. CI workflow passes (dotnet.yml integration-tests job)
7. Commit via `report_progress` and close milestone

---

## Risks & Mitigations

### Risk 1: RabbitMQ Event Subscription Complexity

**Likelihood:** Medium
**Impact:** High
**Description:** Subscribing to 14+ integration messages from 7 domain BCs may introduce queue wiring bugs similar to Cycle 26 Fulfillment incident.

**Mitigation:**
- Use explicit queue names: `backoffice-orders-events`, `backoffice-payments-events`, etc.
- Test RabbitMQ subscriptions independently before projection logic
- Use Alba integration tests with RabbitMQ message injection to verify handlers
- Document queue wiring patterns in retrospective for future BCs

### Risk 2: BFF-Owned Projection Performance

**Likelihood:** Low
**Impact:** Medium
**Description:** BFF projections consuming events from 7 domain BCs may lag if event volume is high.

**Mitigation:**
- Use Marten inline projections (zero lag) for Phase 1
- Monitor projection performance via Aspire dashboard
- If lag occurs, move to async projections in Phase 2

### Risk 3: Multi-BC Composition Complexity

**Likelihood:** Medium
**Impact:** Medium
**Description:** Customer service workflows compose data from 5+ domain BCs (Customer Identity → Orders → Returns → Correspondence → Fulfillment). Nested HTTP calls may cause latency or cascading failures.

**Mitigation:**
- Use parallel HTTP calls where possible (fan-out queries)
- Implement circuit breaker pattern (Polly) if needed
- Cache stable data (customer info, product names) in BFF projections
- Document composition patterns in skills doc

### Risk 4: OrderNote Aggregate Boundary Confusion

**Likelihood:** Low
**Impact:** Medium
**Description:** Developers may incorrectly place OrderNote in Orders BC instead of Backoffice BC.

**Mitigation:**
- Write ADR 0037 clearly explaining boundary rationale
- Document OrderNote as "operational tooling metadata, not order lifecycle"
- Code review enforcement during implementation

### Risk 5: Phase 2 Endpoint Gaps

**Likelihood:** Medium
**Impact:** Low
**Description:** 9 endpoint gaps remain for Phase 2 (Pricing write, Inventory write, Product Catalog admin write, Payments order query). If not addressed before Phase 2, delays will occur.

**Mitigation:**
- Document Phase 2 blockers clearly in this plan
- Create GitHub Issues for each gap with `phase:2-prep` label
- Estimate 4-5 sessions for gap closure
- Schedule Phase 2 prep work before starting Phase 2 implementation

---

## References

**Planning Documents:**
- [Backoffice Event Modeling (Revised)](../backoffice-event-modeling-revised.md)
- [Backoffice Integration Gap Register](../backoffice-integration-gap-register.md)
- [M32.0 Prerequisite Assessment](../m32-0-prerequisite-assessment.md)
- [M31.5 Session 5 Retrospective](./m31-5-session-5-retrospective.md)

**ADRs:**
- [ADR 0031: Admin Portal RBAC Model](../../decisions/0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](../../decisions/0032-multi-issuer-jwt-strategy.md)
- [ADR 0033: Admin Portal to Backoffice Rename](../../decisions/0033-admin-portal-to-backoffice-rename.md)
- [ADR 0034: Backoffice BFF Architecture](../../decisions/0034-backoffice-bff-architecture.md) — **to be written**
- [ADR 0035: Backoffice SignalR Hub Design](../../decisions/0035-backoffice-signalr-hub-design.md) — **to be written**
- [ADR 0036: BFF-Owned Projections Strategy](../../decisions/0036-bff-projections-strategy.md) — **to be written**
- [ADR 0037: OrderNote Aggregate Ownership](../../decisions/0037-ordernote-aggregate-ownership.md) — **to be written**

**Skills:**
- [Wolverine Message Handlers](../../skills/wolverine-message-handlers.md)
- [Marten Event Sourcing](../../skills/marten-event-sourcing.md)
- [Integration Messaging](../../skills/integration-messaging.md)
- [Wolverine SignalR](../../skills/wolverine-signalr.md)
- [BFF Real-Time Patterns](../../skills/bff-realtime-patterns.md)
- [CritterStack Testing Patterns](../../skills/critterstack-testing-patterns.md)

**Examples:**
- Customer Experience BFF: `src/Customer Experience/Storefront.Api/`
- Vendor Portal BFF: `src/Vendor Portal/VendorPortal.Api/`
- Orders BC: `src/Orders/Orders.Api/`
- Returns BC: `src/Returns/Returns.Api/`

---

*Plan created: 2026-03-16*
*Status: Active — Session 1 ready to begin*
