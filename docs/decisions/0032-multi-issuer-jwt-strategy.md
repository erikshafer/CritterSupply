# ADR 0032: Multi-Issuer JWT Authentication Strategy

**Status:** ⚠️ Proposed

**Date:** 2026-03-15

**Context:**

CritterSupply now has two JWT-issuing identity BCs:
1. **Vendor Identity BC** (port 5240) — issues JWTs for vendor partner users accessing the Vendor Portal
2. **Admin Identity BC** (port 5249) — issues JWTs for internal employee users accessing the Admin Portal

As of M31.0 completion, domain BCs (Orders, Payments, Inventory, Fulfillment, Returns, Correspondence, Pricing, Product Catalog) do **not** accept admin JWTs. They either:
- Accept vendor JWTs only (Product Catalog.Api — 3 endpoints protected with `[Authorize(Policy = "Admin")]` which validates vendor tokens)
- Have no authentication at all (Orders, Payments, Returns, Inventory, Fulfillment, Correspondence, Pricing)

M32.0 (Admin Portal Phase 1) requires domain BCs to accept admin JWTs for customer service tooling and read-only dashboards. Specifically:

**Admin Portal Phase 1 Endpoints:**
- `GET /api/customers?email={email}` (Customer Identity) — CS customer search
- `GET /api/orders?customerId={id}` (Orders) — CS order lookup
- `POST /api/orders/{id}/cancel` (Orders) — CS order cancellation
- `GET /api/returns?orderId={id}` (Returns) — CS return lookup
- `POST /api/returns/{id}/approve` (Returns) — CS return approval
- `POST /api/returns/{id}/deny` (Returns) — CS return denial
- `GET /api/correspondence/messages/customer/{id}` (Correspondence) — CS message history
- `GET /api/inventory/{sku}` (Inventory) — WH stock queries
- `GET /api/fulfillment/shipments?orderId={id}` (Fulfillment) — CS shipment tracking

Without multi-issuer JWT support, domain BCs will reject admin tokens and M32.0 cannot proceed.

---

## Decision

CritterSupply adopts **named JWT Bearer schemes** for multi-issuer authentication. Each identity BC (Vendor Identity, Admin Identity) has its own named authentication scheme, and domain BC endpoints declare which scheme(s) they accept via policy-based authorization.

### 1. Named Authentication Schemes

Domain BCs configure two named JWT Bearer schemes:

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
    })
    .AddJwtBearer("Vendor", options =>
    {
        options.Authority = "https://localhost:5240"; // Vendor Identity BC
        options.Audience = "https://localhost:5240";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });
```

**Key Design Choices:**
- **Scheme names:** `"Admin"` and `"Vendor"` (not `"AdminJwt"` or `"AdminBearer"` — concise, matches the identity BC name)
- **Separate `options.Authority`:** Each scheme validates tokens from a different issuer
- **Separate `options.Audience`:** Admin tokens have `aud: "https://localhost:5249"`, vendor tokens have `aud: "https://localhost:5240"`
- **Same `RoleClaimType`:** Both identity BCs use the standard `"role"` claim for consistency

### 2. Policy-Based Authorization

Domain BCs define authorization policies that map to authentication schemes and roles:

```csharp
// Example: Orders.Api/Program.cs
builder.Services.AddAuthorization(opts =>
{
    // Admin policies (accept Admin scheme only)
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

    // Vendor policies (accept Vendor scheme only)
    opts.AddPolicy("VendorAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireRole("VendorAdmin");
    });

    // Cross-issuer policies (accept Admin OR Vendor)
    opts.AddPolicy("AnyAuthenticated", policy =>
    {
        policy.AuthenticationSchemes.Add("Admin", "Vendor");
        policy.RequireAuthenticatedUser();
    });
});
```

**Key Design Choices:**
- **Policy names match admin roles:** `"CustomerService"`, `"WarehouseClerk"`, etc. (consistency with ADR 0031)
- **Policy-to-scheme mapping:** Policies explicitly declare which scheme(s) they accept via `AuthenticationSchemes.Add()`
- **SystemAdmin superuser:** Admin policies include `"SystemAdmin"` in `RequireRole()` to enforce the superuser pattern from ADR 0031
- **Cross-issuer policies:** `"AnyAuthenticated"` policy accepts both admin and vendor tokens (useful for shared read endpoints)

### 3. Endpoint Authorization

Handlers annotate endpoints with policy names:

```csharp
// Example: Orders.Api/Queries/GetOrderDetails.cs
public static class GetOrderDetailsQuery
{
    [WolverineGet("/api/orders/{orderId}")]
    [Authorize(Policy = "CustomerService")] // Accepts Admin scheme only
    public static async Task<OrderView?> Handle(
        Guid orderId,
        IQuerySession session,
        CancellationToken ct)
    {
        // ... query logic
    }
}

