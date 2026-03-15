# Backoffice: Event Modeling & Design Session

**Date:** 2026-03-07
**Participants:** Product Owner, Principal Architect, Engineering Lead
**Status:** 🟡 Planning Complete — Awaiting Implementation Cycle Assignment
**Related CONTEXTS.md Section:** [Backoffice](../../CONTEXTS.md#backoffice)
**Port Assignments:** AdminPortal.Api → 5243, AdminPortal.Web → 5244

---

## Purpose

This document captures the collaborative event modeling and design session for the Backoffice Bounded Context. It is the pre-implementation blueprint that expands the initial sketch in CONTEXTS.md with detailed decisions, trade-off analysis, phased roadmap, and risk register.

**This is NOT a one-shot implementation request.** It is planning output that feeds into phased implementation cycle planning.

---

## Core Architecture Decision: Gateway / BFF, Not a Domain BC

### The Key Question

> Is the Backoffice its own BC? Does it need a "gateway API" to access other BCs?

**Yes — it is its own BC, and its API *is* the internal gateway.**

Backoffice follows the **BFF (Backend-for-Frontend) pattern**, the same pattern used by Customer Experience (for customers) and Vendor Portal (for partner vendors). The difference is the audience: internal employees.

### Why BFF, Not a Pure API Gateway

A generic API gateway (Kong, NGINX, Azure API Management) forwards requests transparently. An Backoffice BFF does more:

| Concern | Generic Gateway | Backoffice BFF |
|---|---|---|
| Authentication | Validates token | Validates token + role claims |
| Authorization | Route-level allow/deny | Fine-grained: CopyWriter can't touch inventory |
| Response shaping | Passes BC response as-is | Merges multiple BC responses into role-tailored view model |
| Audit trail | Access logs | Injects `adminUserId` into every command |
| Real-time push | Not applicable | Subscribes to domain BC events → SignalR push to connected clients |
| Error translation | Passes 4xx/5xx through | Translates domain BC errors into admin-friendly problem details |

A BFF *earns* its existence by eliminating round-trips for the frontend (fan-out queries), enforcing consistent RBAC, and providing a stable API surface that can evolve independently from the domain BCs it wraps.

### Analogy Within This Codebase

```
Customer Experience BC
  └─> CustomerStorefront.Api (BFF for customers)
      └─> Aggregates: Shopping, Orders, Catalog, Payments
      └─> Frontend: Blazor Server (Storefront.Web)

Vendor Portal BC
  └─> VendorPortal.Api (BFF for vendor partners)
      └─> Aggregates: Product Catalog, Inventory, Orders (vendor slice)
      └─> Frontend: TBD (Blazor)

Backoffice BC  ← this document
  └─> AdminPortal.Api (BFF for internal employees)
      └─> Aggregates: ALL domain BCs (role-gated)
      └─> Frontend: React (Next.js SSR) recommended (or Blazor / Vue)
```

---

## Lessons Learned (Do Not Repeat)

| What Went Wrong in Existing BCs | What Backoffice Should Do Instead |
|---|---|
| Customer Experience started with SSE, migrated to SignalR (1 full cycle of rework) | SignalR from day one — `opts.UseSignalR()` |
| `customerId` from query string in StorefrontHub (security concern) | `adminUserId` and `adminRole` from JWT claims ONLY |
| Blurry boundary between domain project / API project | Strict: domain = logic + clients + view models + handlers; API = HTTP + SignalR + DI |
| No RBAC on early admin-style endpoints | Authorization policies declared before first endpoint is written |
| Frontend coupled tightly to Blazor, hard to replace | Backend API is frontend-agnostic; frontend choice is a separate decision |
| Audit trail bolted on later | Every command includes `adminUserId` from day one; domain BCs record it in events |
| PII accidentally in SignalR messages | Define `IAdminPortalMessage` with a rule: no PII in push messages; PII fetched via HTTP GET only |

---

## Internal User Personas & Role Definitions

### Personas

CritterSupply has six distinct internal user personas with different daily tasks and different data access needs.

| Persona | System Role | Primary Job | Sensitive Access? |
|---|---|---|---|
| Content / copy writer | `CopyWriter` | Write product descriptions, display names | No PII, no financials |
| Pricing / vendor manager | `PricingManager` | Set and schedule product prices | Financial data (prices only) |
| Warehouse / inventory worker | `WarehouseClerk` | Adjust stock, receive goods, acknowledge alerts | Inventory data only |
| Customer service representative | `CustomerService` | Resolve customer issues, cancel orders, issue credits | PII (customer emails, addresses) |
| Operations manager | `OperationsManager` | Monitor system health, fulfillment, escalate issues | All operational data; no PII beyond CS role |
| Business executive | `Executive` | Strategic dashboards, report exports | Aggregated data only; no PII |
| Platform admin | `SystemAdmin` | Manage admin users, system configuration | All access |

### Role Permission Matrix

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
| Issue store credit | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ |
| View executive dashboard | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| Export reports (CSV/Excel) | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| View live operations alerts | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| Manage admin user accounts | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |

---

## Technology Decisions

| Aspect | Decision | Rationale |
|---|---|---|
| Backend persistence | Marten (lightweight — cached projections only) | Domain BCs are source of truth; Backoffice only needs to cache dashboard metrics |
| Auth mechanism | JWT Bearer (BackofficeIdentity BC) | SignalR requires JWT for hub authentication; role claims drive RBAC |
| Real-time | SignalR via `opts.UseSignalR()` | Bidirectional: push alerts to connected clients; lesson learned from SSE→SignalR migration |
| Frontend (recommended) | React (Next.js with SSR) | Richer data visualization ecosystem; SSR for dashboard load performance |
| Frontend (alternative A) | Vue.js (Nuxt.js with SSR) | Similar SSR benefits; smaller bundle, simpler state management |
| Frontend (alternative B) | Blazor Server | Full C# consistency; simpler for .NET-only teams |
| SignalR client (React/Vue) | `@microsoft/signalr` npm package | Same package used in other JS SignalR clients; well-maintained |
| SignalR client (Blazor) | `Microsoft.AspNetCore.SignalR.Client` | Native .NET client |
| Hub groups | Role-scoped: `role:{roleName}` | Alerts targeted to roles that need to act (vs. per-user — too granular for broadcast metrics) |
| Port | 5243 (API), 5244 (Web) | Follows port allocation table in CLAUDE.md |
| Schema | `adminportal` | Follows existing BC schema naming convention |

---

## Backoffice Identity: Authentication Prerequisite

Backoffice requires its own internal user identity system. This is a lightweight additional BC with three viable approaches:

### Option 1: Local Backoffice Identity Store (Recommended for Phase 1)

**Same pattern as Customer Identity and Vendor Identity:**
- ASP.NET Core Identity + EF Core + Postgres (`backofficeidentity` schema)
- Roles stored in `AspNetUserRoles` table
- JWT issued on login (15-min access + 7-day refresh in HttpOnly cookie)
- Password hashing: Argon2id (same as Vendor Identity — no plaintext shortcuts)

**Pro:** Zero external dependencies; works in local dev without corporate SSO.
**Con:** Requires separate user management UI (a `SystemAdmin` role within Backoffice itself).

### Option 2: Corporate SSO / IdP Integration

Microsoft Entra ID (Azure AD), Okta, or Auth0. Admin users log in with corporate credentials.

**Pro:** No separate user store to maintain; automatic offboarding when employee leaves.
**Con:** External service dependency; complicates local development; overkill for small teams.

### Option 3: Hybrid (Phase 1 Local → Phase 2 SSO)

Start with local store. Add OIDC/SAML SSO integration in a later cycle without changing the rest of Backoffice.

**Recommendation:** Option 1 for Phase 1 (reference architecture clarity); Option 3 roadmap (practical enterprise path).

---

## Phased Roadmap

### Phase 1: Read-Only Dashboards + Customer Service Tooling

**Goal:** Immediate operational value without full RBAC complexity.

**Included:**
- Backoffice Identity: user store, JWT auth, `Executive`, `OperationsManager`, `CustomerService`, `SystemAdmin` roles
- AdminPortal.Api: gateway skeleton, health endpoint, SignalR hub
- SignalR integration: `OrderPlaced`, `PaymentFailed`, `InventoryLow` → alert push to connected clients
- Customer service endpoints: customer search, order detail, cancel order
- Executive dashboard: today's order count (live counter), top-level revenue (from Analytics BC)
- Frontend: simple read-only dashboard (can start with Blazor for speed, migrate later)

**BCs required:** Customer Identity, Orders, Analytics, Inventory (read), Payments (read)

### Phase 2: Content Management + Pricing + Inventory Write

**Goal:** Eliminate manual workarounds for product updates, price changes, and stock adjustments.

**Included:**
- `CopyWriter` role: product content edit endpoints → Product Catalog BC
- `PricingManager` role: price set + schedule endpoints → Pricing BC (requires Pricing BC to be live)
- `WarehouseClerk` role: inventory adjust + receive stock + acknowledge alert endpoints → Inventory BC
- Full role permission matrix enforcement
- Audit trail verification (spot-check that `adminUserId` appears in domain BC event streams)

**BCs required:** Product Catalog (admin endpoints), Pricing BC, Inventory BC (write endpoints)

### Phase 3: Store Credit + Report Exports + Frontend Polish

**Goal:** Full customer service capability + executive reporting.

**Included:**
- `CustomerService` store credit issuance → Store Credit BC (requires Store Credit BC to be live)
- Report exports: CSV/Excel downloads from Analytics BC projections
- Frontend upgrade: if Phase 1/2 used Blazor, migrate to React/Next.js for richer dashboard UX
- SignalR polish: alert badge count, toast notifications per role
- Notification preferences per admin user (opt out of alert types)

---

## Open Decisions

| # | Question | Status | Recommendation |
|---|---|---|---|
| 1 | Which frontend framework? | 🟡 Open | React (Next.js SSR) for Phase 3; Blazor acceptable for Phase 1/2 |
| 2 | Backoffice Identity: local vs. SSO? | 🟡 Open | Local store (Phase 1); plan SSO path for Phase 3 |
| 3 | Can OperationsManager cancel orders? | ✅ Resolved | Yes — same as CustomerService; both roles need this capability |
| 4 | Report format: CSV, Excel, PDF? | 🟡 Open | CSV first (simplest); Excel (via ClosedXML) in Phase 3; PDF deferred |
| 5 | Should Executive see customer count (PII-adjacent)? | ✅ Resolved | Aggregates only (counts, not emails/names); no PII in Executive dashboard |
| 6 | Multi-tenancy for admin users? | ✅ Resolved | No tenancy — all internal users share a single Backoffice (unlike Vendor Portal which is per-vendor) |
| 7 | Hub group granularity: role vs. department vs. individual? | ✅ Resolved | Role-scoped groups (`role:executive` etc.) — sufficient for broadcast alerts; per-user groups not needed |
| 8 | Should CopyWriter see inventory counts on the product list? | 🟡 Open | Lean toward No — information minimization; they only need description/name fields |

---

## Event Modeling: Key Flows

### Flow 1: Copy Writer Updates Product Description

```
Trigger: Content writer receives editorial brief, opens admin portal

[Backoffice Frontend]
Writer navigates to /admin/products
  └─> GET /api/admin/products?status=published (AdminPortal.Api)
      └─> AdminPortal.Api: IProductCatalogAdminClient.GetProductsAsync()
          └─> GET /api/catalog/products?status=published (Product Catalog BC)
              └─> Return list: [{sku, displayName, description, lastEditedBy, lastEditedAt}]
      └─> Return ProductContentListView to frontend

Writer finds product, opens edit modal
  └─> GET /api/admin/products/{sku}/content (AdminPortal.Api)
      └─> Product Catalog BC: GET /api/catalog/products/{sku}
          └─> Return full ProductContentView

Writer edits description, clicks Save
  └─> PUT /api/admin/products/{sku}/content
      Body: { description: "New copy...", adminUserId: "uuid-from-jwt" }
      Auth: Bearer {jwt} (role: CopyWriter)

AdminPortal.Api:
  ├─> [Authorize(Policy = "CopyWriterOrAbove")] — 403 for other roles
  ├─> Validate: description not empty, ≤ 5000 chars
  └─> IProductCatalogAdminClient.UpdateDescriptionAsync(sku, description, adminUserId)
      └─> POST /api/catalog/products/{sku}/description (Product Catalog BC)
          └─> Product Catalog BC: UpdateProductDescription command
              └─> ProductDescriptionUpdated event (stream: product-{sku})
                  ├─> carries: adminUserId, oldDescription (for diff), newDescription, timestamp
                  └─> Search BC (when live): handler updates search index
```

### Flow 2: Pricing Manager Schedules a Black Friday Sale

```
Trigger: Black Friday sale planning, 4 weeks ahead

[Backoffice Frontend]
PricingManager navigates to /admin/pricing
  └─> GET /api/admin/pricing?sku={optional filter} (AdminPortal.Api)
      └─> Pricing BC: GET /api/pricing/products (current prices + scheduled changes)
          └─> Return PricingDashboardView

Manager selects SKU, clicks "Schedule Price Change"
  └─> POST /api/admin/products/{sku}/price/schedule
      Body: {
        newPrice: 19.99,
        effectiveAt: "2026-11-28T00:00:00Z",
        expiresAt: "2026-12-02T23:59:59Z",
        reason: "Black Friday 2026"
      }
      Auth: Bearer {jwt} (role: PricingManager)

AdminPortal.Api:
  ├─> [Authorize(Policy = "PricingManagerOrAbove")]
  ├─> Validate: newPrice > 0, effectiveAt > now, expiresAt > effectiveAt
  └─> IPricingAdminClient.SchedulePriceChangeAsync(...)
      └─> POST /api/pricing/products/{sku}/price/schedule (Pricing BC)
          └─> Pricing BC: SchedulePriceChange command
              └─> PriceChangeScheduled event
                  ├─> Background job applies at effectiveAt → PriceChanged event
                  ├─> Background job reverts at expiresAt → PriceReverted event
                  └─> Both events trigger Search BC re-index (price facets updated)
```

### Flow 3: Warehouse Clerk Receives Inbound Stock

```
Trigger: Delivery truck arrives with purchase order PO-2026-0042

[Backoffice Frontend — ideally a mobile-friendly warehouse view]
WarehouseClerk scans barcode → pre-fills SKU field
  └─> GET /api/admin/inventory/{sku} (AdminPortal.Api)
      └─> Inventory BC: GET /api/inventory/{sku}?warehouseId={id}
          └─> Return: { available: 3, reserved: 1, lowStockThreshold: 10 }

Clerk enters received quantity: 50, enters PO ref: PO-2026-0042, clicks Receive
  └─> POST /api/admin/inventory/{sku}/receive
      Body: { warehouseId: "WH-EAST-01", quantity: 50, purchaseOrderRef: "PO-2026-0042" }
      Auth: Bearer {jwt} (role: WarehouseClerk)

AdminPortal.Api:
  ├─> [Authorize(Policy = "WarehouseClerkOrAbove")]
  ├─> Validate: quantity > 0, warehouseId non-empty
  └─> IInventoryAdminClient.ReplenishStockAsync(sku, warehouseId, 50, "PO-2026-0042", adminUserId)
      └─> POST /api/inventory/{sku}/replenish (Inventory BC)
          └─> Inventory BC: ReplenishStock command
              └─> StockReplenished event
                  ├─> Available: 3 + 50 = 53
                  ├─> Threshold check: 53 > 10 → deactivate any active LowStockAlert
                  ├─> LowStockAlertDeactivated → Backoffice: push alert dismissed to SignalR
                  └─> Vendor Portal: InventorySnapshot projection updated (if SKU has vendor)

[SignalR push to connected WarehouseClerks and OperationsManagers]
LowStockAlertDeactivated
  └─> AdminPortal: LowStockAlertDeactivatedHandler
      └─> Publish LowStockAlertResolved → hub groups: role:warehouseclerk, role:operations
          └─> Alert badge count decrements in connected clients
```

### Flow 4: Real-Time Executive Dashboard Update

```
[Continuous — every order placed during business hours]

Orders BC: CheckoutCompleted → Order saga → OrderPlaced published to RabbitMQ

[Backoffice domain assembly — Wolverine handler]
OrderPlaced (from RabbitMQ)
  └─> OrderPlacedAdminHandler
      ├─> Update AdminMetrics Marten document:
      │     TodayOrderCount++
      │     TodayEstimatedRevenue += order.TotalAmount
      └─> Wolverine SignalR transport:
          └─> Publish LiveMetricUpdated:
              { ordersToday: 47, estimatedRevenueToday: 4218.93 }
              to hub groups: role:executive, role:operations
              └─> React/Vue frontend: revenue counter animates (CSS transition)
                  order pipeline bar updates without page reload
```

---

## Security Considerations

### What Can Go Wrong (Threat Model)

| Threat | Mitigation |
|---|---|
| Unauthorized user accesses admin portal | JWT auth required on all routes; hub requires auth |
| CopyWriter accesses pricing or customer data | RBAC policy enforcement at API layer; frontend filtering is cosmetic only |
| Forged `adminUserId` in command body | `adminUserId` extracted from JWT claims at gateway — body field ignored if present |
| PII leak via SignalR messages | Policy: no PII in `IAdminPortalMessage` implementations; only IDs and counts |
| Replay attack on management endpoints | JWT expiry (15 min); each command includes `adminUserId` and timestamp for idempotency check |
| Privilege escalation via domain BC endpoints | Domain BCs should validate that admin endpoints require a trusted internal caller header; Backoffice passes a service-to-service API key or uses mTLS |
| GDPR: PII accessed without justification | Access logs for customer-lookup endpoints; consider requiring reason field for CS lookups |

### Service-to-Service Authentication (Backoffice → Domain BCs)

Domain BCs currently don't distinguish internal calls from external calls. For Backoffice, consider one of:

1. **Shared secret / API key header** — `X-Admin-Portal-Key: {secret}` — simple, not ideal for production
2. **mTLS (mutual TLS)** — AdminPortal.Api presents a client certificate; domain BCs validate it
3. **OAuth 2.0 client credentials** — AdminPortal.Api authenticates as a service principal to a token server; domain BCs validate JWT
4. **Network-level isolation** — Backoffice and domain BCs run in a private network segment; external access blocked at the load balancer (acceptable for MVP)

**Recommendation:** Network isolation for Phase 1 (simplest); OAuth 2.0 client credentials for Phase 2+.

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Backoffice becomes a "God Gateway" that leaks domain logic | Medium | High | Strict invariant: gateway only routes and composes; all validation in domain BCs |
| Frontend technology choice creates long-term maintenance burden | Low | Medium | Backend API is framework-agnostic; frontend can be replaced independently |
| Admin users forget to include reason/audit notes on mutations | Medium | Low | Make `reason` field required in API validation; not optional |
| Real-time dashboard creates high SignalR connection overhead | Low | Low | Role-scoped hub groups limit connection count; executive users typically < 10 concurrent |
| Domain BCs add admin endpoints without considering security | Medium | High | Admin endpoints should be clearly namespaced (`/api/admin/*` or behind `[Authorize("InternalService")]`) |
| Phase 1 Blazor frontend creates migration debt (SSE lesson) | Medium | Medium | Use SignalR from day one even in Blazor; migration from Blazor to React/Vue is UI only if backend is stable |

---

## Dependencies on Other BCs

| BC | Dependency Type | Phase |
|---|---|---|
| Customer Identity | HTTP read (customer lookup) | Phase 1 |
| Orders BC | HTTP read (order detail) + HTTP write (cancel) | Phase 1 |
| Inventory BC | HTTP read (stock levels, alerts) + HTTP write (adjust, replenish, acknowledge) | Phase 1 (read) + Phase 2 (write) |
| Analytics BC | HTTP read (projections for dashboard) | Phase 1 |
| Product Catalog BC | HTTP read (product list) + HTTP write (description update) | Phase 2 |
| Payments BC | HTTP read (payment history) | Phase 2 |
| Returns BC | HTTP read (return history) | Phase 2 |
| Pricing BC | HTTP read (prices) + HTTP write (set, schedule) | Phase 2 (requires Pricing BC to be live) |
| Store Credit BC | HTTP write (issue credit) | Phase 3 (requires Store Credit BC to be live) |

---

*See [CONTEXTS.md — Backoffice](../../CONTEXTS.md#backoffice) for the architectural specification.*
*See [docs/features/backoffice/](../features/backoffice/) for Gherkin feature specifications.*
