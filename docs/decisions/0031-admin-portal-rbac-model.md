# ADR 0031: Admin Portal Role-Based Access Control Model

**Status:** ✅ Accepted

> **Note:** "Admin Portal" was renamed to "Backoffice" and "Admin Identity" to "BackofficeIdentity" in [ADR 0033](./0033-admin-portal-to-backoffice-rename.md).

**Date:** 2026-03-13

**Context:**

The Backoffice BC is the gateway for internal employees to manage CritterSupply operations — editing product content, setting prices, adjusting inventory, resolving customer issues, and viewing business intelligence dashboards. Unlike Customer Experience (customer-facing) and Vendor Portal (partner-facing), the Backoffice serves multiple distinct internal personas with varying access needs:

- **CopyWriter** — edits product descriptions and display names
- **PricingManager** — sets and schedules product prices
- **WarehouseClerk** — adjusts inventory, receives stock, acknowledges alerts
- **CustomerService** — searches customers, views/cancels orders, issues credits
- **OperationsManager** — monitors system health, fulfillment, escalates issues
- **Executive** — views aggregated dashboards, exports reports (no PII access)
- **SystemAdmin** — manages admin users, system configuration, full access

Each role requires different data access permissions and command authorization. A consistent RBAC (Role-Based Access Control) model is needed across all domain BCs accessed through the Backoffice to ensure:

1. **Security** — Users can only perform actions appropriate to their role
2. **Audit Trail** — Every domain event records which admin user triggered it
3. **Scalability** — New domain BCs (Promotions, Correspondence, etc.) can adopt the same RBAC pattern without re-inventing authorization
4. **Consistency** — Frontend UI and backend API both enforce the same access rules

**Immediate Trigger:**

Cycle 29, Phase 1 includes Promotions BC implementation. Promotions will be managed via the Backoffice by the `PricingManager` role. Without an established RBAC model, there is no clear pattern for:

- How to annotate Promotions API endpoints with role requirements
- How to extract `adminUserId` from JWT claims for audit trails
- Whether `SystemAdmin` should automatically inherit `PricingManager` permissions
- What JWT claim structure to use (`role` vs `roles` vs custom claims)

This ADR establishes the RBAC model before any Backoffice-managed BC (Promotions, BackofficeIdentity, or future BCs) is implemented.

---

## Decision

CritterSupply adopts a **policy-based authorization model** for the Backoffice, using ASP.NET Core's `AuthorizationPolicy` infrastructure.

### 1. Role Definitions (Phase 1)

| Role | Access Scope | Phase |
|------|--------------|-------|
| `CopyWriter` | Product content (descriptions, names) | Phase 2 |
| `PricingManager` | Product pricing, promotions | Phase 1 (Promotions BC) |
| `WarehouseClerk` | Inventory adjustments, stock receiving | Phase 2 |
| `CustomerService` | Customer lookup, order management, credits | Phase 1 |
| `OperationsManager` | System monitoring, all operational data (no customer PII beyond CS scope) | Phase 1 |
| `Executive` | Aggregated dashboards, report exports (no PII) | Phase 1 |
| `SystemAdmin` | All access, user management | Phase 1 |

**Key Design Rule:** Each user has **exactly one role** (single-role per user, Phase 1 constraint). Multi-role support is deferred to Phase 2+ if needed.

### 2. Authorization Pattern: Policy-Based

**Recommended Pattern:**
```csharp
[Authorize(Policy = "PricingManagerOrAbove")]
[WolverinePost("/api/admin/promotions/coupons")]
public static async Task<IResult> Handle(...)
```

**Policy Registration (Backoffice.Api/Program.cs):**
```csharp
builder.Services.AddAuthorization(opts =>
{
    // Leaf policies (single role, no hierarchy)
    opts.AddPolicy("CopyWriter", policy => policy.RequireRole("CopyWriter", "SystemAdmin"));
    opts.AddPolicy("PricingManager", policy => policy.RequireRole("PricingManager", "SystemAdmin"));
    opts.AddPolicy("WarehouseClerk", policy => policy.RequireRole("WarehouseClerk", "SystemAdmin"));
    opts.AddPolicy("CustomerService", policy => policy.RequireRole("CustomerService", "SystemAdmin"));
    opts.AddPolicy("OperationsManager", policy => policy.RequireRole("OperationsManager", "SystemAdmin"));
    opts.AddPolicy("Executive", policy => policy.RequireRole("Executive", "SystemAdmin"));
    opts.AddPolicy("SystemAdmin", policy => policy.RequireRole("SystemAdmin"));

    // Composite policies (OR logic across multiple roles)
    opts.AddPolicy("PricingManagerOrAbove", policy =>
        policy.RequireRole("PricingManager", "OperationsManager", "SystemAdmin"));

    opts.AddPolicy("CustomerServiceOrAbove", policy =>
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin"));

    opts.AddPolicy("WarehouseOrOperations", policy =>
        policy.RequireRole("WarehouseClerk", "OperationsManager", "SystemAdmin"));
});
```

