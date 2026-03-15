# Phase 0.5: Admin Portal Domain BC Prerequisites — Implementation Plan

**Date:** 2026-03-15
**Milestone:** Phase 0.5 (prerequisite for M32.0)
**Duration:** 1 cycle (4-5 sessions)
**Status:** 📋 READY TO START (ADR 0032 drafted, gaps documented)

---

## Executive Summary

Phase 0.5 closes **8 integration gaps** that block M32.0 (Admin Portal Phase 1). This work is **infrastructure-focused**: adding HTTP endpoints to domain BCs and configuring multi-issuer JWT authentication. No Admin Portal code is written in this phase.

**Deliverables:**
1. ✅ ADR 0032 "Multi-Issuer JWT Strategy" (drafted, needs PSA sign-off)
2. ⏳ 3 new HTTP query endpoints (Customer Identity, Inventory, Fulfillment)
3. ⏳ Inventory BC HTTP layer (currently has zero endpoints)
4. ⏳ Multi-issuer JWT configuration in 5 domain BCs
5. ⏳ Product Catalog.Api policy rename (`"Admin"` → `"VendorAdmin"`)
6. ⏳ Integration tests verifying admin JWT acceptance

**Success Criteria:**
- Admin user logs into Admin Identity BC → receives JWT with `role: "CustomerService"`
- Admin JWT is accepted by Orders, Returns, Customer Identity, Correspondence, Fulfillment APIs
- All Phase 1-required endpoints exist and are callable with admin JWT

**After Phase 0.5:** M32.0 can proceed with all prerequisites met

---

## Work Breakdown

### Task 1: ADR 0032 Sign-Off (0.5 sessions)

**Status:** ⚠️ Drafted, awaiting review

**Deliverable:** ADR 0032 "Multi-Issuer JWT Strategy" marked as ✅ Accepted

**Checklist:**
- [x] Draft ADR 0032 with named schemes pattern (`"Admin"`, `"Vendor"`)
- [ ] PSA reviews ADR for technical correctness
- [ ] PO reviews ADR for alignment with M32.0 scope
- [ ] Resolve any feedback/questions
- [ ] Mark ADR status as ✅ Accepted
- [ ] Commit ADR to `docs/decisions/0032-multi-issuer-jwt-strategy.md`

**Blocker:** PSA and PO sign-off required before implementation begins

---

### Task 2: Customer Identity BC — Add Email Search (< 1 session)

**Gap:** `GET /api/customers?email={email}` does not exist

**Why It Blocks Phase 1:** CS workflow starts with customer email lookup

**Implementation:**

1. **Add Query Handler:**

```csharp
// File: src/Customer Identity/CustomerIdentity.Api/Queries/GetCustomerByEmail.cs
namespace CustomerIdentity.Api.Queries;

public static class GetCustomerByEmailQuery
{
    [WolverineGet("/api/customers")]
    public static async Task<CustomerView?> Handle(
        string email,
        CustomerIdentityDbContext db,
        CancellationToken ct)
    {
        var customer = await db.Customers
            .Where(c => c.Email == email)
            .Select(c => new CustomerView(
                c.Id,
                c.Email,
                c.FirstName,
                c.LastName,
                c.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);

        return customer;
    }
}

public sealed record CustomerView(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAt
);
```

2. **Add Integration Test:**

```csharp
// File: tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/CustomerQueryTests.cs
public class CustomerQueryTests : IClassFixture<CustomerIdentityTestFixture>
{
    private readonly CustomerIdentityTestFixture _fixture;

    public CustomerQueryTests(CustomerIdentityTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetCustomerByEmail_ExistingEmail_ReturnsCustomer()
    {
        // Arrange
        var customer = await _fixture.CreateCustomer("alice@example.com");

        // Act
        var result = await _fixture.Host
            .GetAsJson<CustomerView>($"/api/customers?email=alice@example.com");

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(customer.Id);
        result.Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task GetCustomerByEmail_NonexistentEmail_ReturnsNull()
    {
        // Act
        var result = await _fixture.Host
            .GetAsJson<CustomerView?>("/api/customers?email=nonexistent@example.com");

        // Assert
        result.ShouldBeNull();
    }
}
```

