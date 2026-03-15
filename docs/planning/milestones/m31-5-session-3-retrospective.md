# M31.5 Session 3 Retrospective: Fulfillment Query + Multi-Issuer JWT (Part 1)

**Date:** 2026-03-15
**Session:** 3 of 5 (M31.5 Multi-Issuer JWT Setup)
**Duration:** ~60 minutes
**Status:** ✅ Complete

---

## Session Objectives

### Primary Goals
1. **Objective 1:** Add Fulfillment shipment query endpoint for Admin Portal (now Backoffice) CS workflow
2. **Objective 2:** Configure multi-issuer JWT authentication in Orders and Returns BCs

### Success Criteria
- [x] `GET /api/fulfillment/shipments?orderId={id}` endpoint created and tested
- [x] Integration tests written (3 test cases covering happy paths and edge cases)
- [x] Orders.Api multi-issuer JWT authentication configured (Admin + Vendor schemes)
- [x] Returns.Api multi-issuer JWT authentication configured (Admin + Vendor schemes)
- [x] Authorization policies added for CustomerService, WarehouseClerk, OperationsManager, VendorAdmin
- [x] All tests passing
- [x] All builds successful
- [x] Changes committed incrementally (4 commits total)

---

## What Was Accomplished

### 1. Fulfillment Shipment Query Endpoint

**File Created:** `src/Fulfillment/Fulfillment.Api/Queries/GetShipmentsForOrder.cs`

**Endpoint:** `GET /api/fulfillment/shipments?orderId={id}`

**Key Design Decisions:**
- **Query parameter for OrderId**: Supports filtering shipments by order (primary CS use case)
- **Returns list**: Supports future split shipments (Phase 1 typically returns 1 shipment per order)
- **Ordered by RequestedAt**: Most recent shipments first (DESC)
- **Leverages snapshot projection**: Queries `Shipment` snapshots directly (no event replay)
- **Empty list for missing orders**: Returns 200 OK with empty array (not 404)

**Response DTO:**
```csharp
public sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    ShipmentStatus Status,
    string? Carrier,
    string? TrackingNumber,
    string? WarehouseId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    string? FailureReason);
```

**Why Important:**
- **CS agent WISMO workflow**: "Where is my order?" tickets require shipment tracking info
- **Backoffice dashboard**: OperationsManager needs fulfillment status visibility
- **Cross-BC composition**: Backoffice queries Orders BC (order details) + Fulfillment BC (shipment tracking)

**Commit:** `(M31.5) Add Fulfillment shipment query endpoint for Backoffice`

### 2. Fulfillment Shipment Query Tests

**File Created:** `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/ShipmentQueryTests.cs`

**Test Cases (3 total):**

1. **`GetShipmentsForOrder_ExistingOrder_ReturnsShipments`**
   **Purpose:** Validate happy path — existing order returns shipment
   **Setup:** Create shipment via `RequestFulfillment` command
   **Assertions:**
   - Shipment count = 1
   - OrderId matches
   - Status = Pending
   - Carrier, TrackingNumber, WarehouseId are null (not yet dispatched)
   - RequestedAt timestamp present

2. **`GetShipmentsForOrder_NonexistentOrder_ReturnsEmptyList`**
   **Purpose:** Validate empty result handling
   **Setup:** Query with non-existent order ID
   **Assertions:**
   - Returns 200 OK (not 404)
   - Response is empty list
   - Count = 0

3. **`GetShipmentsForOrder_DispatchedShipment_ReturnsTrackingInfo`**
   **Purpose:** Validate dispatched shipment includes tracking details
   **Setup:** Create, assign warehouse, dispatch shipment
   **Assertions:**
   - Status = Shipped
   - Carrier = "UPS"
   - TrackingNumber = "1Z999AA10123456789"
   - WarehouseId = "WH-01"
   - DispatchedAt timestamp present

**Test Execution:** All 3 tests passed (19.4 seconds)

**Commit:** `(M31.5) Add integration tests for Fulfillment shipment query`

### 3. Orders.Api Multi-Issuer JWT Configuration

**Files Modified:**
- `src/Orders/Orders.Api/Program.cs` (added authentication and authorization configuration)
- `src/Orders/Orders.Api/Orders.Api.csproj` (added JWT Bearer package reference)