**Rationale:**

- **`SystemAdmin` is always included** — The `SystemAdmin` role automatically satisfies all policy checks (superuser pattern).
- **Explicit composite policies** — Handlers declare intent via policy name (`"PricingManagerOrAbove"`), not raw role checks in code.
- **Frontend can query policies** — Blazor/React components can check `AuthorizationService.AuthorizeAsync(user, "PricingManager")` to conditionally render UI elements.

**Alternative Rejected: Direct Role Checks**
```csharp
[Authorize(Roles = "PricingManager,SystemAdmin")]  // ❌ Verbose, duplicates superuser logic
```

This forces every handler to remember to include `SystemAdmin`. Policy-based encapsulates the superuser rule once in `Program.cs`.

### 3. JWT Claims Structure

**BackofficeIdentity BC issues JWTs with these claims:**

```json
{
  "sub": "3fa85f64-5717-4562-b3fc-2c963f66afa6",  // Admin user ID (GUID)
  "role": "PricingManager",                        // Single role (Phase 1)
  "name": "Jane Doe",                              // Display name
  "email": "jane.doe@crittersupply.com",           // Email (for audit logs)
  "exp": 1710345600,                               // Expiry (15 minutes from issue)
  "iat": 1710344700,                               // Issued at
  "iss": "https://localhost:5249",                 // BackofficeIdentity BC (issuer)
  "aud": "https://localhost:5249"                  // Phase 1: BackofficeIdentity self-validates its own protected endpoints
}
```

> **Phase 1 vs. Phase 2 audience:** In Phase 1, BackofficeIdentity BC is both the token issuer *and* the audience — its own user-management endpoints (`/api/admin-identity/users`) are protected by the JWTs it issues. When Backoffice API (port 5243) is built in a future cycle, it will configure its own `JwtBearerOptions` to accept tokens with `aud: "https://localhost:5243"`, and BackofficeIdentity will need to issue tokens with that audience. The claim example and validator config below will need updating at that point.

**Key Decisions:**

- **Claim name is `"role"` (singular)** — Matches ASP.NET Core's default `ClaimsIdentity.RoleClaimType`. Authorization policies using `RequireRole("PricingManager")` read the `"role"` claim automatically.
- **Single role per user (Phase 1)** — Multi-role is deferred. If a user needs multiple capabilities, assign the broadest role (`OperationsManager` or `SystemAdmin`).
- **`sub` claim is the admin user ID** — Use `HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)` to extract `adminUserId` for audit trails.

**Token Lifetime:**
- **Access Token:** 15 minutes (short-lived, in Authorization header)
- **Refresh Token:** 7 days (HttpOnly cookie, server-side revocation list)

### 4. BackofficeIdentity BC Requirements

The BackofficeIdentity BC is a **new bounded context** (not a shared service) that issues and validates JWT tokens for admin users.

**Technology Stack:**
- **Persistence:** EF Core + PostgreSQL (`adminidentity` schema)
- **Auth Framework:** ASP.NET Core Identity
- **Password Hashing:** PBKDF2-SHA256 via ASP.NET Core Identity's `PasswordHasher<T>` (100,000 iterations by default). Argon2id would require a custom `IPasswordHasher<T>` implementation and is deferred to Phase 2+ if needed.
- **Port:** `5249` (next available)
- **Database:** `adminidentity` (added to `docker/postgres/create-databases.sh`)

**Endpoints:**
```
POST   /api/admin-identity/auth/login              # Returns access JWT + refresh cookie
POST   /api/admin-identity/auth/refresh            # Issues new access JWT
POST   /api/admin-identity/auth/logout             # Revokes refresh token
GET    /api/admin-identity/users                   # List admin users (SystemAdmin only)
POST   /api/admin-identity/users                   # Create admin user (SystemAdmin only)
PUT    /api/admin-identity/users/{id}/role         # Change user role (SystemAdmin only)
DELETE /api/admin-identity/users/{id}              # Deactivate admin user (SystemAdmin only)
```

**Integration with Backoffice (Phase 2+):**

When Backoffice API (port 5243) is built, it will trust JWTs signed by BackofficeIdentity BC. Its validation is configured via `JwtBearerOptions` using the signing key directly (no OpenID Connect discovery in Phase 1):

```csharp
// Backoffice API (future — Phase 2+)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://localhost:5249",   // BackofficeIdentity BC issuer
            ValidateAudience = true,
            ValidAudience = "https://localhost:5243", // Backoffice API audience (Phase 2+)
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!))
        };
    });
```