**Checklist:**
- [ ] Add `GetCustomerByEmailQuery.cs` to `CustomerIdentity.Api/Queries/`
- [ ] Run `dotnet build` to verify compilation
- [ ] Add 2 integration tests to `CustomerIdentity.Api.IntegrationTests/CustomerQueryTests.cs`
- [ ] Run `dotnet test` to verify tests pass
- [ ] Commit with message: `(Phase 0.5) Add customer email search endpoint`

**Estimated Time:** < 1 hour

---

### Task 3: Inventory BC — Add HTTP Layer (1 session)

**Gap:** Inventory BC has zero HTTP endpoints (entirely message-driven)

**Why It Blocks Phase 1:** WarehouseClerk dashboard needs stock queries, low-stock alerts

**Implementation:**

1. **Add Inventory.Api Project (if it doesn't exist):**

```bash
cd /home/runner/work/CritterSupply/CritterSupply/src/Inventory
dotnet new web -n Inventory.Api -o Inventory.Api
dotnet sln add Inventory.Api/Inventory.Api.csproj
```

2. **Add Marten Snapshot Projection for Queryability:**

```csharp
// File: src/Inventory/Inventory.Api/Program.cs
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));
    opts.DatabaseSchemaName = Constants.Inventory.ToLowerInvariant();

    // Snapshot projection for queryability
    opts.Projections.Snapshot<InventoryItem>(SnapshotLifecycle.Inline);
});
```

3. **Add Query Endpoints:**

```csharp
// File: src/Inventory/Inventory.Api/Queries/GetStockLevel.cs
public static class GetStockLevelQuery
{
    [WolverineGet("/api/inventory/{sku}")]
    public static async Task<StockLevelView?> Handle(
        string sku,
        IQuerySession session,
        CancellationToken ct)
    {
        var item = await session.Query<InventoryItem>()
            .FirstOrDefaultAsync(i => i.Sku == sku, ct);

        return item is null ? null : new StockLevelView(
            item.Sku,
            item.QuantityAvailable,
            item.QuantityReserved,
            item.LowStockThreshold,
            item.IsLowStock
        );
    }
}

public sealed record StockLevelView(
    string Sku,
    int QuantityAvailable,
    int QuantityReserved,
    int LowStockThreshold,
    bool IsLowStock
);
```

```csharp
// File: src/Inventory/Inventory.Api/Queries/GetLowStockAlerts.cs
public static class GetLowStockAlertsQuery
{
    [WolverineGet("/api/inventory/low-stock")]
    public static async Task<IReadOnlyList<LowStockAlertView>> Handle(
        IQuerySession session,
        CancellationToken ct)
    {
        var lowStockItems = await session.Query<InventoryItem>()
            .Where(i => i.IsLowStock)
            .OrderBy(i => i.QuantityAvailable)
            .ToListAsync(ct);

        return lowStockItems.Select(i => new LowStockAlertView(
            i.Sku,
            i.QuantityAvailable,
            i.LowStockThreshold,
            i.LastRestockedAt
        )).ToList();
    }
}

public sealed record LowStockAlertView(
    string Sku,
    int QuantityAvailable,
    int LowStockThreshold,
    DateTimeOffset? LastRestockedAt
);
```

4. **Add Properties/launchSettings.json:**

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "profiles": {
    "InventoryApi": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5233",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

5. **Add Integration Tests:**

```csharp
// File: tests/Inventory/Inventory.Api.IntegrationTests/StockQueryTests.cs
public class StockQueryTests : IClassFixture<InventoryTestFixture>
{
    [Fact]
    public async Task GetStockLevel_ExistingSku_ReturnsStock()
    {
        // Test stock query endpoint
    }

    [Fact]
    public async Task GetLowStockAlerts_ReturnsLowStockItems()
    {
        // Test low-stock alert endpoint
    }
}
```

**Checklist:**
- [ ] Verify Inventory.Api project exists (create if missing)
- [ ] Add Marten snapshot projection configuration
- [ ] Add `GetStockLevelQuery.cs` and `GetLowStockAlertsQuery.cs`
- [ ] Add `Properties/launchSettings.json` with port 5233
- [ ] Run `dotnet build` to verify compilation
- [ ] Add 2 integration tests
- [ ] Run `dotnet test` to verify tests pass
- [ ] Commit with message: `(Phase 0.5) Add Inventory BC HTTP layer`

**Estimated Time:** 2-3 hours

---

### Task 4: Fulfillment BC — Verify/Add Shipment Query (< 1 session)

**Gap:** Unknown if `GET /api/fulfillment/shipments?orderId={id}` exists

**Why It Blocks Phase 1:** CS agents need shipment tracking for WISMO tickets (35-40% of CS volume)

**Implementation:**

1. **Verify Existing Endpoint:**

```bash
grep -r "GetShipmentsForOrder\|shipments?orderId" src/Fulfillment/Fulfillment.Api/
```

2. **If Missing, Add Query Handler:**

```csharp
// File: src/Fulfillment/Fulfillment.Api/Queries/GetShipmentsForOrder.cs
public static class GetShipmentsForOrderQuery
{
    [WolverineGet("/api/fulfillment/shipments")]
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
            s.Status.ToString()
        )).ToList();
    }
}

public sealed record ShipmentView(
    Guid Id,
    Guid OrderId,
    string? TrackingNumber,
    string Carrier,
    DateTimeOffset? DispatchedAt,
    string Status
);
```

3. **Add Integration Test:**

```csharp
// File: tests/Fulfillment/Fulfillment.Api.IntegrationTests/ShipmentQueryTests.cs
public class ShipmentQueryTests : IClassFixture<FulfillmentTestFixture>
{
    [Fact]
    public async Task GetShipmentsForOrder_ExistingOrder_ReturnsShipments()
    {
        // Test shipment query endpoint
    }
}
```

**Checklist:**
- [ ] Search Fulfillment.Api for existing shipment query endpoint
- [ ] If missing, add `GetShipmentsForOrderQuery.cs`
- [ ] Run `dotnet build` to verify compilation
- [ ] Add integration test
- [ ] Run `dotnet test` to verify tests pass
- [ ] Commit with message: `(Phase 0.5) Add/verify shipment query endpoint`

**Estimated Time:** < 1 hour

---

### Task 5: Multi-Issuer JWT Configuration (1 session)

**Gap:** 5 domain BCs do not accept admin JWTs

**BCs to Update:**
1. Orders.Api
2. Returns.Api
3. Customer Identity.Api
4. Correspondence.Api
5. Fulfillment.Api

**Implementation Pattern (apply to all 5 BCs):**

```csharp
// Example: src/Orders/Orders.Api/Program.cs

// Add JWT Bearer authentication with named schemes
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

    opts.AddPolicy("WarehouseClerk", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("WarehouseClerk", "OperationsManager", "SystemAdmin");
    });
});

// Enable authentication middleware
app.UseAuthentication();
app.UseAuthorization();
```

**Update Endpoints with Authorization Policies:**

```csharp
// Example: src/Orders/Orders.Api/Commands/CancelOrder.cs
[WolverinePost("/api/orders/{orderId}/cancel")]
[Authorize(Policy = "CustomerService")] // ← ADD THIS
public static async Task<IResult> Handle(...)
```

**Checklist:**
- [ ] Orders.Api: Add `"Admin"` scheme + policies + endpoint annotations
- [ ] Returns.Api: Add `"Admin"` scheme + policies + endpoint annotations
- [ ] Customer Identity.Api: Add `"Admin"` scheme + policies + endpoint annotations
- [ ] Correspondence.Api: Add `"Admin"` scheme + policies + endpoint annotations
- [ ] Fulfillment.Api: Add `"Admin"` scheme + policies + endpoint annotations
- [ ] Run `dotnet build` on all 5 projects
- [ ] Commit with message: `(Phase 0.5) Configure multi-issuer JWT in domain BCs`

**Estimated Time:** 2-3 hours (mostly copy-paste, but must be careful with policy names)

---

### Task 6: Product Catalog.Api Policy Rename (< 1 session)

**Gap:** Product Catalog.Api has 3 endpoints with `[Authorize(Policy = "Admin")]` which validates **vendor** tokens, not admin tokens

**Implementation:**

1. **Rename Existing Policy:**

```csharp
// File: src/Product Catalog/ProductCatalog.Api/Program.cs

// BEFORE:
opts.AddPolicy("Admin", policy =>
{
    policy.RequireRole("VendorAdmin");
});

// AFTER:
opts.AddPolicy("VendorAdmin", policy =>
{
    policy.AuthenticationSchemes.Add("Vendor");
    policy.RequireRole("VendorAdmin");
});
```

2. **Update 3 Existing Endpoints:**

```csharp
// Find all files with [Authorize(Policy = "Admin")]
grep -r "Authorize(Policy = \"Admin\")" src/Product\ Catalog/ProductCatalog.Api/

// Update each to:
[Authorize(Policy = "VendorAdmin")]
```

3. **Add New Admin Policies:**

```csharp
// File: src/Product Catalog/ProductCatalog.Api/Program.cs
builder.Services.AddAuthorization(opts =>
{
    // Vendor policies (renamed)
    opts.AddPolicy("VendorAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireRole("VendorAdmin");
    });

    // Admin policies (new)
    opts.AddPolicy("PricingManager", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("PricingManager", "OperationsManager", "SystemAdmin");
    });

    opts.AddPolicy("CopyWriter", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin");
        policy.RequireRole("CopyWriter", "SystemAdmin");
    });
});
```

4. **Verify Existing Tests Still Pass:**

```bash
dotnet test tests/Product\ Catalog/ProductCatalog.Api.IntegrationTests/
```

**Checklist:**
- [ ] Rename `"Admin"` policy to `"VendorAdmin"` in Program.cs
- [ ] Update 3 existing endpoints to `[Authorize(Policy = "VendorAdmin")]`
- [ ] Add new `"PricingManager"` and `"CopyWriter"` policies
- [ ] Run existing integration tests to ensure vendor JWT auth still works
- [ ] Commit with message: `(Phase 0.5) Rename Product Catalog Admin policy to VendorAdmin`

**Estimated Time:** < 1 hour

---

### Task 7: Integration Tests (1 session)

**Goal:** Verify admin JWT is accepted by all 5 domain BCs

**Test Pattern:**

```csharp
// File: tests/Admin Identity/AdminIdentity.Api.IntegrationTests/MultiIssuerJwtTests.cs
public class MultiIssuerJwtTests : IClassFixture<AdminIdentityTestFixture>
{
    [Fact]
    public async Task AdminJwt_OrdersApi_Accepted()
    {
        // Arrange: Login as admin user
        var loginResult = await _fixture.Host
            .PostJson("/api/admin-identity/auth/login", new LoginRequest("admin@example.com", "password"));

        var jwt = loginResult.AccessToken;

        // Act: Call Orders.Api with admin JWT
        var response = await _ordersClient
            .GetAsync("/api/orders?customerId={guid}", headers => headers.Add("Authorization", $"Bearer {jwt}"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VendorJwt_OrdersApi_Rejected()
    {
        // Arrange: Login as vendor user
        var vendorJwt = await GetVendorJwt();

        // Act: Call Orders.Api with vendor JWT
        var response = await _ordersClient
            .GetAsync("/api/orders?customerId={guid}", headers => headers.Add("Authorization", $"Bearer {vendorJwt}"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

**Checklist:**
- [ ] Add `MultiIssuerJwtTests.cs` to `AdminIdentity.Api.IntegrationTests/`
- [ ] Test admin JWT accepted by Orders.Api, Returns.Api, Customer Identity.Api, Correspondence.Api, Fulfillment.Api
- [ ] Test vendor JWT rejected by all 5 domain BCs (wrong scheme)
- [ ] Run `dotnet test` to verify all tests pass
- [ ] Commit with message: `(Phase 0.5) Add multi-issuer JWT integration tests`

**Estimated Time:** 2-3 hours

---

### Task 8: Documentation Updates (< 1 session)

**Files to Update:**

1. **admin-portal-integration-gap-register.md:**
   - Mark 8 gaps as ✅ Closed
   - Add "Closed Date: 2026-03-XX" column

2. **m32-0-prerequisite-assessment.md:**
   - Mark multi-issuer JWT as ✅ COMPLETE
   - Mark domain BC endpoint gaps as ✅ CLOSED

3. **CLAUDE.md:**
   - Add multi-issuer JWT pattern to "API Project Configuration" section
   - Update port allocation table if Inventory.Api was created

4. **CURRENT-CYCLE.md:**
   - Mark Phase 0.5 as ✅ COMPLETE
   - Update M32.0 status to "Prerequisites met, ready to start"

**Checklist:**
- [ ] Update integration gap register
- [ ] Update prerequisite assessment
- [ ] Update CLAUDE.md
- [ ] Update CURRENT-CYCLE.md
- [ ] Commit with message: `(Phase 0.5) Update documentation after gap closure`

**Estimated Time:** < 1 hour

---

## Session-by-Session Plan

### Session 1: ADR Sign-Off + Customer Identity Email Search
- **Duration:** 1-2 hours
- **Deliverables:** ADR 0032 accepted, email search endpoint added, tests passing
- **Commits:**
  1. `(Phase 0.5) ADR 0032 Multi-Issuer JWT Strategy - Accepted`
  2. `(Phase 0.5) Add customer email search endpoint`

### Session 2: Inventory BC HTTP Layer
- **Duration:** 2-3 hours
- **Deliverables:** Inventory.Api project, snapshot projection, 2 query endpoints, tests
- **Commits:**
  1. `(Phase 0.5) Add Inventory BC HTTP layer with query endpoints`

### Session 3: Fulfillment Query + Multi-Issuer JWT Config (Part 1)
- **Duration:** 2-3 hours
- **Deliverables:** Shipment query endpoint verified/added, JWT config in Orders.Api + Returns.Api
- **Commits:**
  1. `(Phase 0.5) Add/verify Fulfillment shipment query endpoint`
  2. `(Phase 0.5) Configure multi-issuer JWT in Orders and Returns APIs`

### Session 4: Multi-Issuer JWT Config (Part 2) + Product Catalog Rename
- **Duration:** 2-3 hours
- **Deliverables:** JWT config in Customer Identity, Correspondence, Fulfillment; Product Catalog policy rename
- **Commits:**
  1. `(Phase 0.5) Configure multi-issuer JWT in Customer Identity, Correspondence, Fulfillment`
  2. `(Phase 0.5) Rename Product Catalog Admin policy to VendorAdmin`

### Session 5: Integration Tests + Documentation
- **Duration:** 2-3 hours
- **Deliverables:** Multi-issuer JWT tests, documentation updates
- **Commits:**
  1. `(Phase 0.5) Add multi-issuer JWT integration tests`
  2. `(Phase 0.5) Update documentation after gap closure`

---

## Success Criteria

**Phase 0.5 is complete when:**

1. ✅ ADR 0032 is marked as ✅ Accepted
2. ✅ `GET /api/customers?email={email}` exists in Customer Identity.Api
3. ✅ `GET /api/inventory/{sku}` and `GET /api/inventory/low-stock` exist in Inventory.Api
4. ✅ `GET /api/fulfillment/shipments?orderId={id}` exists (verified or added)
5. ✅ Multi-issuer JWT configured in 5 domain BCs (Orders, Returns, Customer Identity, Correspondence, Fulfillment)
6. ✅ Product Catalog.Api `"Admin"` policy renamed to `"VendorAdmin"`
7. ✅ Integration tests verify admin JWT acceptance across all 5 BCs
8. ✅ Documentation updated (gap register, prerequisite assessment, CLAUDE.md, CURRENT-CYCLE.md)
9. ✅ Solution builds with 0 errors
10. ✅ All tests pass

---

## Next Steps After Phase 0.5

**When Phase 0.5 is complete:**
1. Update CURRENT-CYCLE.md to mark M32.0 as "Prerequisites met, ready to start"
2. Conduct targeted event modeling session for M32.0 Phase 1 (Admin Portal BFF)
3. Start M32.0 Phase 1 implementation (Admin Portal domain project, API project, Blazor WASM frontend)

---

**Plan Created:** 2026-03-15
**Created By:** AI Agent (Claude Sonnet 4.5)
**Estimated Duration:** 1 cycle (4-5 sessions, 10-15 hours total)
**Status:** 📋 READY TO START (ADR 0032 drafted, waiting for sign-off)