// Example: Orders.Api/Commands/CancelOrder.cs
public static class CancelOrderHandler
{
    [WolverinePost("/api/orders/{orderId}/cancel")]
    [Authorize(Policy = "CustomerService")] // Accepts Admin scheme only
    public static async Task<IResult> Handle(
        Guid orderId,
        CancelOrderRequest request,
        IMessageBus bus,
        HttpContext context)
    {
        var adminUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var command = new CancelOrder(orderId, adminUserId, request.Reason);
        await bus.InvokeAsync(command);
        return Results.Ok();
    }
}
```

**Key Design Choices:**
- **Policy-based, not role-based:** Use `[Authorize(Policy = "CustomerService")]`, not `[Authorize(Roles = "CustomerService,SystemAdmin")]` (policies encapsulate the SystemAdmin superuser rule)
- **Extracting admin user ID:** Use `context.User.FindFirstValue(ClaimTypes.NameIdentifier)` to get the `sub` claim (admin user ID) for audit trails
- **Request DTOs for admin attribution:** Commands include `AdminUserId` and optional `Reason` fields for audit logging

### 4. Product Catalog Special Case

Product Catalog.Api currently has 3 endpoints protected with `[Authorize(Policy = "Admin")]` which validates **vendor** tokens (not admin tokens). This is a naming collision.

**Resolution:**
1. Rename existing `"Admin"` policy to `"VendorAdmin"` in Product Catalog.Api/Program.cs
2. Update 3 existing endpoints to use `[Authorize(Policy = "VendorAdmin")]`
3. Add new `"CustomerService"` and `"PricingManager"` policies that validate admin tokens
4. New admin-facing endpoints use admin policies

```csharp
// Product Catalog.Api/Program.cs
builder.Services.AddAuthorization(opts =>
{
    // Vendor policies (existing, renamed)
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

### 5. Audience Evolution (Phase 1 vs. Phase 2+)

**Phase 1 (Current Decision):**
- Admin Identity BC issues tokens with `aud: "https://localhost:5249"` (self-referential)
- Domain BCs configure `options.Audience = "https://localhost:5249"`
- **Rationale:** Admin Portal BFF does not exist yet. Admin Identity's own user management endpoints (`/api/admin-identity/users`) are the only protected endpoints. Domain BCs accept these tokens to enable M32.0 Phase 1 (read-only dashboards + CS tooling).

**Phase 2+ (Future Evolution):**
- Admin Portal API (port 5243) is built
- Admin Identity BC should issue tokens with `aud: "https://localhost:5243"` (Admin Portal API audience)
- Domain BCs should configure `options.Audience = "https://localhost:5243"` OR accept multiple audiences
- **Migration Path:** Coordinated update across Admin Identity BC + 9 domain BCs

**Decision:** Defer audience evolution to Phase 2+. Document the limitation in this ADR and plan the migration when Admin Portal API ships.

---

## Rationale

### Why Named Schemes Instead of Single Default Scheme

**Alternative 1: Single default scheme accepting both issuers**
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuers = new[] { "https://localhost:5249", "https://localhost:5240" },
            ValidAudiences = new[] { "https://localhost:5249", "https://localhost:5240" }
        };
    });
