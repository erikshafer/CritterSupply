# M32.0: Admin Portal Phase 1 — Prerequisite Assessment

**Date:** 2026-03-15
**Status:** ✅ DECISION MADE — Option A (M31.5 separate milestone) approved
**Milestone:** M32.0 (Admin Portal Phase 1)
**Assessment Completed By:** AI Agent (Claude Sonnet 4.5)
**Owner Decision:** Option A (create M31.5) — approved 2026-03-15

---

## Executive Summary

**M32.0 cannot proceed to implementation until M31.5 is complete.** The milestone had three explicit prerequisites defined in the problem statement and planning documents. One is complete, but **two critical prerequisites were not met** (discovered during Step 4 of the implementation process).

**Owner Decision (2026-03-15):** Owner chose **Option A** — create M31.5 (Admin Portal Prerequisites) as a separate milestone. This separates infrastructure work (JWT configuration, endpoint gaps) from Admin Portal BFF implementation (M32.0).

| Prerequisite | Status | Blocker Severity |
|--------------|--------|------------------|
| **1. RBAC ADR** | ✅ **COMPLETE** | Not blocking |
| **2. Multi-issuer JWT** | ❌ **NOT IMPLEMENTED** | 🔴 **HARD BLOCKER** — resolved in M31.5 |
| **3. Domain BC endpoint gaps** | ⚠️ **8 GAPS IDENTIFIED** | 🔴 **HARD BLOCKER** — resolved in M31.5 |

**Next Steps:** Implement M31.5 (4-5 sessions) to close all blockers, then M32.0 can proceed with all prerequisites met.

---

## Detailed Assessment

### Prerequisite 1: RBAC ADR ✅ COMPLETE

**Status:** ADR 0031 "Admin Portal Role-Based Access Control Model" is **ACCEPTED** (2026-03-13) and covers M32.0 scope completely.

**What ADR 0031 Delivers:**
- ✅ 7 admin roles defined (CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin)
- ✅ Policy-based authorization pattern with ASP.NET Core `RequireRole()` policies
- ✅ SystemAdmin as superuser (automatically included in all policies)
- ✅ JWT claims structure documented (`sub`, `role`, `name`, `email`, `iss`, `aud`)
- ✅ Single role per user (Phase 1 constraint)
- ✅ Admin Identity BC requirements documented (EF Core, PBKDF2-SHA256, port 5249)