> **Phase 1 note:** BackofficeIdentity API validates its own tokens in Phase 1 with `ValidIssuer` and `ValidAudience` both set to its own base URL. Audience alignment between issuer and consumer must be verified before Backoffice API is wired up.

### 5. Audit Trail Pattern

All domain BCs (Promotions, Pricing, Inventory, etc.) must record **which admin user** triggered each mutation.

**Command Pattern:**
```csharp
public sealed record CreatePromotion(
    string Name,
    decimal DiscountPercentage,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid AdminUserId);  // ← Extracted from JWT at handler
```

**Handler Pattern (Backoffice gateway):**
```csharp
[Authorize(Policy = "PricingManagerOrAbove")]
[WolverinePost("/api/admin/promotions")]
public static async Task<IResult> Handle(
    CreatePromotionRequest request,
    HttpContext httpContext,
    IPromotionsClient promotionsClient)
{
    // Extract adminUserId from JWT claims
    var adminUserId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Forward to Promotions BC with adminUserId
    var command = new CreatePromotion(
        request.Name,
        request.DiscountPercentage,
        request.StartsAt,
        request.EndsAt,
        adminUserId);

    var result = await promotionsClient.CreatePromotionAsync(command);
    return Results.Created($"/api/admin/promotions/{result.PromotionId}", result);
}
```

**Domain Event (Promotions BC):**
```csharp
public sealed record PromotionCreated(
    Guid PromotionId,
    string Name,
    decimal DiscountPercentage,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid CreatedByAdminUserId,        // ← Captured in event stream
    DateTimeOffset CreatedAt);
```

**Rationale:**

- **Gateway-level extraction** — Backoffice (BFF) extracts `adminUserId` from JWT, not domain BCs. Domain BCs trust the Backoffice to provide valid `adminUserId` values.
- **Service-to-service auth** — Backoffice → Domain BC calls are trusted (network isolation Phase 1, OAuth 2.0 client credentials Phase 2+).
- **Immutable audit trail** — Event streams contain `adminUserId` in every mutation event. Any admin action can be traced back to a specific user and timestamp.

### 6. Integration with Domain BCs

**Pattern: Backoffice as BFF (Backend-for-Frontend)**

Backoffice does **not** expose domain BC APIs directly. It acts as a gateway that:

1. **Enforces RBAC** — `[Authorize(Policy = "...")]` on every handler
2. **Extracts audit context** — `adminUserId`, `adminRole` from JWT
3. **Routes to domain BCs** — HTTP client calls or Wolverine message publishing
4. **Composes responses** — Fan-out queries across multiple BCs (e.g., dashboard metrics)
5. **Translates errors** — Domain BC error responses → user-friendly problem details

**Service-to-Service Authentication (Backoffice → Domain BCs):**

| Phase | Approach | Notes |
|-------|----------|-------|
| **Phase 1** | Network isolation | Backoffice + Domain BCs run in private network; external access blocked at load balancer |
| **Phase 2** | Shared secret / API key | `X-Admin-Portal-Key: {secret}` header; simple but not ideal for production |
| **Phase 3** | OAuth 2.0 client credentials | Backoffice authenticates as a service principal; domain BCs validate JWT |
| **Production** | mTLS (mutual TLS) | Backoffice presents a client certificate; domain BCs validate it |

**Recommendation:** Phase 1 uses network isolation (simplest). OAuth 2.0 client credentials planned for Phase 2+.

---

## Rationale

**Why Policy-Based Over Role-Based:**

1. **DRY Principle** — `SystemAdmin` superuser logic is defined once in policy registration, not repeated in every `[Authorize]` attribute.
2. **Frontend Integration** — Blazor/React components can query policies (`AuthorizeAsync(user, "PricingManager")`) to conditionally render UI without hardcoding role checks.
3. **Flexibility** — Composite policies (`"PricingManagerOrAbove"`) express intent clearly. Adding a new role to a policy is a one-line change.
4. **Consistency** — Vendor Portal already uses `[Authorize(Policy = "Admin")]` (see `src/Product Catalog/ProductCatalog.Api/Products/AssignProductToVendor.cs:75,152,284`). Backoffice follows the same pattern.

**Why Single Role Per User (Phase 1):**

- **Simplicity** — Multi-role introduces edge cases (stacking, conflict resolution). Single role avoids this complexity.
- **Real-World Alignment** — Most organizations assign one primary role per admin user. If a user needs broader access, promote them to `OperationsManager` or `SystemAdmin`.
- **Upgradeable** — If multi-role is needed later, change claim name to `"roles"` (array) and update policy logic to `RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == "roles" && c.Value.Contains("PricingManager")))`.

**Why JWT Over Session Cookies:**

