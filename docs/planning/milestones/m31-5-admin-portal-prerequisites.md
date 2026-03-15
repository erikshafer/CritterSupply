# M31.5: Admin Portal Prerequisites

**Date Started:** 2026-03-15
**Date Completed:** TBD
**Status:** 📋 READY TO START
**Duration Estimate:** 1 cycle (4-5 sessions)
**GitHub Milestone:** M31.5: Admin Portal Prerequisites
**Implementation Branch:** `claude/implement-admin-portal-phase-1`

---

## Executive Summary

M31.5 is a prerequisite milestone that closes **8 integration gaps** blocking M32.0 (Admin Portal Phase 1). This work is infrastructure-focused: adding HTTP endpoints to domain BCs and configuring multi-issuer JWT authentication.

**Why M31.5 Exists:**
During M32.0 prerequisite assessment (2026-03-15), we discovered that domain BCs cannot accept admin JWTs and are missing critical query endpoints. Rather than mixing this infrastructure work with Admin Portal BFF implementation, we've created M31.5 as a separate, focused milestone.

**After M31.5:** M32.0 can proceed with all prerequisites met.

---

## Deliverables

### 1. ADR 0032: Multi-Issuer JWT Strategy ✅ PSA Sign-Off Required

**Status:** ⚠️ Drafted (2026-03-15), awaiting PSA and PO review

**What It Covers:**
- Named JWT Bearer schemes (`"Admin"` from port 5249, `"Vendor"` from port 5240)
- Policy-based authorization mapping
- Phase 1 self-referential audience pattern
- Product Catalog.Api policy rename
- Implementation checklist for 5 domain BCs

**Location:** `docs/decisions/0032-multi-issuer-jwt-strategy.md`

**Success Criteria:** ADR marked as ✅ Accepted

---

### 2. Customer Identity BC — Email Search Endpoint

**Gap:** `GET /api/customers?email={email}` does not exist

**Why Critical:** CS workflow starts with customer email lookup (P0 requirement)

**Implementation:**
- Add `GetCustomerByEmailQuery` handler in `CustomerIdentity.Api/Queries/`
- Return `CustomerView` with Id, Email, FirstName, LastName, CreatedAt
- Add 2 integration tests (existing email, nonexistent email)

**Estimated Effort:** < 1 session

---

### 3. Inventory BC — HTTP Layer

**Gap:** Inventory BC has zero HTTP endpoints (entirely message-driven)

**Why Critical:**
- WarehouseClerk dashboard needs stock levels
- WarehouseClerk alert feed needs low-stock summary
- OperationsManager dashboard needs low-stock KPI

**Implementation:**
1. Verify/create `Inventory.Api` project (Web SDK)
2. Add Marten snapshot projection: `opts.Projections.Snapshot<InventoryItem>(SnapshotLifecycle.Inline);`
3. Add `GET /api/inventory/{sku}` query (stock level)
4. Add `GET /api/inventory/low-stock` query (alert summary)
5. Add `Properties/launchSettings.json` with port 5233
6. Add 2 integration tests

**Estimated Effort:** 1 session (largest gap in M31.5)

---

### 4. Fulfillment BC — Shipment Query Endpoint

**Gap:** Unknown if `GET /api/fulfillment/shipments?orderId={id}` exists

**Why Critical:** CS agents need shipment tracking for WISMO tickets (35-40% of CS volume)

**Implementation:**
1. Search codebase for existing endpoint
2. If missing, add `GetShipmentsForOrderQuery` handler
3. Return list of `ShipmentView` (Id, OrderId, TrackingNumber, Carrier, Status)
4. Add integration test

**Estimated Effort:** < 1 session

---

### 5. Multi-Issuer JWT Configuration (5 Domain BCs)

**Gap:** Domain BCs do not accept admin JWTs

**BCs to Configure:**
1. Orders.Api
2. Returns.Api
3. Customer Identity.Api
4. Correspondence.Api
5. Fulfillment.Api

**Implementation Pattern (apply to all 5):**
```csharp
// Add named JWT Bearer schemes
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Admin", options =>
    {
        options.Authority = "https://localhost:5249";
        options.Audience = "https://localhost:5249";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });

// Add authorization policies
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("CustomerService", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin");
    });
});

// Enable middleware
app.UseAuthentication();
app.UseAuthorization();
```

**Annotate Endpoints:**
```csharp
[Authorize(Policy = "CustomerService")]
```

**Estimated Effort:** 1 session (mostly copy-paste)

---

### 6. Product Catalog.Api — Policy Rename

**Gap:** Existing `"Admin"` policy validates vendor tokens (naming collision)

**Implementation:**
1. Rename `"Admin"` policy to `"VendorAdmin"` in Program.cs
2. Update 3 existing endpoints to `[Authorize(Policy = "VendorAdmin")]`
3. Add new admin policies (`"PricingManager"`, `"CopyWriter"`)
4. Verify existing vendor JWT tests still pass

