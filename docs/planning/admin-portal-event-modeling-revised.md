# Admin Portal: Revised Event Model & Design

**Date:** 2026-03-14 (Re-modeling session)
**Last Updated:** 2026-03-14 (Post-Cycle 29 Phase 1 reconciliation)
**Original Document:** [admin-portal-event-modeling.md](admin-portal-event-modeling.md) (2026-03-07)
**Participants:** Principal Software Architect, Product Owner, UX Engineer
**Status:** 🟡 Revised Model — Awaiting Owner Sign-Off on Open Questions (Phase 0 complete)
**Input Documents:** Original event model, [Research & Discovery](admin-portal-research-discovery.md), [UX Research](admin-portal-ux-research.md), Cycle 21-28 retrospectives, codebase audit, [Cycle 29 Phase 1 Retrospective](cycles/cycle-29-admin-identity-phase-1-retrospective.md)
**Companion Documents:** [Integration Gap Register](admin-portal-integration-gap-register.md), [Decision Log](admin-portal-revised-decision-log.md), [Open Questions](admin-portal-open-questions.md)

> **Revision Note:** This document replaces the original `admin-portal-event-modeling.md` as the current-state event model. The original is preserved as-is for historical reference. All changes are marked with `[REVISED]` tags and rationale. The companion [Decision Log](admin-portal-revised-decision-log.md) records what was kept, changed, or removed from the original.
>
> **Post-PR #375 Update:** Admin Identity BC (Phase 0) was implemented in Cycle 29 Phase 1 and merged to main. This document has been updated to reflect Phase 0 as complete, reconcile assumptions against the actual implementation (password hashing, user provisioning, JWT claims), and update remaining phase blockers.

---

## Table of Contents