**What's Already Implemented:**
- ✅ Admin Identity BC (M29.0, PR #375) — login, refresh, logout, user CRUD, port 5249
- ✅ 7 authorization policies in AdminIdentity.Api/Program.cs
- ✅ JWT token generation with `role` claim

**Coverage for M32.0:**
- ✅ Read-only dashboards: Covered by `CustomerService`, `OperationsManager`, `Executive` policies
- ✅ Customer service tooling: Covered by `CustomerService` and `CustomerServiceOrAbove` policies
- ✅ Warehouse operations: Covered by `WarehouseClerk` and `WarehouseOrOperations` policies

**No action needed.** RBAC ADR is finalized and implementation-ready.

---

### Prerequisite 2: Multi-issuer JWT ❌ NOT IMPLEMENTED

**Status:** ❌ **DOES NOT EXIST** — This is a Phase 0.5 hard blocker.

**Current State:**
- Admin Identity BC (port 5249) issues JWTs with `iss: "https://localhost:5249"` and `aud: "https://localhost:5249"` (self-referential)
- These tokens work for Admin Identity's own protected endpoints (`/api/admin-identity/users`)
- **Problem:** Domain BCs (Orders, Returns, Customer Identity, Payments, Inventory, Fulfillment, Correspondence, Pricing, Product Catalog) do NOT have JWT Bearer authentication configured
- **Impact:** Admin Portal BFF will issue commands/queries to domain BCs, but domain BCs will reject the requests because they don't validate admin JWTs

**What Multi-issuer JWT Means:**
Domain BCs need to accept JWTs from **multiple issuers**:
1. **Admin Identity BC** (`https://localhost:5249`) — for admin users accessing Admin Portal
2. **Vendor Identity BC** (`https://localhost:5240`) — already configured in Product Catalog.Api for vendor partner access
3. **Customer Identity BC** (session-based) — customers use cookies, not JWTs

**Implementation Pattern (from ADR 0028 and 0031):**

```csharp
// Example: Orders.Api/Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Admin", options =>
    {
        options.Authority = "https://localhost:5249";
        options.Audience = "https://localhost:5249"; // Phase 1: self-referential
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    })
    .AddJwtBearer("Vendor", options =>
    {
        options.Authority = "https://localhost:5240";
        options.Audience = "https://localhost:5240";
        // ... same validation parameters
    });

builder.Services.AddAuthorization(opts =>
{
    // Admin policies
    opts.AddPolicy("CustomerService", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin");
    });

    // Vendor policies (existing)
    opts.AddPolicy("VendorAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireRole("VendorAdmin");
    });
});
```

**BCs That Need Multi-issuer JWT Setup:**

| BC | Current Auth | Needs Admin Scheme | Effort | Priority |
|----|--------------|-------------------|--------|----------|
| Orders.Api | None | ✅ Yes | < 1 session | Phase 0.5 |
| Returns.Api | None | ✅ Yes | < 1 session | Phase 0.5 |
| Customer Identity.Api | Cookie-based (customers) | ✅ Yes | < 1 session | Phase 0.5 |
| Correspondence.Api | None | ✅ Yes (CS access) | < 1 session | Phase 0.5 |
| Inventory.Api | None (no HTTP layer) | ✅ Yes | 1 session (+ HTTP layer) | Phase 0.5 |
| Fulfillment.Api | None | ⚠️ Maybe (WISMO queries) | < 1 session | Phase 0.5 |
| Payments.Api | None | ⚠️ Defer to Phase 2 | < 1 session | Phase 2 |
| Pricing.Api | None | ⚠️ Defer to Phase 2 | < 1 session | Phase 2 |
| Product Catalog.Api | Vendor JWT | ✅ Yes (add admin) | < 1 session | Phase 2 |

**Estimated Effort:** 1 session to add admin JWT scheme to 5 BCs (Orders, Returns, Customer Identity, Correspondence, Fulfillment).

**ADR Required:** ADR 0032 "Multi-Issuer JWT Strategy" must be created before implementation begins. This ADR should cover:
- Token validation pattern (named schemes: `"Admin"`, `"Vendor"`)
- Policy-based authorization mapping (which policies use which schemes)
- Audience evolution (Phase 1: self-referential `https://localhost:5249`; Phase 2+: Admin Portal API audience `https://localhost:5243`)
- Signing key management (shared secret or asymmetric keys)
- Cross-BC consistency (all domain BCs follow same pattern)

**Escalation:** Multi-issuer JWT ADR does not exist. Creating and finalizing it is the **first implementation task** of this milestone.

---

### Prerequisite 3: Domain BC Endpoint Gaps ⚠️ 8 GAPS IDENTIFIED

**Status:** ⚠️ **8 Phase 0.5 blockers** documented in `admin-portal-integration-gap-register.md`

**Gap Summary:**

| Gap | Owning BC | Why It Blocks Phase 1 | Effort |
|-----|-----------|----------------------|--------|
| `GET /api/customers?email={email}` | Customer Identity | CS workflow starts with email lookup (P0) | < 1 session |
| `GET /api/inventory/{sku}` | Inventory | WarehouseClerk dashboard, low-stock KPI | 1 session |
| `GET /api/inventory/low-stock` | Inventory | WarehouseClerk alert feed | 1 session |
| `GET /api/fulfillment/shipments?orderId={id}` | Fulfillment | WISMO (35-40% of CS tickets) | < 1 session |
| Admin JWT in Orders.Api | Orders | Auth for order cancellation | < 1 session |
| Admin JWT in Returns.Api | Returns | Auth for return approve/deny | < 1 session |
| Admin JWT in Customer Identity.Api | Customer Identity | Auth for customer search | < 1 session |
| Admin JWT in Correspondence.Api | Correspondence | Auth for message history | < 1 session |

**Total Estimated Effort:** 4-5 sessions

#### Gap 1: Customer Search by Email (Customer Identity BC)

**Current State:**
- `GET /api/customers/{id}` exists (ID-based lookup)
- `GET /api/customers?email={email}` **does not exist**

**Why It Blocks Phase 1:**
CS workflow starts with customer email lookup. Agent receives call from customer, asks for email, needs to pull up customer record. Without this endpoint, CS tooling is dead on arrival.

**Implementation:**
```csharp
// CustomerIdentity.Api/Queries/GetCustomerByEmail.cs
public static class GetCustomerByEmailQuery
{
    [WolverineGet("/api/customers")]
    [Authorize(Policy = "CustomerService")] // Admin JWT
    public static async Task<CustomerView?> Handle(
        string email,
        CustomerIdentityDbContext db,
        CancellationToken ct)
    {
        var customer = await db.Customers
            .Where(c => c.Email == email)
            .Select(c => new CustomerView(c.Id, c.Email, c.FirstName, c.LastName))
            .FirstOrDefaultAsync(ct);

        return customer;
    }
}
```

**Estimated Effort:** < 1 session

---

#### Gap 2-3: Inventory BC HTTP Layer (Inventory BC)

**Current State:**
- Inventory BC has **zero HTTP endpoints**
- Entirely message-driven via RabbitMQ handlers (`ReserveStock`, `CommitReservation`, `ReleaseReservation`, `InitializeInventory`, `ReceiveStock`)
- Commands exist in code but are not exposed as HTTP endpoints

**Why It Blocks Phase 1:**
- WarehouseClerk dashboard needs stock levels per SKU
- WarehouseClerk alert feed needs low-stock alert summary
- OperationsManager dashboard needs low-stock KPI

**Phase 0.5 Needs:**
1. `GET /api/inventory/{sku}` — Stock level query (QuantityAvailable, QuantityReserved, LowStockThreshold)
2. `GET /api/inventory/low-stock` — Low-stock alert summary (returns list of SKUs below threshold)

**Phase 2 Needs (deferred):**
3. `POST /api/inventory/{sku}/receive` — Warehouse stock receipt
4. `POST /api/inventory/{sku}/adjust` — Manual inventory adjustment

**Implementation Notes:**
- Inventory BC uses Marten event sourcing (`InventoryItem` aggregate)
- Need to add Marten snapshot projection for queryability: `opts.Projections.Snapshot<InventoryItem>(SnapshotLifecycle.Inline);`
- Add `InventoryItem.Api` project (Web SDK) if it doesn't exist
- Add `Queries/` folder for HTTP endpoints

**Estimated Effort:** 1 session for HTTP layer + 2 query endpoints

---

#### Gap 4: Shipment Query by Order (Fulfillment BC)

**Current State:**
- Fulfillment BC has shipment dispatching logic
- Unknown if `GET /api/fulfillment/shipments?orderId={id}` exists (needs codebase verification)

**Why It Blocks Phase 1:**
CS agents handle "Where Is My Order?" (WISMO) tickets — 35-40% of all CS volume. Agents need shipment tracking data (carrier, tracking number, dispatch date, status) to answer customer questions.

**Implementation (if missing):**
```csharp
// Fulfillment.Api/Queries/GetShipmentsForOrder.cs
public static class GetShipmentsForOrderQuery
{
    [WolverineGet("/api/fulfillment/shipments")]
    [Authorize(Policy = "CustomerService")] // Admin JWT
    public static async Task<IReadOnlyList<ShipmentView>> Handle(
        Guid orderId,
        IQuerySession session,
        CancellationToken ct)
    {
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync(ct);

        return shipments.Select(s => new ShipmentView(
            s.Id,
            s.OrderId,
            s.TrackingNumber,
            s.Carrier,
            s.DispatchedAt,
            s.Status
        )).ToList();
    }
}
```

**Estimated Effort:** < 1 session (assuming Shipment aggregate is queryable)

---

#### Gap 5-8: Admin JWT Schemes in Domain BCs

**Implementation Pattern (apply to all 4 BCs):**

```csharp
// Example: Orders.Api/Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Admin", options =>
    {
        options.Authority = "https://localhost:5249"; // Admin Identity BC
        options.Audience = "https://localhost:5249";  // Phase 1: self-referential
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role" // Map JWT "role" claim to ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("CustomerService", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin");
    });
});
```

**Apply to:**
1. Orders.Api — CS needs to cancel orders
2. Returns.Api — CS needs to approve/deny returns
3. Customer Identity.Api — CS needs to search customers
4. Correspondence.Api — CS needs to view message history

**Estimated Effort:** < 1 session (copy-paste pattern across 4 BCs)

---

## Event Modeling Gate Assessment

**Status:** ⚠️ **GAPS EXIST** — Event model does not cover M32.0 specific flows in sufficient detail

**What the Revised Event Model Covers:**
- ✅ Copy Writer updates product description (Flow 1)
- ✅ Pricing Manager schedules price change (Flow 2)
- ✅ Warehouse Clerk receives stock (Flow 3)
- ✅ Real-time executive dashboard update (Flow 4)
- ✅ CS Agent handles return request (Flow 5)

**What's Missing for M32.0:**
1. **Multi-issuer JWT authentication flow** — How admin user logs in, JWT validation at domain BCs, token refresh
2. **Commands issued through Admin Portal BFF** — Admin Portal BFF → domain BC command routing (HTTP client pattern)
3. **Queries powering read-only dashboards** — AdminDailyMetrics projection design, SignalR group routing for live updates
4. **Customer service tooling commands** — Order cancellation with admin audit trail, return approval with CS reason

**Recommendation:**
- If implementing Phase 0.5 only (domain BC prerequisites), **no additional event modeling needed** — these are infrastructure changes (JWT config, HTTP endpoints wrapping existing handlers)
- If implementing M32.0 Phase 1 (Admin Portal BFF), **conduct targeted event modeling session** covering:
  - Auth flows (login, JWT validation, refresh)
  - BFF composition patterns (how BFF queries multiple BCs and composes views)
  - Real-time dashboard updates (RabbitMQ → Wolverine handler → SignalR push)
  - CS tooling commands (BFF → domain BC with admin attribution)

**Estimated Effort:** 1-2 hours focused event modeling session (3 personas: PSA, PO, UXE)

---

## Escalation Items for Owner

The following items required owner input before M32.0 could proceed:

### 1. Phase 0.5 vs. M32.0 Scope Decision ✅ RESOLVED

**Question:** Should Phase 0.5 (domain BC prerequisites) be a separate milestone (M31.5) or folded into M32.0 as the first phase?

**Context:**
- Phase 0.5 is **4-5 sessions** of work (multi-issuer JWT ADR + 8 endpoint gaps)
- M32.0 Phase 1 (Admin Portal BFF implementation) is **2-3 cycles** of work
- Mixing infrastructure work (Phase 0.5) with BFF implementation (Phase 1) in a single milestone risks confusion

**Recommendation:** Create **M31.5: Admin Portal Prerequisites** as a separate milestone:
- Deliverables: ADR 0032 (Multi-issuer JWT), 8 endpoint gaps closed, JWT schemes configured in 5 domain BCs
- Success Criteria: Admin Portal can authenticate against domain BCs and call all Phase 1-required endpoints
- Duration: 1 cycle (4-5 sessions)
- Then: M32.0 becomes pure Admin Portal BFF implementation (cleaner scope)

**Owner Decision (2026-03-15):** ✅ **Option A approved** — M31.5 created as separate milestone. See:
- [M31.5 Milestone Plan](milestones/m31-5-admin-portal-prerequisites.md)
- [Phase 0.5 Implementation Plan](phase-0-5-implementation-plan.md)

### 2. Multi-issuer JWT ADR Ownership ⏳ IN PROGRESS

**Question:** Who writes ADR 0032 "Multi-Issuer JWT Strategy"?

**Context:**
- ADR 0031 (RBAC) was created during Admin Identity BC implementation (M29.0)
- ADR 0032 is a cross-cutting architectural decision affecting 9+ domain BCs
- Principal Software Architect typically owns cross-BC infrastructure decisions

**Recommendation:** PSA writes ADR 0032 before any multi-issuer JWT implementation begins. AI agent can draft the ADR based on ADR 0028 (Vendor Identity JWT) and ADR 0031 (Admin RBAC) patterns, but PSA must review and sign off.

**Status:** ADR 0032 drafted (2026-03-15), awaiting PSA and PO sign-off. See [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)

### 3. Audience Evolution Timeline ⏳ DEFERRED TO PHASE 2+

**Question:** When should Admin Identity BC start issuing tokens with `aud: "https://localhost:5243"` (Admin Portal API audience)?

**Context:**
- Phase 1: Admin Identity issues tokens with `aud: "https://localhost:5249"` (self-referential)
- Domain BCs configure `options.Audience = "https://localhost:5249"` to accept admin tokens
- Phase 2+: Admin Portal API (port 5243) is built; should have its own audience
- Changing audience requires coordinated update across Admin Identity BC + all domain BCs

**Recommendation:** Defer audience evolution to Phase 2+. Phase 1 uses self-referential audience to minimize coordination overhead. Document the limitation in ADR 0032 and plan the migration when Admin Portal API ships.

**Decision:** Deferred to Phase 2+ as recommended. Documented in ADR 0032 "Future Evolution" section.

---

## Owner Decision: Option A (M31.5 Separate Milestone)

**Decision Made:** 2026-03-15
**Decision:** Option A — Create M31.5 (Admin Portal Prerequisites) as separate milestone

**Rationale:**
- Cleaner scope separation: Infrastructure work (M31.5) vs BFF implementation (M32.0)
- Easier progress tracking: M31.5 has clear, objective success criteria (8 endpoint gaps closed, JWT schemes configured)
- Lower risk: M31.5 can be completed and verified before starting M32.0 BFF work
- Better milestone sizing: M31.5 (1 cycle) vs M32.0 (2-3 cycles)

**M31.5 Deliverables:**
1. ADR 0032 "Multi-Issuer JWT Strategy" (accepted)
2. `GET /api/customers?email={email}` in Customer Identity.Api
3. Inventory BC HTTP layer (`GET /api/inventory/{sku}`, `GET /api/inventory/low-stock`)
4. `GET /api/fulfillment/shipments?orderId={id}` verified/added
5. Multi-issuer JWT schemes in Orders, Returns, Customer Identity, Correspondence, Fulfillment APIs
6. Product Catalog.Api policy rename (`"Admin"` → `"VendorAdmin"`)
7. Integration tests verifying admin JWT acceptance
8. Documentation updates

**M31.5 Success Criteria:** Admin user can authenticate against all domain BCs and call all Phase 1-required endpoints

**After M31.5:** M32.0 can proceed with all prerequisites met

---

## Recommendation: Implement M31.5 First

**Path Forward (Approved):**

### ✅ Option A: M31.5 (Admin Portal Prerequisites) — Separate Milestone (APPROVED)
1. Create ADR 0032 "Multi-Issuer JWT Strategy" (PSA, PO sign-off)
2. Add `GET /api/customers?email={email}` to Customer Identity.Api
3. Add HTTP layer to Inventory.Api (`GET /api/inventory/{sku}`, `GET /api/inventory/low-stock`)
4. Verify or add `GET /api/fulfillment/shipments?orderId={id}` to Fulfillment.Api
5. Configure admin JWT schemes in Orders.Api, Returns.Api, Customer Identity.Api, Correspondence.Api, Fulfillment.Api
6. Integration test: Admin user logs in → JWT accepted by all 5 domain BCs
7. **Success Criteria:** Admin Portal (when built) can authenticate and call all Phase 1 endpoints

**Duration:** 1 cycle (4-5 sessions)

**Then:** M32.0 starts with all prerequisites met

**Status:** ✅ **APPROVED** — M31.5 milestone created. See:
- [M31.5 Milestone Plan](milestones/m31-5-admin-portal-prerequisites.md)
- [Phase 0.5 Implementation Plan](phase-0-5-implementation-plan.md)

### ❌ Option B: M32.0 Includes Phase 0.5 — Monolithic Milestone (NOT CHOSEN)
1. Same 7 steps as Option A
2. Then: Implement Admin Portal BFF (AdminPortal/, AdminPortal.Api/, AdminPortal.Web/)
3. Then: Implement read-only dashboards + CS tooling

**Duration:** 3-4 cycles (Phase 0.5 = 1 cycle, Phase 1 = 2-3 cycles)

**Risk:** Mixing infrastructure work with BFF implementation makes the milestone harder to track

**Status:** ❌ **REJECTED** — Owner chose Option A for cleaner scope separation

---

## Next Steps

**✅ Owner approved Option A (M31.5 separate milestone) on 2026-03-15.**

**M31.5 Implementation (Current):**
1. ✅ Create M31.5 milestone plan document
2. ⏳ PSA and PO review ADR 0032 "Multi-Issuer JWT Strategy"
3. ⏳ Implement 8 endpoint gaps (Customer Identity, Inventory, Fulfillment) + 5 JWT schemes
4. ⏳ Close M31.5

**M32.0 Implementation (After M31.5):**
5. ⏳ Update CURRENT-CYCLE.md to mark M32.0 as "Prerequisites met, ready to start"
6. ⏳ Conduct targeted event modeling session for M32.0 Phase 1
7. ⏳ Implement Admin Portal BFF (AdminPortal/, AdminPortal.Api/, AdminPortal.Web/)
8. ⏳ Close M32.0

---

**Assessment Completed:** 2026-03-15
**Assessed By:** AI Agent (Claude Sonnet 4.5)
**Status:** ✅ **DECISION MADE** — Option A (M31.5 separate milestone) approved
**Owner Decision Date:** 2026-03-15
**Next Milestone:** M31.5 (Admin Portal Prerequisites) — 1 cycle (4-5 sessions)