- **SignalR Compatibility** — SignalR hubs require `[Authorize]` on connection. JWT-based auth works seamlessly with SignalR's `AccessTokenProvider` pattern (see Vendor Portal WASM client).
- **Stateless** — Backoffice can scale horizontally without shared session state.
- **Cross-Origin** — If Backoffice Web (React/Vue) runs on a different origin than Backoffice API, JWT in Authorization header avoids CORS cookie issues.

**Why BackofficeIdentity BC, Not Shared ASP.NET Core Identity:**

- **Bounded Context Clarity** — BackofficeIdentity is a separate domain concern from Customer Identity and Vendor Identity. Mixing them violates bounded context boundaries.
- **Independent Lifecycle** — Admin user lifecycle (onboarding, offboarding, role changes) is different from customer/vendor lifecycle.
- **SSO Roadmap** — Phase 3 will integrate corporate SSO (Azure AD, Okta). Keeping BackofficeIdentity as a BC allows replacing the local store with an OIDC proxy without changing Backoffice code.

---

## Consequences

**Positive:**

- ✅ **Consistent authorization pattern** — All domain BCs managed via Backoffice use the same policy-based RBAC model
- ✅ **Comprehensive audit trail** — Every domain event records which admin user triggered it
- ✅ **Frontend-backend alignment** — UI can query policies to conditionally render admin tools
- ✅ **Scalable** — New domain BCs (Correspondence, Analytics, etc.) adopt the same pattern without reinventing auth
- ✅ **SSO-ready** — Local store (Phase 1) can be replaced with OIDC proxy (Phase 3) without changing Backoffice

**Negative:**

- ⚠️ **Additional BC to scaffold** — BackofficeIdentity BC adds infrastructure (EF Core, migrations, JWT middleware)
- ⚠️ **Policy proliferation risk** — Need discipline to avoid creating too many policies (`PricingManagerOrAbove`, `PricingManagerOrOperations`, etc.). Keep policies minimal.
- ⚠️ **Single role constraint** — Users needing multiple capabilities must be assigned a broader role (`OperationsManager` or `SystemAdmin`). Multi-role support is deferred.

**Mitigation:**

- **BackofficeIdentity BC scaffolding** — Reuse Customer Identity + Vendor Identity patterns exactly (3-file colocation, EF Core, JWT). Expected effort: 0.5 cycles.
- **Policy proliferation** — Document policy design rules in this ADR: Only create composite policies when ≥ 2 handlers need the same role combination.

---

## Alternatives Considered

### Alternative A: Direct Role Checks in `[Authorize]`

```csharp
[Authorize(Roles = "PricingManager,SystemAdmin")]  // ❌ Rejected
```

**Rejected because:**
- Forces every handler to remember to include `SystemAdmin`
- Frontend must hardcode role checks instead of querying policies
- Harder to refactor when role hierarchy changes

---

### Alternative B: Multi-Role JWT Claims (Phase 1)

```json
{
  "roles": ["PricingManager", "WarehouseClerk"]  // ❌ Rejected for Phase 1
}
```

**Rejected because:**
- Adds edge case complexity (what if a user has both `PricingManager` and `WarehouseClerk`? Which takes precedence for dashboard views?)
- Real-world orgs typically assign one primary admin role per user
- Can be added in Phase 2+ if needed without breaking existing code

---

### Alternative C: Shared ASP.NET Core Identity Across All BCs

All admin, customer, and vendor users in one `aspnetusers` table.

**Rejected because:**
- Violates bounded context boundaries (mixing customer lifecycle with admin lifecycle)
- Complicates SSO integration (customers log in via email/password, admins via corporate SSO)
- Creates a single point of failure for authentication across all BCs

---

## References

- **Backoffice Event Modeling:** `docs/planning/admin-portal-event-modeling.md` (role permission matrix lines 95-116)
- **Vendor Portal RBAC Precedent:** `src/Product Catalog/ProductCatalog.Api/Products/AssignProductToVendor.cs` (policy-based `[Authorize(Policy = "Admin")]`)
- **Customer Identity ADR:** `docs/decisions/0002-ef-core-for-customer-identity.md` (EF Core + ASP.NET Core Identity pattern)
- **Vendor Identity ADR:** `docs/decisions/0028-jwt-for-vendor-identity.md` (JWT auth for Blazor WASM)
- **Blazor WASM JWT Skill:** `docs/skills/blazor-wasm-jwt.md` (JWT refresh + SignalR `AccessTokenProvider`)

---

**Implementation Cycles:**

- **Cycle 29, Phase 1:** BackofficeIdentity BC scaffolding + Promotions BC with `PricingManager` RBAC
- **Cycle 30+:** Customer service tooling (`CustomerService` role), warehouse operations (`WarehouseClerk` role), executive dashboards (`Executive` role)

---

**Approval Required:** Product Owner, Principal Architect

**Status:** ✅ **Accepted** — 2026-03-13