**Estimated Effort:** < 1 session

---

### 7. Integration Tests

**Goal:** Verify admin JWT acceptance across all 5 domain BCs

**Test Coverage:**
- Admin JWT accepted by Orders.Api, Returns.Api, Customer Identity.Api, Correspondence.Api, Fulfillment.Api
- Vendor JWT rejected by all 5 domain BCs (wrong scheme)
- Cross-issuer `"AnyAuthenticated"` policy accepts both

**Location:** `tests/Admin Identity/AdminIdentity.Api.IntegrationTests/MultiIssuerJwtTests.cs`

**Estimated Effort:** 1 session

---

### 8. Documentation Updates

**Files to Update:**
1. `admin-portal-integration-gap-register.md` — Mark 8 gaps as ✅ Closed
2. `m32-0-prerequisite-assessment.md` — Document owner decision (Option A)
3. `CLAUDE.md` — Add multi-issuer JWT pattern to API Project Configuration
4. `CURRENT-CYCLE.md` — Mark M31.5 as active, M32.0 as blocked
5. `phase-0-5-implementation-plan.md` — Reference M31.5 milestone

**Estimated Effort:** < 1 session

---

## Session-by-Session Plan

### Session 1: ADR Sign-Off + Customer Identity Email Search
- **Duration:** 1-2 hours
- **Deliverables:** ADR 0032 accepted, email search endpoint
- **Commits:**
  1. `(M31.5) ADR 0032 Multi-Issuer JWT Strategy - Accepted`
  2. `(M31.5) Add customer email search endpoint`

### Session 2: Inventory BC HTTP Layer
- **Duration:** 2-3 hours
- **Deliverables:** Inventory.Api with 2 query endpoints
- **Commits:**
  1. `(M31.5) Add Inventory BC HTTP layer`

### Session 3: Fulfillment Query + Multi-Issuer JWT (Part 1)
- **Duration:** 2-3 hours
- **Deliverables:** Shipment query, JWT in Orders + Returns
- **Commits:**
  1. `(M31.5) Verify/add Fulfillment shipment query`
  2. `(M31.5) Configure multi-issuer JWT in Orders and Returns`

### Session 4: Multi-Issuer JWT (Part 2) + Product Catalog
- **Duration:** 2-3 hours
- **Deliverables:** JWT in 3 more BCs, Product Catalog rename
- **Commits:**
  1. `(M31.5) Configure multi-issuer JWT in Customer Identity, Correspondence, Fulfillment`
  2. `(M31.5) Rename Product Catalog Admin policy`

### Session 5: Integration Tests + Documentation
- **Duration:** 2-3 hours
- **Deliverables:** Multi-issuer JWT tests, docs
- **Commits:**
  1. `(M31.5) Add multi-issuer JWT integration tests`
  2. `(M31.5) Update documentation`

---

## Success Criteria

M31.5 is complete when all of the following are true:

1. ✅ ADR 0032 marked as ✅ Accepted
2. ✅ `GET /api/customers?email={email}` exists and tested
3. ✅ `GET /api/inventory/{sku}` exists and tested
4. ✅ `GET /api/inventory/low-stock` exists and tested
5. ✅ `GET /api/fulfillment/shipments?orderId={id}` exists and tested
6. ✅ Admin JWT schemes configured in 5 domain BCs (Orders, Returns, Customer Identity, Correspondence, Fulfillment)
7. ✅ Product Catalog `"Admin"` policy renamed to `"VendorAdmin"`
8. ✅ Integration tests verify admin JWT acceptance
9. ✅ All documentation updated
10. ✅ Solution builds with 0 errors
11. ✅ All tests pass

---

## Dependencies

**Blocks:** M32.0 (Admin Portal Phase 1)

**Blocked By:** None (all prerequisites met)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| ADR 0032 sign-off delayed | PSA can review async; proceed with endpoint work in parallel |
| Inventory BC architecture unclear | Follow Returns/Orders pattern (Marten snapshots + Wolverine HTTP) |
| JWT scheme testing complexity | Use Alba + TestContainers pattern from Vendor Portal tests |
| Product Catalog policy rename breaks vendor tests | Run existing tests first; fix any failures before proceeding |

---

## Related Documents

- [M32.0 Prerequisite Assessment](../m32-0-prerequisite-assessment.md) — Comprehensive analysis leading to M31.5 creation
- [ADR 0032: Multi-Issuer JWT Strategy](../../decisions/0032-multi-issuer-jwt-strategy.md) — Technical implementation pattern
- [Phase 0.5 Implementation Plan](../phase-0-5-implementation-plan.md) — Detailed session-by-session guide
- [Admin Portal Integration Gap Register](../admin-portal-integration-gap-register.md) — Complete gap inventory

---

**Milestone Created:** 2026-03-15
**Created By:** AI Agent (Claude Sonnet 4.5)
**Status:** 📋 READY TO START
**Next Step:** PSA sign-off on ADR 0032, then begin Session 1