**Authentication Configuration (ADR 0032):**
- **Named scheme "Admin"**: Authority = `https://localhost:5249`, Audience = `https://localhost:5249`
- **Named scheme "Vendor"**: Authority = `https://localhost:5240`, Audience = `https://localhost:5240`
- **RoleClaimType**: Both schemes use `"role"` claim for ASP.NET Core role mapping
- **TokenValidationParameters**: Full validation (issuer, audience, lifetime, signing key)

**Authorization Policies:**
- **CustomerService**: Admin scheme only, roles = CustomerService, OperationsManager, SystemAdmin
- **WarehouseClerk**: Admin scheme only, roles = WarehouseClerk, OperationsManager, SystemAdmin
- **OperationsManager**: Admin scheme only, roles = OperationsManager, SystemAdmin
- **VendorAdmin**: Vendor scheme only, role = VendorAdmin
- **AnyAuthenticated**: Both schemes, any authenticated user

**Middleware Order:**
```csharp
app.UseAuthentication();  // Added before UseAuthorization
app.UseAuthorization();   // Added after UseAuthentication
```

**Package Added:** `Microsoft.AspNetCore.Authentication.JwtBearer` (centrally managed version)

**Commit:** `(M31.5) Configure multi-issuer JWT in Orders.Api - ADR 0032`

### 4. Returns.Api Multi-Issuer JWT Configuration

**Files Modified:**
- `src/Returns/Returns.Api/Program.cs` (added authentication and authorization configuration)
- `src/Returns/Returns.Api/Returns.Api.csproj` (added JWT Bearer package reference)

**Configuration:** Identical to Orders.Api (same schemes, same policies, same middleware order)

**Why Identical:**
- Both BCs need Admin access (CustomerService, OperationsManager workflows)
- Both BCs need Vendor access (future vendor-initiated return workflows)
- Consistency across domain BCs simplifies Backoffice client-side routing

**Commit:** `(M31.5) Configure multi-issuer JWT in Returns.Api - ADR 0032`

---

## Lessons Learned

### What Went Well

1. **Incremental Commits Strategy**
   - **Good:** 4 separate commits for 4 distinct units of work
   - **Pattern:** Objective 1 (2 commits: endpoint + tests), Objective 2 (2 commits: Orders + Returns)
   - **Benefit:** Git history clearly shows Session 3 progress; easy to revert individual changes if needed

2. **ADR 0032 Reference Implementation**
   - **Good:** Followed ADR 0032 pattern exactly (named schemes, policy-based authorization)
   - **Pattern:** Copy-paste from ADR saved 10+ minutes vs. writing from scratch
   - **Benefit:** Configuration consistency across Orders.Api and Returns.Api (identical code)

3. **Package Reference from Directory.Packages.props**
   - **Good:** Central Package Management eliminated version mismatch risk
   - **Pattern:** `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />` (no version attribute)
   - **Benefit:** All BCs use same JWT Bearer version (10.0.3)

4. **Alba Integration Test Pattern (Fulfillment)**
   - **Good:** Followed existing Fulfillment test fixture pattern (ShipmentLifecycleTests.cs)
   - **Pattern:** `_fixture.ExecuteAndWaitAsync()` → `_fixture.Host.Scenario()` → `result.ReadAsJson<T>()`
   - **Benefit:** Tests passed on first run (no test fixture debugging needed)

5. **Query Parameter for OrderId (Not Route Parameter)**
   - **Good:** `?orderId={id}` instead of `/shipments/{orderId}`
   - **Rationale:** Future: add `?customerId={id}` filter, `?status={status}` filter (query composition)
   - **Benefit:** Extensible query design without breaking API compatibility

### What Could Be Improved

1. **Forgot to Add Package Reference Initially**
   - **Issue:** Added JWT configuration to Program.cs, forgot to add package to `.csproj`
   - **Error:** `CS0234: The type or namespace name 'JwtBearer' does not exist`
   - **Fix:** Added `Microsoft.AspNetCore.Authentication.JwtBearer` package reference
   - **Duration:** 2 minutes to diagnose and fix
   - **Prevention:** Skill file reminder — always add package references BEFORE writing code