1. [Core Architecture Decision](#core-architecture-decision)
2. [What Changed Since the Original Model](#what-changed-since-the-original-model)
3. [Internal User Personas & Roles](#internal-user-personas--roles)
4. [Technology Decisions](#technology-decisions)
5. [Admin Identity: Authentication Prerequisite](#admin-identity-authentication-prerequisite)
6. [Revised Phased Roadmap](#revised-phased-roadmap)
7. [Event Modeling: Key Flows (Revised)](#event-modeling-key-flows-revised)
8. [Commands & Queries — Reconciled Against Codebase](#commands--queries--reconciled-against-codebase)
9. [Views & Projections — Reconciled](#views--projections--reconciled)
10. [Aggregates & Boundaries](#aggregates--boundaries)
11. [Integration Points — Full Audit](#integration-points--full-audit)
12. [Sagas — Reassessment](#sagas--reassessment)
13. [Naming Audit](#naming-audit)
14. [Security Considerations](#security-considerations)
15. [Risks (Revised)](#risks-revised)
16. [Dependencies on Other BCs (Revised)](#dependencies-on-other-bcs-revised)

---

## Core Architecture Decision

**Unchanged from original:** Admin Portal is a **BFF (Backend-for-Frontend)**, not a pure API gateway. It follows the same pattern as Customer Experience (for customers) and Vendor Portal (for vendor partners).

```
Customer Experience BC → Storefront.Api (BFF for customers)
Vendor Portal BC       → VendorPortal.Api (BFF for vendor partners)
Admin Portal BC        → AdminPortal.Api (BFF for internal employees)  ← this document
```

The Admin Portal BFF:
- **DOES:** Authenticate, authorize (RBAC), compose multi-BC queries, transform responses, subscribe to events via RabbitMQ, push via SignalR, validate input, translate errors
- **DOES NOT:** Execute business logic, store transactional data, proxy raw requests

---

## What Changed Since the Original Model

> **Critical context:** CONTEXTS.md is reliable for descriptions and architectural intent but NOT for exact events, commands, queries, or integration messages. The codebase (especially `src/Shared/Messages.Contracts/` and BC endpoint handlers) is the source of truth for actual contracts. All integration surface claims in this document have been verified against the codebase as of Cycle 28 completion.

### BCs Now Implemented (Since 2026-03-07)

| BC | Cycles | Key Impact on Admin Portal |
|----|--------|---------------------------|
| **Admin Identity** | 29 (Phase 1) | ✅ **Phase 0 prerequisite COMPLETE.** EF Core + JWT auth + 7 admin roles + 7 API endpoints (login, refresh, logout, CRUD). Port 5249. ADR 0031 accepted. Policy-based authorization with SystemAdmin superuser pattern. PBKDF2-SHA256 password hashing. Direct user creation (not invitation flow). |
| **Returns** | 25-27 | 14 integration messages, 10 endpoints, 10 lifecycle states. CS return management is now feasible — endpoints exist for approve, deny, receive, inspect, exchange. **Original model had Returns as Phase 2 read-only; revised to Phase 1 read+write.** |
| **Correspondence** | 28 | Renamed from "Notifications" (ADR 0030). Email delivery tracking. 2 query endpoints exist. **Not in original model; added to Phase 1 CS view.** |
| **Pricing** | 21 | PriceRule event-sourced aggregate, BulkPricingJob saga, CurrentPriceView projection. Only 2 GET endpoints exist today — **no admin write endpoints yet**. |
| **Vendor Portal** | 22-23 | Blazor WASM + JWT + SignalR pattern fully proven. 143 tests, 100% pass rate. Provides the template for Admin Portal frontend. |

### BCs Still Missing (Impact on Original Model)

| BC | Original Phase | Revised Status |
|----|---------------|----------------|
| **Analytics** | Phase 1 hard dependency | **REMOVED.** Does not exist, 🟢 Low Priority. Dashboard KPIs sourced from BFF-owned projections instead. |
| **Store Credit** | Phase 3 hard dependency | **DEFERRED to Phase 4+.** Does not exist, no timeline. Order notes as manual workaround. |
| **Promotions** | Not in original model | Shipping Cycle 29. **Added:** Phase 2 (read-only), Phase 3 (write). |
| **Search** | Not in original model | Does not exist. Not a Phase 1-3 dependency. |

### Collisions Fixed

| Collision | Original | Fixed |
|-----------|----------|-------|
| **Port 5245** | AdminIdentity.Api | **5249** — Returns.Api owns 5245 |
| **ADR 0026-0029** | Reserved for Admin Portal | **0031-0034** — 0026-0030 taken by other decisions |
| **Frontend recommendation** | React/Next.js (event model) vs Blazor WASM (research doc) | **Blazor WASM** — research doc override is authoritative; consistent with Vendor Portal |
| **"Notifications BC"** | Referenced in original | **Correspondence BC** — renamed per ADR 0030 |

---

## Internal User Personas & Roles

**[REVISED]** Permission matrix updated with Returns and Correspondence visibility.

| Capability | CopyWriter | PricingMgr | WarehouseClerk | CustomerSvc | OpsMgr | Executive | SysAdmin |
|---|---|---|---|---|---|---|---|
| Edit product description | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Edit display name | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Publish / unpublish product | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| Set base price | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Schedule price change | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Cancel scheduled price change | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Adjust inventory quantity | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| Receive inbound stock | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| Acknowledge low-stock alert | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Customer search (email) | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| View order details | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Cancel order | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| **[NEW] Approve/deny return** | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| **[NEW] Approve/deny exchange** | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| **[NEW] Receive return (warehouse)** | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **[NEW] Submit inspection** | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **[NEW] View correspondence history** | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| **[NEW] View return details** | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Issue store credit | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| View executive dashboard | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| Export reports (CSV/Excel) | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| View live operations alerts | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| **[NEW] View promotions (read-only)** | ❌ | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ |
| Manage admin user accounts | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |

> **PO Decision (confirmed):** Store credit issuance is removed from ALL phases — blocked on Store Credit BC which has no timeline. Manual tracking via order notes is the interim workflow.

> **PO Decision (confirmed):** PricingManager owns promotions management (no separate PromotionsManager role until team exceeds 50 admin users).

---

## Technology Decisions

| Aspect | Decision | Change from Original? |
|---|---|---|
| Backend persistence | Marten (BFF-owned projections for dashboard metrics) | **[REVISED]** No Analytics BC dependency |
| Auth mechanism | JWT Bearer (AdminIdentity BC, issuer `https://localhost:5249`) | **[UPDATED]** Phase 1: self-referential audience; Phase 2+ needs Admin Portal API audience |
| Real-time | SignalR via `opts.UseSignalR()` | Unchanged |
| **Frontend** | **Blazor WASM** | **[REVISED]** Changed from React/Next.js. Research doc override (consistency with Vendor Portal pattern). |
| SignalR client | `Microsoft.AspNetCore.SignalR.Client` (native .NET) | **[REVISED]** Follows from Blazor WASM decision |
| Hub groups | Role-scoped: `role:{roleName}` + `admin-user:{userId}` | Unchanged |
| Admin Portal.Api port | **5243** | Unchanged |
| Admin Portal.Web port | **5244** | Unchanged |
| **Admin Identity.Api port** | **5249** | **[REVISED]** Was 5245 — collision with Returns.Api |
| Schema | `adminportal` | Unchanged |

> **Counter-proposal (from research doc):** If the team wants a React reference, the **Operations Dashboard BC** (developer-facing tool with heavy chart/visualization needs) is a better candidate than the Admin Portal.

---

## Admin Identity: Authentication Prerequisite

> **Status: ✅ IMPLEMENTED** — Delivered in Cycle 29 Phase 1 (PR #375). See [Cycle 29 Phase 1 Retrospective](cycles/cycle-29-admin-identity-phase-1-retrospective.md) and [ADR 0031](../decisions/0031-admin-portal-rbac-model.md).

- EF Core + Postgres (`adminidentity` schema) — **implemented**
- Single-organization (no tenant concept, mirrors VendorIdentity) — **implemented**
- JWT: issuer `https://localhost:5249`, audience `https://localhost:5249` (self-referential in Phase 1; Phase 2+ will add Admin Portal API audience), 15-min access + 7-day refresh — **implemented**
- Password hashing: PBKDF2-SHA256 via ASP.NET Core Identity `PasswordHasher<T>` (100,000 iterations) — **implemented** (original research doc assumed Argon2id; ADR 0031 documents the decision to use PBKDF2 with Argon2id deferred to Phase 2+ if needed)
- User provisioning: Direct creation by SystemAdmin via `POST /api/admin-identity/users` — **implemented** (no invitation flow; resolves the discrepancy in the original research doc Appendix B)
- No seed data in migration — users are created via the CreateAdminUser endpoint at runtime
- **Port: 5249** — **implemented**
- **ADR: 0031** (Admin Portal RBAC Model) — **accepted**

**Implemented endpoints:**
```
POST   /api/admin-identity/auth/login       # Returns JWT access token + refresh cookie
POST   /api/admin-identity/auth/refresh     # Rotates refresh token, issues new access token
POST   /api/admin-identity/auth/logout      # Invalidates refresh token
GET    /api/admin-identity/users            # List admin users (SystemAdmin only)
POST   /api/admin-identity/users            # Create admin user (SystemAdmin only)
PUT    /api/admin-identity/users/{id}/role  # Change user role (SystemAdmin only)
DELETE /api/admin-identity/users/{id}       # Deactivate admin user (SystemAdmin only)
```

**Authorization policies (implemented in Program.cs per ADR 0031):**
- Leaf policies: `CopyWriter`, `PricingManager`, `WarehouseClerk`, `CustomerService`, `OperationsManager`, `Executive`, `SystemAdmin`
- Composite policies: `PricingManagerOrAbove`, `CustomerServiceOrAbove`, `WarehouseOrOperations`
- All policies include `SystemAdmin` (superuser pattern)

---

## Revised Phased Roadmap

### Phase 0: AdminIdentity BC (Prerequisite) — ✅ COMPLETE (Cycle 29 Phase 1)

> **Delivered:** PR #375, merged 2026-03-14. All deliverables shipped.

| Deliverable | Status |
|------------|--------|
| AdminIdentity project (EF Core, schema `adminidentity`) | ✅ Implemented |
| AdminIdentity.Api (JWT issuer, login/logout/refresh, port 5249) | ✅ Implemented |
| AdminRole enum (CopyWriter=1 through SystemAdmin=7) | ✅ Implemented |
| ADR 0031: Admin Portal RBAC Model | ✅ Accepted |
| Authorization policies (7 leaf + 3 composite) | ✅ Implemented |
| User management CRUD (SystemAdmin only) | ✅ Implemented |

**Deliverable gate:** ✅ `POST /api/admin-identity/auth/login` returns a valid JWT with `sub` (AdminUserId), `role` (AdminRole), `email`, and `name` claims.

**Not yet delivered (deferred):**
- Integration messages in Messages.Contracts (`AdminUserCreated`, `AdminUserDeactivated`, `AdminUserRoleChanged`) — not yet needed; to be added when Admin Portal BFF subscribes to these events
- Integration tests (Alba + TestContainers) — no test project created yet
- Seed data script — users created via CreateAdminUser endpoint

### Phase 0.5: Domain BC Endpoint Gaps (Prerequisite) — 1 cycle

> **[NEW PHASE]** Codebase audit revealed critical endpoint gaps in domain BCs that block Phase 1. These must ship before Admin Portal Phase 1 begins. This is the lesson from Cycle 26 (Fulfillment queue wiring bug discovered mid-cycle).

| Gap | Owning BC | Why It Blocks Phase 1 | Estimated Effort |
|-----|----------|----------------------|-----------------|
| `GET /api/customers?email={email}` — Customer search by email | Customer Identity | CS workflow starts with email lookup; only ID-based lookup exists today | < 1 session |
| `GET /api/inventory/{sku}` — Stock level query | Inventory | WarehouseClerk dashboard, low-stock KPI | 1 session |
| `GET /api/inventory/low-stock` — Low-stock alert summary | Inventory | WarehouseClerk alert feed; Ops low-stock KPI | 1 session |
| `GET /api/fulfillment/shipments?orderId={id}` — Shipment tracking for CS | Fulfillment | WISMO (35-40% of CS tickets) needs shipment status | < 1 session |
| Multi-issuer JWT setup in Orders.Api, Returns.Api, Customer Identity.Api | Multiple | Admin JWT tokens must be accepted by domain BCs | 1 session |

### Phase 1: Read-Only Dashboards + CS Tooling + Returns Workflow — 2-3 cycles

**[REVISED]** Major scope changes from original:
- **ADDED:** Returns read+write (approve/deny) — was Phase 2 read-only
- **ADDED:** Correspondence history view for CS
- **ADDED:** Order notes/internal comments (PO P0 requirement)
- **ADDED:** WarehouseClerk alert viewing (moved from Phase 2)
- **REMOVED:** Analytics BC dependency — replaced with BFF projections
- **ADDED:** Full customer order history (not just "recent 5")

| Capability | Role | BC | Endpoint Status | Priority |
|-----------|------|-----|----------------|----------|
| BFF skeleton + SignalR hub + RBAC policies | All | Self | N/A (creating) | P0 |
| Blazor WASM shell + role-based navigation | All | Self | N/A (creating) | P0 |
| Customer search by email | CS, Ops | Customer Identity | ⚠️ **GAP** — needs Phase 0.5 | P0 |
| Full customer order history | CS, Ops | Orders | ✅ `GET /api/orders?customerId=` | P0 |
| Order detail with saga timeline | CS, Ops | Orders | ✅ `GET /api/orders/{orderId}` | P0 |
| Order cancellation with reason | CS, Ops | Orders | ✅ `POST /api/orders/{orderId}/cancel` | P0 |
| **Return lookup by order** | CS, Ops | Returns | ✅ `GET /api/returns` (supports orderId filter) | P0 |
| **Return detail view** | CS, Ops | Returns | ✅ `GET /api/returns/{returnId}` | P0 |
| **Return approval/denial** | CS, Ops | Returns | ✅ `POST /api/returns/{id}/approve` + `/deny` | P0 |
| **Order notes/internal comments** | CS | Admin Portal (new aggregate) | N/A (creating) | P0 |
| **Correspondence history** | CS | Correspondence | ✅ `GET /api/correspondence/messages/customer/{id}` | P1 |
| Executive KPIs (7 metrics via BFF projections) | Exec, Ops | BFF ← RabbitMQ events | N/A (creating) | P1 |
| Operations alert feed (SignalR) | Ops, WH | BFF ← RabbitMQ events | N/A (creating) | P1 |
| WarehouseClerk alert viewing + acknowledgment | WH | Inventory | ⚠️ **GAP** — needs Phase 0.5 query endpoints | P1 |
| Ops read access to pricing history | Ops | Pricing | ✅ `GET /api/pricing/products/{sku}` | P1 |
| Ops read access to product content | Ops | Product Catalog | ✅ `GET /api/products/{sku}` | P1 |
| PII access logging (GDPR) | Compliance | Self | N/A (creating) | P0 |
| ADR 0032: Multi-Issuer JWT Strategy | — | — | — | P0 |
| ADR 0033: Blazor WASM for Admin Portal | — | — | — | P0 |
| ADR 0034: Admin Portal SignalR Hub Design | — | — | — | P0 |

**Executive Dashboard KPIs (Phase 1):**

| KPI | Source BC | BFF Projection Trigger | Comparison |
|-----|----------|----------------------|------------|
| Order Count (Today) | Orders | `OrderPlaced` | vs same day last week |
| Gross Revenue (Today) | Orders | `OrderPlaced` (TotalAmount) | vs same day last week |
| Average Order Value | Calculated | Orders projection | weekly trend |
| Payment Failure Rate | Payments | `PaymentFailed` / `PaymentCaptured` | ⚠️ >5%, 🔴 >10% |
| Fulfillment Pipeline | Orders | Order saga state events | real-time distribution |
| **Active Return Rate** | Returns | `ReturnRequested` / `ShipmentDelivered` | weekly trend |
| **Correspondence Delivery Rate** | Correspondence | `CorrespondenceDelivered` / `CorrespondenceQueued` | ⚠️ <95%, 🔴 <90% |

### Phase 2: Write Operations + Warehouse Returns + Promotions Read — 2 cycles

**[REVISED]** Added warehouse return operations, exchange workflows, promotions read-only.

| Capability | Role | BC | Endpoint Status | Priority |
|-----------|------|-----|----------------|----------|
| Product content editing | CopyWriter | Product Catalog | ⚠️ **GAP** — needs admin write endpoints | P1 |
| Price set + schedule + cancel | PricingMgr | Pricing | ⚠️ **GAP** — only 2 GET endpoints exist | P1 |
| Inventory adjust + receive stock | WH | Inventory | ⚠️ **GAP** — message-driven only, no HTTP | P1 |
| Receiving discrepancy notes | WH | Admin Portal | N/A (creating) | P1 |
| **Return receipt** | WH | Returns | ✅ `POST /api/returns/{id}/receive` | P1 |
| **Submit inspection** | WH | Returns | ✅ `POST /api/returns/{id}/inspection` | P1 |
| **Exchange approval/denial** | CS, Ops | Returns | ✅ `POST /api/returns/{id}/approve-exchange` + `/deny-exchange` | P1 |
| **Ship replacement item** | WH | Returns | ✅ `POST /api/returns/{id}/ship-replacement` | P1 |
| Floor price visibility | PricingMgr | Pricing | ⚠️ Needs FloorPriceSet data exposed | P1 |
| Escalation workflow (CS → Ops) | CS, Ops | Admin Portal (new) | N/A (creating) | P1 |
| Correspondence delivery monitoring | Ops | Correspondence | ✅ Existing endpoints + new projection | P2 |
| Promotions read-only view | PricingMgr, Ops | Promotions | TBD (Cycle 29 output) | P2 |
| Auth headers in Inventory.Api, Payments.Api | Infra | Multiple | N/A | P0 |
| Audit trail validation | Compliance | All BCs | N/A | P0 |

### Phase 3: Polish + Advanced Features + Promotions Write — 1-2 cycles

| Capability | Role | BC | Priority |
|-----------|------|-----|----------|
| Promotions management (create/deactivate) | PricingMgr | Promotions | P1 |
| SystemAdmin user management CRUD | SysAdmin | AdminIdentity | P1 |
| CSV/Excel report exports | Exec, Ops | BFF projections | P1 |
| Returns analytics dashboard | Ops, Exec | Returns + BFF projections | P2 |
| Audit log viewer | SysAdmin | BFF projection ← domain events | P2 |
| Bulk operations pattern | All write roles | All write BCs | P2 |
| Tab visibility API (ADR 0025 must-fix) | All | Admin Portal.Web | P1 |
| Session expiry modal (ADR 0025 must-fix) | All | Admin Portal.Web | P1 |
| Alert notification preferences | All | Admin Portal | P2 |

### Phase 4+: Future Work (Blocked or Low Priority)

| Capability | Blocking BC | Priority |
|-----------|------------|----------|
| Store credit issuance | Store Credit BC (no timeline) | P2 — becomes P0 if return policy changes |
| ChannelManager role | Listings BC (Cycles 30+) | P2 |
| Barcode scanning | None (hardware integration) | P3 |
| Corporate SSO integration | None (infra decision) | P2 — becomes P0 at 50+ admin users |
| Full Analytics BC integration | Analytics BC (no timeline) | P3 — BFF projections are sufficient |
| Conflict resolution UI | Product Catalog (event sourcing evolution) | P2 |

---

## Event Modeling: Key Flows (Revised)

### Flow 1: Copy Writer Updates Product Description

**Unchanged from original** — Product Catalog BC endpoints exist (`GET /api/products/{sku}`, `PUT /api/products/{sku}`). The admin-specific write endpoint (`PUT /api/products/{sku}/description`) does not exist yet and needs to be created in Phase 0.5 or Phase 2.

### Flow 2: Pricing Manager Schedules a Black Friday Sale

**[REVISED]** Pricing BC currently only has 2 GET endpoints (`/api/pricing/products/{sku}`, `/api/pricing/products`). The write endpoints assumed by the original model (schedule price change, cancel schedule) do **not exist**. These must be created as part of Phase 2 domain BC work.

Actual Pricing BC commands that exist in code (message handlers, not HTTP):
- `SetInitialPrice(Sku, Amount, Currency, FloorPrice?, CeilingPrice?)`
- `ChangePrice(Sku, NewAmount, Currency, Reason?, ChangedBy, ChangedAt)`

Phase 2 needs: HTTP endpoints that wrap these commands with admin JWT auth.

### Flow 3: Warehouse Clerk Receives Inbound Stock

**[REVISED]** Inventory BC has **zero HTTP endpoints**. It is entirely message-driven (RabbitMQ handlers). Commands exist in code:
- `ReserveStock`, `CommitReservation`, `ReleaseReservation` (saga-driven)
- `InitializeInventory`, `ReceiveStock` (admin-facing candidates)

Phase 0.5 must add at minimum: `GET /api/inventory/{sku}` for stock queries.
Phase 2 must add: `POST /api/inventory/{sku}/receive` and `POST /api/inventory/{sku}/adjust` for warehouse write operations.

### Flow 4: Real-Time Executive Dashboard Update

**[REVISED]** Source changed from Analytics BC to BFF-owned Marten projections.

```
Orders BC: OrderPlaced published to RabbitMQ
  └─> AdminPortal domain assembly — OrderPlacedAdminHandler (Wolverine handler)
      ├─> Update AdminDailyMetrics Marten document:
      │     TodayOrderCount++
      │     TodayEstimatedRevenue += order.TotalAmount
      └─> Wolverine SignalR transport:
          └─> Publish LiveMetricUpdated { ordersToday, estimatedRevenueToday }
              to hub groups: role:executive, role:operations
```

No Analytics BC dependency. The Admin Portal BFF subscribes directly to domain BC events via RabbitMQ and maintains its own lightweight projections.

### Flow 5: CS Agent Handles Return Request [NEW]

```
Trigger: Customer calls about a return

[Admin Portal Frontend — CustomerService role]
CS agent searches customer by email
  └─> GET /api/admin/customers?email={email} (AdminPortal.Api)
      └─> Customer Identity BC: GET /api/customers?email={email} [Phase 0.5 gap]
          └─> Return CustomerServiceView (id, email, name)

CS agent views customer's orders
  └─> GET /api/admin/customers/{customerId}/orders (AdminPortal.Api)
      └─> Orders BC: GET /api/orders?customerId={customerId}
          └─> Return order list with saga states

CS agent opens specific order, views returnable items
  └─> GET /api/admin/orders/{orderId}/returnable-items (AdminPortal.Api)
      └─> Orders BC: GET /api/orders/{orderId}/returnable-items
          └─> Return: items with delivery date, eligibility window, return status

CS agent views existing return (if any)
  └─> GET /api/admin/returns?orderId={orderId} (AdminPortal.Api)
      └─> Returns BC: GET /api/returns?orderId={orderId}

CS agent approves pending return
  └─> POST /api/admin/returns/{returnId}/approve (AdminPortal.Api)
      Auth: Bearer {jwt} (role: CustomerService)
      ├─> [Authorize(Policy = "CustomerServiceOrAbove")]
      ├─> Extract adminUserId from JWT claims
      └─> Returns BC: POST /api/returns/{returnId}/approve
          └─> ReturnApproved event
              ├─> Orders BC: saga tracks active return
              ├─> Fulfillment BC: expects return shipment
              ├─> Correspondence BC: sends return approval email
              └─> Admin Portal SignalR: alert to role:operations
```

### Flow 6: CS Agent Views Correspondence History [NEW]

```
Trigger: Customer asks "I never got my order confirmation"

[Admin Portal Frontend — CustomerService role]
CS agent is already viewing customer detail (from Flow 5)
CS agent clicks "Messages" tab
  └─> GET /api/admin/customers/{customerId}/messages (AdminPortal.Api)
      └─> Correspondence BC: GET /api/correspondence/messages/customer/{customerId}
          └─> Return: message list [{messageId, subject, channel, status, queuedAt, deliveredAt}]

CS agent sees order confirmation with status "DeliveryFailed"
  └─> GET /api/admin/messages/{messageId} (AdminPortal.Api)
      └─> Correspondence BC: GET /api/correspondence/messages/{messageId}
          └─> Return: full message detail with retry history and error messages

CS agent: "I see the email failed. Let me verify your email address..."
  └─> (Manual resolution — no automated retry from Admin Portal in Phase 1)
```

---

## Commands & Queries — Reconciled Against Codebase

### Commands the Admin Portal Will Issue (Verified Against Actual BC Endpoints)

| Command | Target BC | Endpoint | Exists Today? | Phase |
|---------|-----------|----------|--------------|-------|
| Search customer by email | Customer Identity | `GET /api/customers?email=` | ❌ **GAP** | 0.5 |
| Get customer by ID | Customer Identity | `GET /api/customers/{id}` | ✅ | 1 |
| Get customer addresses | Customer Identity | `GET /api/customers/{id}/addresses` | ✅ | 1 |
| List orders for customer | Orders | `GET /api/orders?customerId=` | ✅ | 1 |
| Get order detail | Orders | `GET /api/orders/{orderId}` | ✅ | 1 |
| Cancel order | Orders | `POST /api/orders/{orderId}/cancel` | ✅ | 1 |
| Get returnable items | Orders | `GET /api/orders/{orderId}/returnable-items` | ✅ | 1 |
| List returns for order | Returns | `GET /api/returns` (with orderId filter) | ✅ | 1 |
| Get return detail | Returns | `GET /api/returns/{returnId}` | ✅ | 1 |
| Approve return | Returns | `POST /api/returns/{id}/approve` | ✅ | 1 |
| Deny return | Returns | `POST /api/returns/{id}/deny` | ✅ | 1 |
| Get messages for customer | Correspondence | `GET /api/correspondence/messages/customer/{id}` | ✅ | 1 |
| Get message detail | Correspondence | `GET /api/correspondence/messages/{id}` | ✅ | 1 |
| Get product detail | Product Catalog | `GET /api/products/{sku}` | ✅ | 1 |
| List products | Product Catalog | `GET /api/products` | ✅ | 1 |
| Get current price | Pricing | `GET /api/pricing/products/{sku}` | ✅ | 1 |
| Get payment detail | Payments | `GET /api/payments/{paymentId}` | ✅ | 1 |
| Get stock level | Inventory | `GET /api/inventory/{sku}` | ❌ **GAP** | 0.5 |
| Get low-stock alerts | Inventory | `GET /api/inventory/low-stock` | ❌ **GAP** | 0.5 |
| Get shipment for order | Fulfillment | `GET /api/fulfillment/shipments?orderId=` | ❌ **GAP** | 0.5 |
| Update product description | Product Catalog | `PUT /api/products/{sku}/description` | ❌ **GAP** | 2 |
| Set/change price | Pricing | `PUT /api/pricing/products/{sku}/price` | ❌ **GAP** | 2 |
| Schedule price change | Pricing | `POST /api/pricing/products/{sku}/price/schedule` | ❌ **GAP** | 2 |
| Cancel scheduled price | Pricing | `DELETE /api/pricing/products/{sku}/price/schedule/{id}` | ❌ **GAP** | 2 |
| Adjust inventory | Inventory | `POST /api/inventory/{sku}/adjust` | ❌ **GAP** | 2 |
| Receive stock | Inventory | `POST /api/inventory/{sku}/receive` | ❌ **GAP** | 2 |
| Receive return (warehouse) | Returns | `POST /api/returns/{id}/receive` | ✅ | 2 |
| Submit inspection | Returns | `POST /api/returns/{id}/inspection` | ✅ | 2 |
| Approve/deny exchange | Returns | `POST /api/returns/{id}/approve-exchange` | ✅ | 2 |
| Ship replacement item | Returns | `POST /api/returns/{id}/ship-replacement` | ✅ | 2 |

### Summary: 16 of 28 commands have existing endpoints. 12 require new endpoint creation.

---

## Views & Projections — Reconciled

### BFF-Owned Marten Projections (AdminPortal domain project)

| Projection | Trigger Events | Purpose | Phase |
|-----------|---------------|---------|-------|
| `AdminDailyMetrics` | `OrderPlaced`, `OrderCancelled`, `PaymentCaptured`, `PaymentFailed` | Executive KPIs (revenue, order count, AOV, payment failure rate) | 1 |
| `FulfillmentPipelineView` | Order saga state change events | Fulfillment pipeline distribution chart | 1 |
| `ReturnMetricsView` | `ReturnRequested`, `ReturnCompleted`, `ReturnExpired` | Active return rate KPI | 1 |
| `CorrespondenceMetricsView` | `CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed` | Delivery success rate KPI | 1 |
| `AlertFeedView` | `PaymentFailed`, `LowStockDetected`, `ShipmentDeliveryFailed`, `ReturnExpired` | Operations alert feed (with severity, timestamps) | 1 |
| `PendingInspectionsView` | `ReturnReceived`, `InspectionStarted`, `InspectionPassed/Failed` | Warehouse: pending inspection queue | 2 |

### Projections Available in Domain BCs (Already Built)

| Projection | Owning BC | Endpoint | Used By Admin Portal |
|-----------|-----------|----------|---------------------|
| `CurrentPriceView` | Pricing | `GET /api/pricing/products/{sku}` | Yes — PricingManager dashboard |
| `MessageListView` | Correspondence | `GET /api/correspondence/messages/customer/{id}` | Yes — CS correspondence tab |
| Order saga state | Orders | `GET /api/orders/{orderId}` | Yes — CS order detail |
| Return snapshot | Returns | `GET /api/returns/{returnId}` | Yes — CS return detail |

### Projections Not Yet Built (Gaps)

| Projection | Owning BC | Needed For | Phase |
|-----------|-----------|-----------|-------|
| Inventory stock level query | Inventory | WH dashboard, low-stock KPI | 0.5 |
| Shipment tracking query | Fulfillment | CS WISMO response | 0.5 |
| Product content list (admin view) | Product Catalog | CopyWriter product search | 1 (existing `GET /api/products` may suffice) |

---

## Aggregates & Boundaries

### Admin Portal's Own Aggregates

| Aggregate | Storage | Purpose | Phase |
|-----------|---------|---------|-------|
| **OrderNote** (new) | Marten document store | CS internal comments on orders. Keyed by `{orderId}:{noteId}`. Not part of the Orders BC — the Admin Portal owns this context. | 1 |
| **AdminDailyMetrics** | Marten document | BFF-owned projection for dashboard KPIs | 1 |
| **AlertAcknowledgment** | Marten document | Tracks which alerts have been acknowledged by which admin user | 1 |
| **EscalationTicket** (new) | Marten document | CS escalation to Ops. Status: Open → Acknowledged → Resolved. | 2 |

> **Boundary stress-test:** OrderNote intentionally lives in Admin Portal, NOT in Orders BC. Rationale: internal CS notes are operational tooling metadata, not part of the order's business lifecycle. Orders BC events should not be polluted with `OrderNoteAdded` — that's an Admin Portal concern. If a future "Notes" aggregate is needed in Orders BC for customer-visible notes, that's a separate aggregate with different semantics.

### Aggregates in Domain BCs Used by Admin Portal (Verified)

| Aggregate | BC | Type | Admin Portal Interaction |
|-----------|-----|------|------------------------|
| Order (saga) | Orders | Event-sourced saga | Read state, cancel command |
| Checkout | Orders | Event-sourced | Not directly — Admin Portal sees completed orders |
| Cart | Shopping | Event-sourced | Not directly — Admin Portal doesn't manage carts |
| Return | Returns | Event-sourced | Read state, approve/deny/receive/inspect commands |
| ProductInventory | Inventory | Event-sourced | Read stock levels (Phase 0.5), adjust/receive (Phase 2) |
| Payment | Payments | Event-sourced saga | Read payment status |
| Product | Product Catalog | Marten document | Read and update (Phase 2) |
| ProductPrice (PriceRule) | Pricing | Event-sourced | Read current price, set/schedule (Phase 2) |
| Message | Correspondence | Event-sourced | Read delivery status |
| Customer | Customer Identity | EF Core entity | Read customer info for CS |

---

## Integration Points — Full Audit

### Integration Point Status by BC

| BC | Strategy | Phase 1 | Phase 2 | Status |
|----|----------|---------|---------|--------|
| **Admin Identity** | JWT issuer + user management | Login, token refresh, user CRUD | — | ✅ **COMPLETE** (Cycle 29 Phase 1) |
| **Customer Identity** | HTTP queries (typed client) | Customer lookup, address display | — | ⚠️ Missing email search endpoint |
| **Orders** | HTTP queries + commands | Order list, detail, cancel, returnable items | — | ✅ Fully defined |
| **Returns** | HTTP queries + commands | Return list, detail, approve, deny | Receive, inspect, exchange approve/deny, ship replacement | ✅ Fully defined |
| **Payments** | HTTP queries | Payment detail | — | ✅ Read endpoint exists |
| **Inventory** | RabbitMQ events → SignalR; HTTP queries (Phase 0.5) | Alert feed, low-stock KPI | Stock adjust, receive, acknowledge | ⚠️ Zero HTTP endpoints today |
| **Fulfillment** | HTTP queries | Shipment tracking for CS | — | ⚠️ Missing shipment query endpoint |
| **Product Catalog** | HTTP queries + commands | Product list, detail (read) | Content editing (write) | ⚠️ No admin write endpoints |
| **Pricing** | HTTP queries + commands | Price history (read) | Set, schedule, cancel price changes | ⚠️ Only 2 GET endpoints |
| **Correspondence** | HTTP queries | Message history for CS | Delivery monitoring (Ops) | ✅ Query endpoints exist |
| **Promotions** | HTTP queries | — | Read-only view | 🟡 BC shipping Cycle 29 — endpoints TBD |
| **Analytics** | None | — | — | ❌ **REMOVED** — BFF projections replace |
| **Store Credit** | None | — | — | ❌ **REMOVED** — does not exist |

### RabbitMQ Event Subscriptions (Admin Portal BFF)

The Admin Portal BFF subscribes to these integration events for real-time dashboards and alert feeds:

| Event | Source BC | Admin Portal Handler | Purpose |
|-------|----------|---------------------|---------|
| `OrderPlaced` | Orders | `OrderPlacedAdminHandler` | Revenue/order count KPI |
| `OrderCancelled` | Orders | `OrderCancelledAdminHandler` | KPI adjustment, CS alert |
| `PaymentCaptured` | Payments | `PaymentCapturedAdminHandler` | Revenue confirmation KPI |
| `PaymentFailed` | Payments | `PaymentFailedAdminHandler` | Failure rate KPI, Ops alert |
| `RefundCompleted` | Payments | `RefundCompletedAdminHandler` | Revenue adjustment |
| `ShipmentDispatched` | Fulfillment | `ShipmentDispatchedAdminHandler` | Fulfillment pipeline update |
| `ShipmentDelivered` | Fulfillment | `ShipmentDeliveredAdminHandler` | Fulfillment pipeline update |
| `ShipmentDeliveryFailed` | Fulfillment | `DeliveryFailedAdminHandler` | Ops alert (CS follow-up needed) |
| `LowStockDetected` | Inventory | `LowStockAdminHandler` | WH + Ops alert, low-stock KPI |
| `StockReplenished` | Inventory | `StockReplenishedAdminHandler` | Auto-resolve low-stock alert |
| `ReturnRequested` | Returns | `ReturnRequestedAdminHandler` | Return rate KPI, CS alert |
| `ReturnCompleted` | Returns | `ReturnCompletedAdminHandler` | Return rate KPI update |
| `ReturnExpired` | Returns | `ReturnExpiredAdminHandler` | Ops alert |
| `CorrespondenceQueued` | Correspondence | `CorrespondenceQueuedAdminHandler` | Delivery rate KPI |
| `CorrespondenceDelivered` | Correspondence | `CorrespondenceDeliveredAdminHandler` | Delivery rate KPI |
| `CorrespondenceFailed` | Correspondence | `CorrespondenceFailedAdminHandler` | Ops alert, delivery rate KPI |

---

## Sagas — Reassessment

### Original Saga Candidates

The original event model did not identify explicit Admin Portal sagas. Reassessment:

| Candidate | Assessment | Decision |
|-----------|-----------|----------|
| **Admin user provisioning** | AdminIdentity implements user creation as a simple command handler (`CreateAdminUser`). Delivered in Cycle 29 Phase 1. | **Not a saga.** Simple command handler (confirmed by implementation). |
| **Escalation workflow** (Phase 2) | CS creates escalation → Ops acknowledges → Ops resolves. Three-state lifecycle. | **Not a saga.** Simple Marten document with state transitions. No cross-BC orchestration. |
| **Bulk pricing job** | Already a saga in Pricing BC (`BulkPricingJob`). Admin Portal triggers via HTTP, does not own the saga. | **Delegated.** Admin Portal is a client, not the orchestrator. |
| **Order cancellation** | Already orchestrated by Order saga in Orders BC. Admin Portal calls `POST /cancel`, saga handles compensation. | **Delegated.** Admin Portal issues the command; Orders saga orchestrates the rollback. |

**Conclusion:** Admin Portal does NOT need its own sagas in any phase. It delegates orchestration to domain BCs and owns only simple document-based state (OrderNote, AlertAcknowledgment, EscalationTicket).

---

## Naming Audit

All names verified against the codebase ubiquitous language established in Cycles 21-28.

| Model Name | Codebase Name | Match? | Action |
|-----------|--------------|--------|--------|
| "Notifications BC" | Correspondence BC | ❌ | **[FIXED]** Updated to Correspondence throughout |
| `LowStockAlert` | `LowStockDetected` | ❌ | **[FIXED]** Use `LowStockDetected` (Messages.Contracts name) |
| `StockReplenished` | `StockReplenished` | ✅ | No change |
| `OrderPlaced` | `OrderPlaced` | ✅ | No change |
| `PaymentFailed` | `PaymentFailed` | ✅ | No change |
| `ReturnApproved` | `ReturnApproved` | ✅ | No change |
| `InventoryAdjusted` | `InventoryAdjusted` | ✅ | No change |
| "AdminMetrics" (projection) | N/A (new) | — | Use `AdminDailyMetrics` for clarity |
| `ProductDescriptionUpdated` (event model Flow 1) | Not an existing event — Product Catalog uses document updates, not events | ⚠️ | **[NOTE]** When Product Catalog evolves to event sourcing, this event name should be adopted |
| `PriceChangeScheduled` (event model Flow 2) | `PriceChangeScheduled` exists in Pricing domain events | ✅ | No change |
| `ScheduledPriceActivated` | `ScheduledPriceActivated` exists in Pricing domain events | ✅ | No change |
| `AdminRole` enum values | ✅ Implemented in `AdminIdentity.Identity.AdminRole` (Cycle 29 Phase 1) | ✅ | CopyWriter=1, PricingManager=2, WarehouseClerk=3, CustomerService=4, OperationsManager=5, Executive=6, SystemAdmin=7 |
| `IAdminRoleMessage` / `IAdminUserMessage` | Not yet in codebase — to be created when Admin Portal BFF needs SignalR routing | — | Naming consistent with Vendor Portal pattern |

---

## Security Considerations

**Unchanged from original** with one addition:

| Threat | Mitigation |
|--------|-----------|
| (all original threats remain) | (all original mitigations remain) |
| **[NEW] Stale JWT after admin deactivation** | Check `deactivatedAt` against JWT `iat` claim at BFF validation layer. If user was deactivated after token issuance, reject. Force session termination via SignalR `admin-user:{userId}` group message. |

---

## Risks (Revised)

| Risk | Likelihood | Impact | Change from Original |
|------|-----------|--------|---------------------|
| Admin Portal becomes "God Gateway" | Medium | High | Unchanged |
| **[REVISED]** Frontend technology choice | Low | Low | **Risk reduced** — Blazor WASM decision eliminates React migration debt |
| Audit notes on mutations | Medium | Low | Unchanged |
| SignalR connection overhead | Low | Low | Unchanged |
| Domain BCs add admin endpoints without security | Medium | High | Unchanged |
| **[REMOVED]** Phase 1 Blazor → React migration | — | — | **Eliminated** — Blazor WASM is the final choice |
| **[NEW]** Phase 0.5 endpoint gaps delay Phase 1 | Medium | High | If domain BC teams are slow to add required endpoints, Admin Portal Phase 1 is blocked |
| **[NEW]** Inventory BC has zero HTTP endpoints | High | Medium | Most endpoint-sparse BC. Needs significant work before Admin Portal can integrate. |
| **[NEW]** Product Catalog evolution (document → event sourcing) during Admin Portal lifecycle | Low | Medium | Interface-based HTTP clients insulate Admin Portal from API surface changes |

---

## Dependencies on Other BCs (Revised)

| BC | Dependency Type | Phase | Status |
|----|----------------|-------|--------|
| **Admin Identity** | JWT issuer (prerequisite) | 0 | ✅ **COMPLETE** (Cycle 29 Phase 1) |
| **Customer Identity** | HTTP read (customer lookup, address) | 0.5 + 1 | ⚠️ Missing email search |
| **Orders** | HTTP read (orders, returnable items) + HTTP write (cancel) | 1 | ✅ All endpoints exist |
| **Returns** | HTTP read (returns) + HTTP write (approve, deny) | 1 | ✅ All endpoints exist |
| **Correspondence** | HTTP read (message history) | 1 | ✅ All endpoints exist |
| **Payments** | HTTP read (payment detail) | 1 | ✅ Read endpoint exists |
| **Inventory** | RabbitMQ events (alerts, metrics) + HTTP read/write | 0.5 + 1 + 2 | ⚠️ Zero HTTP endpoints |
| **Fulfillment** | HTTP read (shipment tracking) | 0.5 + 1 | ⚠️ Missing shipment query |
| **Product Catalog** | HTTP read (product list) + HTTP write (content edit) | 1 + 2 | ⚠️ Missing admin write |
| **Pricing** | HTTP read (prices) + HTTP write (set, schedule) | 1 + 2 | ⚠️ Only 2 GET endpoints |
| **Promotions** | HTTP read (active promotions) + HTTP write (manage) | 2 + 3 | 🟡 BC shipping Cycle 29 |
| ~~Analytics~~ | ~~HTTP read (projections)~~ | ~~1~~ | ❌ **REMOVED** |
| ~~Store Credit~~ | ~~HTTP write (issue credit)~~ | ~~3~~ | ❌ **REMOVED** |

---

*See [Integration Gap Register](admin-portal-integration-gap-register.md) for the full audit of every integration point with current status.*
*See [Decision Log](admin-portal-revised-decision-log.md) for what was kept, changed, and removed from the original model.*
*See [Open Questions](admin-portal-open-questions.md) for unresolved items requiring owner decision.*