```

**Rejected because:**
- ❌ Cannot enforce role-based policies per issuer (admin roles vs. vendor roles have different names)
- ❌ Cannot restrict endpoints to a single issuer (e.g., "this endpoint only accepts admin tokens")
- ❌ SignalR hub groups use role-based routing (`role:CustomerService`) which breaks if admin and vendor roles overlap

**Alternative 2: Dynamic issuer validation in middleware**
```csharp
options.TokenValidationParameters.IssuerValidator = (issuer, securityToken, validationParameters) =>
{
    if (issuer == "https://localhost:5249" || issuer == "https://localhost:5240")
        return issuer;
    throw new SecurityTokenInvalidIssuerException();
};
```

**Rejected because:**
- ❌ More complex than named schemes
- ❌ Still requires per-issuer role mapping logic
- ❌ Harder to test and reason about

**Named schemes solve all problems:**
- ✅ Per-issuer role validation (admin roles vs. vendor roles)
- ✅ Per-endpoint scheme restriction (`[Authorize(Policy = "CustomerService")]` only accepts admin tokens)
- ✅ Clear, testable, follows ASP.NET Core conventions

### Why `RoleClaimType = "role"` for Both Issuers

Both Admin Identity BC and Vendor Identity BC use the standard `"role"` claim (not custom claims like `"admin_role"` or `"vendor_role"`). This enables:
- ✅ `policy.RequireRole("CustomerService")` works without custom claim mapping
- ✅ SignalR hub groups use standard `HttpContext.User.IsInRole()` checks
- ✅ Frontend `AuthorizeView Roles="CustomerService"` components work without custom logic

### Why Self-Referential Audience in Phase 1

Admin Identity BC issues tokens with `aud: "https://localhost:5249"` (its own address) because:
1. Admin Portal API (port 5243) does not exist yet
2. Admin Identity's user management endpoints need protection
3. Domain BCs accept these tokens as a transitional pattern until Admin Portal API ships

**Trade-off:** Domain BCs validate tokens intended for Admin Identity BC, not for themselves. This is acceptable in Phase 1 because:
- All admin tokens go through Admin Identity BC (single issuer, single audience)
- Phase 2 migration path is clear (update audience to Admin Portal API address)
- No security risk (tokens are still validated against Admin Identity's signing key)

---

## Consequences

### Positive

✅ **Domain BCs accept admin JWTs** — Unblocks M32.0 Phase 1 implementation
✅ **Per-issuer role validation** — Admin roles and vendor roles are isolated
✅ **Per-endpoint scheme restriction** — Handlers declare which issuer(s) they accept
✅ **Consistent with ADR 0031** — Policy names match admin roles (CustomerService, PricingManager, etc.)
✅ **Follows ASP.NET Core conventions** — Named schemes are a standard pattern
✅ **Testable** — Integration tests can issue tokens for each scheme and verify policy enforcement
✅ **SignalR-compatible** — Hub groups use standard role claims (`role:CustomerService`)

### Negative

⚠️ **Configuration duplication** — Each domain BC must configure both schemes (8 BCs × 2 schemes = 16 configurations)
⚠️ **Product Catalog policy rename** — Breaking change for existing `"Admin"` policy (must rename to `"VendorAdmin"` and update 3 endpoints)
⚠️ **Audience evolution complexity** — Phase 2 migration requires coordinated update across 9 BCs
⚠️ **Self-referential audience limitation** — Phase 1 tokens are issued for Admin Identity BC, not for domain BCs (transitional pattern only)

### Neutral

🔵 **No shared signing key** — Each identity BC has its own signing key (validated via `options.Authority` discovery)
🔵 **No token refresh coordination** — Each identity BC manages its own refresh tokens independently
🔵 **Customer Identity unchanged** — Customer-facing endpoints continue to use session cookies (not JWTs)

---

## Implementation Checklist

**Phase 0.5: Multi-Issuer JWT Setup (4-5 sessions)**

1. **ADR Sign-Off**
   - [ ] PSA reviews and approves this ADR
   - [ ] PO reviews and confirms alignment with M32.0 scope
   - [ ] Mark ADR status as ✅ Accepted

2. **Domain BC JWT Scheme Configuration (5 BCs)**
   - [ ] Orders.Api — Add `"Admin"` and `"Vendor"` schemes + policies
   - [ ] Returns.Api — Add `"Admin"` and `"Vendor"` schemes + policies
   - [ ] Customer Identity.Api — Add `"Admin"` scheme only (customers use cookies)
   - [ ] Correspondence.Api — Add `"Admin"` scheme only
   - [ ] Fulfillment.Api — Add `"Admin"` scheme only (verify if WISMO queries exist first)

3. **Product Catalog.Api Policy Rename**
   - [ ] Rename `"Admin"` policy to `"VendorAdmin"`
   - [ ] Update 3 existing endpoints to `[Authorize(Policy = "VendorAdmin")]`
   - [ ] Add new `"PricingManager"` and `"CopyWriter"` policies for admin tokens

4. **Integration Testing**
   - [ ] Test admin user login → JWT issued with `iss: "https://localhost:5249"`, `aud: "https://localhost:5249"`, `role: "CustomerService"`
   - [ ] Test Orders.Api endpoint accepts admin JWT and rejects vendor JWT
   - [ ] Test Product Catalog.Api endpoint accepts vendor JWT and rejects admin JWT (existing behavior preserved)
   - [ ] Test cross-issuer policy accepts both admin and vendor JWTs

5. **Documentation Updates**
   - [ ] Update CLAUDE.md "API Project Configuration" section with multi-issuer JWT pattern
   - [ ] Update admin-portal-integration-gap-register.md to mark JWT gaps as closed
   - [ ] Update m32-0-prerequisite-assessment.md to mark multi-issuer JWT as ✅ COMPLETE

---

## References

- [ADR 0028: JWT Bearer Tokens for Vendor Identity](./0028-jwt-for-vendor-identity.md) — Establishes the JWT pattern for Vendor Identity BC
- [ADR 0031: Admin Portal Role-Based Access Control Model](./0031-admin-portal-rbac-model.md) — Defines admin roles and policy-based authorization
- [Admin Portal Integration Gap Register](../planning/admin-portal-integration-gap-register.md) — Documents 8 Phase 0.5 blockers including multi-issuer JWT
- [M32.0 Prerequisite Assessment](../planning/m32-0-prerequisite-assessment.md) — Assesses M32.0 prerequisites and recommends Phase 0.5 implementation

---

**Author:** AI Agent (Claude Sonnet 4.5)
**Review Requested:** Principal Software Architect, Product Owner
**Estimated Review Time:** 15-20 minutes
**Estimated Implementation Time:** 1 session (5 domain BCs × JWT config + Product Catalog rename)