2. **Syntax Error in Authorization Policies**
   - **Issue:** Used `policy.AuthenticationSchemes.Add("Admin", "Vendor")` (2 arguments)
   - **Error:** `CS1501: No overload for method 'Add' takes 2 arguments`
   - **Fix:** Called `.Add()` twice (once per scheme)
   - **Duration:** 1 minute to diagnose and fix
   - **Prevention:** ADR 0032 example showed shorthand syntax (not real API)

### Technical Debt and Limitations

1. **No JWT Authorization on Endpoints Yet**
   - Endpoints still unauthenticated (Phase 0.5 limitation)
   - Session 4-5 will add `[Authorize]` attributes to Wolverine HTTP endpoints
   - **Risk:** Endpoints temporarily accessible without authentication (acceptable in dev environment)

2. **No Development-Mode HTTPS Workaround**
   - Phase 1 JWT configuration uses `https://localhost:5249` (Admin) and `https://localhost:5240` (Vendor)
   - Development self-signed certificates may cause JWT validation failures
   - **Mitigation (Session 4):** Add `options.RequireHttpsMetadata = false;` for Development environment

3. **Hardcoded Authority URLs**
   - Authority URLs hardcoded in `Program.cs` (not `appsettings.json`)
   - Future improvement: Add `Authentication:Admin:Authority` configuration keys
   - **Mitigation (Phase 2+):** Externalize to config for production deployment

4. **No Audit Logging Yet**
   - JWT authentication configured, but no audit logging of admin actions
   - Session 5 will add audit logging using JWT claims (`sub` = admin user ID)
   - **Risk:** No attribution for CS agent actions in Fulfillment/Orders queries

### Architectural Insights

1. **Named JWT Bearer Schemes vs. Default Scheme**
   - **Pattern:** `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` sets default scheme
   - **Named schemes:** `.AddJwtBearer("Admin", ...)` and `.AddJwtBearer("Vendor", ...)`
   - **Policy binding:** `policy.AuthenticationSchemes.Add("Admin")` binds policy to named scheme
   - **Why:** Default scheme handles unauthenticated requests; named schemes handle policy-specific auth

2. **Policy-Based Authorization vs. Role-Based Authorization**
   - **Old pattern:** `[Authorize(Roles = "CustomerService")]` (single issuer only)
   - **New pattern:** `[Authorize(Policy = "CustomerService")]` (multi-issuer aware)
   - **Benefit:** Policy explicitly specifies which issuer(s) are acceptable (Admin, Vendor, or both)
   - **Critical Rule:** Multi-issuer auth REQUIRES policy-based authorization (roles alone are insufficient)

3. **SystemAdmin Superuser Pattern**
   - **Design:** SystemAdmin role included in ALL admin policies (CustomerService, WarehouseClerk, OperationsManager)
   - **Rationale:** SystemAdmin can perform any admin action (troubleshooting, support escalation)
   - **Benefit:** No need for separate "SystemAdmin" policy (inherits all admin permissions)

4. **Middleware Order: Authentication Before Authorization**
   - **Critical Pattern:** `app.UseAuthentication()` MUST precede `app.UseAuthorization()`
   - **Why:** Authorization middleware checks `HttpContext.User` (populated by authentication middleware)
   - **Error if reversed:** All authorized endpoints return 401 Unauthorized (user never authenticated)

5. **Alba TestServer Limitations with JWT**
   - **Issue:** Alba uses `TestServer` (in-memory HTTP), which bypasses JWT middleware
   - **Implication:** Cannot test JWT authorization with Alba integration tests
   - **Workaround (Session 5):** Use Alba's `HttpContext` builder to inject fake ClaimsPrincipal
   - **Alternative (Phase 2+):** Use WebApplicationFactory with real Kestrel for E2E JWT tests

---

## Issues Encountered

### Issue 1: Missing Package Reference
**Error:** `CS0234: The type or namespace name 'JwtBearer' does not exist`
**Root Cause:** Forgot to add `Microsoft.AspNetCore.Authentication.JwtBearer` to `.csproj`
**Fix:** Added package reference to Orders.Api.csproj and Returns.Api.csproj
**Duration:** 2 minutes
**Prevention:** Always add package references before writing using statements

### Issue 2: AuthenticationSchemes.Add() Syntax Error
**Error:** `CS1501: No overload for method 'Add' takes 2 arguments`
**Root Cause:** ADR 0032 example showed `policy.AuthenticationSchemes.Add("Admin", "Vendor")`
**Fix:** Called `.Add()` twice (once per scheme)
**Duration:** 1 minute
**Prevention:** Verify API syntax against real code (not documentation shorthand)

---

## Risks and Mitigations

### Risk 1: Unauthenticated Endpoints (Phase 0.5)
**Impact:** Medium
**Likelihood:** Accepted (dev environment only)
**Mitigation:** Session 4-5 will add `[Authorize]` attributes before M32.0 Phase 1

### Risk 2: HTTPS Certificate Validation in Development
**Impact:** Medium (JWT validation may fail with self-signed certs)
**Likelihood:** High (default .NET development certificates)
**Mitigation:** Session 4 will add `options.RequireHttpsMetadata = false;` for Development environment

### Risk 3: No Audit Logging (Phase 0.5)
**Impact:** Medium (no attribution for admin actions)
**Likelihood:** Accepted (dev environment only)
**Mitigation:** Session 5 will add audit logging using JWT claims (`sub`)

### Risk 4: Hardcoded Authority URLs
**Impact:** Low (Phase 1 development only)
**Likelihood:** Accepted
**Mitigation:** Phase 2+ will externalize to `appsettings.json` for production

---

## Readiness for Session 4

### Prerequisites Met
- [x] Fulfillment shipment query endpoint implemented and tested
- [x] Orders.Api JWT authentication and authorization configured
- [x] Returns.Api JWT authentication and authorization configured
- [x] All 3 integration tests passing (Fulfillment)
- [x] All builds successful (Orders.Api, Returns.Api)
- [x] Git history clean (4 incremental commits pushed)

### Session 4 Scope: Multi-Issuer JWT (Part 2) — Inventory, Fulfillment, Payments
**Objective:** Configure multi-issuer JWT in remaining domain BCs (Inventory.Api, Fulfillment.Api, Payments.Api)
**Estimated Duration:** 45-60 minutes
**Deliverables:**
- Inventory.Api JWT configuration (Admin + Vendor schemes)
- Fulfillment.Api JWT configuration (Admin + Vendor schemes)
- Payments.Api JWT configuration (Admin only, no Vendor scheme)
- Authorization policies for all three BCs
- Build verification for all three BCs
- Commit incrementally (3 separate commits)

### Session 4 Checklist
- [ ] Configure Admin + Vendor JWT schemes in Inventory.Api Program.cs
- [ ] Configure Admin + Vendor JWT schemes in Fulfillment.Api Program.cs
- [ ] Configure Admin JWT scheme in Payments.Api Program.cs (no Vendor scheme — vendor payments in Phase 2+)
- [ ] Add authorization policies (CustomerService, WarehouseClerk, OperationsManager) to all three BCs
- [ ] Add `options.RequireHttpsMetadata = false;` for Development environment (all BCs)
- [ ] Verify builds for all three BCs
- [ ] Write Session 4 retrospective

---

## Summary

**Session 3 Status:** ✅ Complete (4/4 deliverables)

**Key Achievements:**
1. Fulfillment shipment query endpoint created and tested (3 integration tests, all passing)
2. Orders.Api multi-issuer JWT configured (Admin + Vendor schemes, 5 authorization policies)
3. Returns.Api multi-issuer JWT configured (Admin + Vendor schemes, 5 authorization policies)
4. Incremental commits strategy validated (4 commits, clear git history)

**Blockers:** None

**Next Session:** Multi-Issuer JWT (Part 2) — Inventory, Fulfillment, Payments BCs

**Progress Toward M31.5:** 60% complete (3 of 5 sessions done)
- ✅ Session 1: Pricing bulk update endpoint
- ✅ Session 2: Inventory query endpoints
- ✅ Session 3: Fulfillment query + JWT (Orders, Returns)
- ⏳ Session 4: JWT (Inventory, Fulfillment, Payments)
- ⏳ Session 5: Endpoint authorization + audit logging

**ETA to M32.0 Readiness:** 2-3 hours remaining (Sessions 4-5)
