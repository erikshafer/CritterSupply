# Backoffice — Research & Discovery

> **Date:** 2026-03-10
> **Status:** 📋 Research Complete — Ready for ADR Drafting
> **Authors:** Principal Software Architect, Product Owner, UX Engineer
> **Scope:** Technical recommendations, business validation, and UX design for the Backoffice bounded context — spanning BackofficeIdentity BC, AdminPortal BFF, multi-issuer JWT, frontend technology, SignalR hub design, API extension strategy, audit trail pattern, role-based navigation, and phased implementation
> **Prerequisite Reading:** [CONTEXTS.md — Backoffice §](../../CONTEXTS.md), [Backoffice Event Modeling](backoffice-event-modeling.md), [Backoffice UX Research](backoffice-ux-research.md)
> **Companion Documents:** [Backoffice Feature Files](../features/backoffice/), [ADR 0028 (JWT)](../decisions/0028-jwt-for-vendor-identity.md), [ADR 0025 (Blazor WASM Learnings)](../decisions/0025-blazor-wasm-jwt-poc-learnings.md)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [BackofficeIdentity BC Design](#2-backofficeidentity-bc-design)
3. [Multi-Issuer JWT Validation](#3-multi-issuer-jwt-validation)
4. [Frontend Technology](#4-frontend-technology)
5. [SignalR Hub Design](#5-signalr-hub-design)
6. [API Extension Strategy](#6-api-extension-strategy)
7. [Audit Trail Pattern](#7-audit-trail-pattern)
8. [Evolving BC Considerations](#8-evolving-bc-considerations)
9. [Implementation Phasing](#9-implementation-phasing)
10. [ADRs to Draft](#10-adrs-to-draft)
11. [Open Questions for Product Owner](#11-open-questions-for-product-owner)

---

## 1. Executive Summary

The Backoffice bounded context requires 7 interconnected design decisions before implementation begins. This document provides technically grounded recommendations for each, drawing from proven patterns in the Vendor Portal (Blazor WASM + JWT + SignalR), Storefront BFF (SSE + Marten projections), and the existing CONTEXTS.md specification.

**Key recommendations:**

| # | Decision | Recommendation |
|---|----------|---------------|
| 1 | BackofficeIdentity BC | Separate BC, EF Core, mirrors VendorIdentity structure |
| 2 | Multi-issuer JWT | Named JWT Bearer schemes in domain BCs |
| 3 | Frontend | Blazor WASM (consistency over ecosystem breadth) |
| 4 | SignalR hub | Single hub, role-based groups, multi-group membership |
| 5 | API extension | Admin endpoints live in domain BCs; Backoffice is a composing BFF |
| 6 | Audit trail | `adminUserId` from JWT claim, forwarded in command payloads |
| 7 | BC evolution | Typed HTTP clients with interface abstraction; no API versioning yet |

---

## 2. BackofficeIdentity BC Design

### Recommendation: Separate BC — Mirrors VendorIdentity Pattern

BackofficeIdentity should be a **separate bounded context** with its own EF Core DbContext, PostgreSQL schema (`backofficeidentity`), and JWT token issuer. This mirrors the VendorIdentity pattern exactly.

### Why NOT Reuse VendorIdentity?

| Concern | Detail |
|---------|--------|
| **Different trust domains** | Vendor users are external partners. Admin users are employees. Conflating them in the same identity store creates a privilege escalation risk surface. |
| **Different role models** | VendorRole has 3 values (`Admin`, `CatalogManager`, `ReadOnly`). AdminRole has 7 values with fundamentally different permission semantics. |
| **Different lifecycle** | Vendor users are invited by tenant admins with 72-hour expiry tokens. Admin users are provisioned by system administrators with different onboarding flows. |
| **Different audit requirements** | Admin actions on customer data (PII lookups) require GDPR-compliant access logging. Vendor actions do not. |
| **CONTEXTS.md directive** | Line 3726: *"Admin user identity and authentication (Backoffice Identity BC — separate, analogous to Customer Identity and Vendor Identity)"* |

### AdminRole Enum

```csharp
// src/Shared/Messages.Contracts/BackofficeIdentity/AdminRole.cs
namespace Messages.Contracts.BackofficeIdentity;

/// <summary>
/// Roles for internal admin users defining their permissions within the Backoffice.
/// Each role maps to a real job function at CritterSupply — see CONTEXTS.md Backoffice § for permission matrix.
/// </summary>
public enum AdminRole
{
    /// <summary>Product descriptions, marketing text, display names.</summary>
    CopyWriter,

    /// <summary>Set and schedule product prices.</summary>
    PricingManager,

    /// <summary>Adjust stock levels, receive inbound goods.</summary>
    WarehouseClerk,

    /// <summary>Customer lookups, order cancellations, store credit.</summary>
    CustomerService,

    /// <summary>Cross-system dashboard, alert acknowledgement, fulfillment oversight.</summary>
    OperationsManager,

    /// <summary>Read-only strategic dashboards and report exports.</summary>
    Executive,

    /// <summary>Full access: user management, all capabilities.</summary>
    SystemAdmin
}
```

### JWT Claims

BackofficeIdentity issues JWT tokens with these claims — paralleling the VendorIdentity pattern but with admin-specific claim names to prevent cross-context token misuse:

| Claim | Type | Example | Purpose |
|-------|------|---------|---------|
| `AdminUserId` | `Guid` | `a1b2c3d4-...` | Unique admin user identifier |
| `AdminRole` | `string` | `"WarehouseClerk"` | RBAC role (single role per user) |
| `email` | `string` | `"alice@crittersupply.internal"` | User email (standard claim) |
| `role` | `string` | `"WarehouseClerk"` | Standard role claim for ASP.NET Core `[Authorize(Roles = "...")]` |
| `jti` | `string` | `Guid` | Token ID for revocation tracking |

**Why `AdminUserId` instead of `sub`?** Consistency with the established Vendor Portal pattern (`VendorUserId`, `VendorTenantId`). Custom claim names prevent a VendorIdentity JWT from accidentally satisfying an AdminPortal authorization check — the claim names are namespace-disjoint by convention.

```csharp
// Reference pattern — BackofficeIdentity JwtTokenService.CreateAccessToken
var claims = new[]
{
    new Claim("AdminUserId", user.Id.ToString()),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.Role, user.Role.ToString()),
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
};

var token = new JwtSecurityToken(
    issuer: "admin-identity",     // Different from "vendor-identity"
    audience: "backoffice",     // Different from "vendor-portal"
    claims: claims,
    expires: DateTime.UtcNow.AddMinutes(15),
    signingCredentials: credentials);
```

### Token Lifecycle

Same pattern as VendorIdentity (ADR 0028):

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Access token expiry | 15 minutes | Short-lived; limits damage from token leak |
| Refresh token expiry | 7 days | Internal users log in daily; 7-day rolling window |
| Refresh token storage | HttpOnly cookie, `SameSite=Strict` | XSS protection |
| Access token storage (WASM) | In-memory (`AdminAuthState`) | Never localStorage (ADR 0025) |
| Password hashing | Argon2id via `PasswordHasher<T>` | Internal users have higher-value access; Argon2id is appropriate |
| Signing key | Separate key from VendorIdentity | Defense-in-depth: compromised vendor signing key cannot forge admin tokens |

### Project Structure

```
src/
  Backoffice Identity/
    BackofficeIdentity/                    # Domain library (regular SDK)
      BackofficeIdentity.csproj            # References: Messages.Contracts, EF Core
      Identity/
        BackofficeIdentityDbContext.cs      # Schema: "backofficeidentity"
      UserManagement/
        AdminUser.cs                  # Entity: Id, Email, PasswordHash, Role, Status, etc.
        AdminUserStatus.cs            # Enum: Active, Deactivated
        CreateAdminUser.cs            # Command
        CreateAdminUserHandler.cs     # Handler
        CreateAdminUserValidator.cs   # FluentValidation

    BackofficeIdentity.Api/                # API project (Web SDK)
      BackofficeIdentity.Api.csproj
      Program.cs                      # EF Core + Wolverine + JWT issuer
      Auth/
        JwtTokenService.cs            # Issuer: "admin-identity", Audience: "backoffice"
        JwtSettings.cs                # Config DTO
        AdminLogin.cs                 # POST /api/admin-identity/auth/login
        AdminLogout.cs                # POST /api/admin-identity/auth/logout
        AdminRefreshToken.cs          # POST /api/admin-identity/auth/refresh
        BackofficeIdentitySeedData.cs      # Seed: dev users per role
      Properties/
        launchSettings.json           # Port: 5245
      appsettings.json
```

**Port allocation:** `5245` for BackofficeIdentity.Api (next available after Pricing.Api at 5242; AdminPortal.Api is planned for 5243 and AdminPortal.Web for 5244 — see [Appendix A](#appendix-a-port-allocation-updated) for the full table).

### Seed Data

Seven dev users, one per role — enables immediate manual testing of RBAC:

```csharp
// BackofficeIdentitySeedData.cs — development only
private static readonly (string Email, string First, string Last, AdminRole Role)[] SeedUsers =
[
    ("copy@admin.local", "Carol", "Writer", AdminRole.CopyWriter),
    ("pricing@admin.local", "Pete", "Manager", AdminRole.PricingManager),
    ("warehouse@admin.local", "Walter", "Clerk", AdminRole.WarehouseClerk),
    ("cs@admin.local", "Clara", "Service", AdminRole.CustomerService),
    ("ops@admin.local", "Oscar", "Manager", AdminRole.OperationsManager),
    ("exec@admin.local", "Eva", "Executive", AdminRole.Executive),
    ("admin@admin.local", "Sylvia", "Backoffice", AdminRole.SystemAdmin),
];
```

### Integration Messages

BackofficeIdentity publishes lifecycle events to RabbitMQ (same pattern as VendorIdentity):

```csharp
// src/Shared/Messages.Contracts/BackofficeIdentity/AdminUserCreated.cs
public sealed record AdminUserCreated(Guid AdminUserId, string Email, AdminRole Role, DateTimeOffset CreatedAt);

// src/Shared/Messages.Contracts/BackofficeIdentity/AdminUserDeactivated.cs
public sealed record AdminUserDeactivated(Guid AdminUserId, string Reason, DateTimeOffset DeactivatedAt);

// src/Shared/Messages.Contracts/BackofficeIdentity/AdminUserRoleChanged.cs
public sealed record AdminUserRoleChanged(Guid AdminUserId, AdminRole OldRole, AdminRole NewRole, DateTimeOffset ChangedAt);
```

> **No tenant concept.** Unlike VendorIdentity, admin users do not belong to tenants. CritterSupply is a single-organization admin team. This removes the entire TenantManagement vertical slice from the BackofficeIdentity BC.

---

## 3. Multi-Issuer JWT Validation

### The Problem

ProductCatalog.Api currently validates JWT tokens from VendorIdentity (issuer: `vendor-identity`, audience: `vendor-portal`). The existing `[Authorize(Policy = "Backoffice")]` endpoints use VendorRole.Admin.

Backoffice users need to call ProductCatalog for content editing and vendor assignment. But Backoffice uses a DIFFERENT JWT issuer (`admin-identity`, audience: `backoffice`).

**This is a cross-cutting infrastructure concern that affects every domain BC with admin-facing endpoints.**

### Options Evaluated

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A: Named JWT Bearer Schemes** | Domain BCs register multiple `AddJwtBearer("vendor", ...)` / `AddJwtBearer("admin", ...)` schemes; policies select which scheme(s) to accept | Clean separation; each scheme validates independently; ASP.NET Core native | Each domain BC must know about every issuer; config grows as new portals are added |
| **B: Service-to-Service Tokens** | Backoffice.Api obtains a machine-to-machine token (client credentials) to call domain BCs on behalf of admin users | Domain BCs only validate one issuer per caller type; clean trust boundary | Loses `adminUserId` from the JWT claim (must be in request body); extra token exchange hop; more complex |
| **C: True BFF Gateway (Proxy)** | Backoffice.Api proxies ALL calls — domain BCs never see external JWT tokens; Backoffice authenticates the admin user and calls domain BCs with internal service credentials | Domain BCs stay simpler; only trust internal network | Backoffice becomes a bottleneck; every new domain BC endpoint requires a corresponding proxy endpoint; high coupling |
| **D: Shared Signing Key, Different Audiences** | All identity BCs use the same HMAC signing key; domain BCs validate signature but accept multiple audiences | Simple key management | Single key compromise affects all contexts; violates defense-in-depth; audiences provide weak isolation |

### Recommendation: Option A — Named JWT Bearer Schemes

This is the correct choice for a reference architecture because:

1. **It's the ASP.NET Core-native pattern.** Named authentication schemes are a first-class concept.
2. **It preserves claim propagation.** `adminUserId` flows naturally through the JWT — no extra body parameter needed.
3. **It scales.** When Analytics BC or Operations Dashboard needs its own auth, it's just another named scheme.
4. **It's explicit.** Each policy declares exactly which issuers it trusts. No implicit key sharing.

### Reference Pattern: Multi-Scheme JWT in ProductCatalog.Api

```csharp
// ProductCatalog.Api Program.cs — Phase 2 (multi-issuer)
var vendorJwtKey = builder.Configuration["Jwt:Vendor:SigningKey"]
    ?? throw new Exception("Vendor JWT signing key not found");
var adminJwtKey = builder.Configuration["Jwt:Admin:SigningKey"]
    ?? throw new Exception("Admin JWT signing key not found");

builder.Services.AddAuthentication()
    // Vendor Portal JWT scheme
    .AddJwtBearer("Vendor", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "vendor-identity",
            ValidateAudience = true,
            ValidAudience = "vendor-portal",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(vendorJwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    // Backoffice JWT scheme
    .AddJwtBearer("Backoffice", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "admin-identity",
            ValidateAudience = true,
            ValidAudience = "backoffice",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(adminJwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(opts =>
{
    // Vendor Admin: existing policy (now scoped to Vendor scheme)
    opts.AddPolicy("VendorAdmin", policy => policy
        .AddAuthenticationSchemes("Vendor")
        .RequireAuthenticatedUser()
        .RequireRole("Backoffice"));

    // Backoffice: new policies per admin role
    opts.AddPolicy("AdminCopyWriter", policy => policy
        .AddAuthenticationSchemes("Backoffice")
        .RequireAuthenticatedUser()
        .RequireRole("CopyWriter", "SystemAdmin"));

    opts.AddPolicy("AdminPricingManager", policy => policy
        .AddAuthenticationSchemes("Backoffice")
        .RequireAuthenticatedUser()
        .RequireRole("PricingManager", "SystemAdmin"));

    // SystemAdmin always included — they can do everything
    opts.AddPolicy("AdminSystemAdmin", policy => policy
        .AddAuthenticationSchemes("Backoffice")
        .RequireAuthenticatedUser()
        .RequireRole("SystemAdmin"));
});
```

### Migration Path for Existing ProductCatalog.Api `[Authorize(Policy = "Backoffice")]`

The current `[Authorize(Policy = "Backoffice")]` on `AssignProductToVendor.cs` uses the default scheme. When named schemes are introduced:

1. **Rename** the existing policy from `"Backoffice"` to `"VendorAdmin"` to make the trust boundary explicit.
2. **Add** the `"Vendor"` scheme to the renamed policy.
3. **Add** new `"AdminCopyWriter"` and `"AdminSystemAdmin"` policies for the new admin content endpoints.
4. **Update** the three existing `[Authorize(Policy = "Backoffice")]` attributes to `[Authorize(Policy = "VendorAdmin")]`.

This is a **non-breaking change** — VendorPortal.Web tokens continue to work because they're validated by the `"Vendor"` scheme, and the renamed policy still requires `Role == "Backoffice"`.

### Configuration Pattern (appsettings.json)

```json
{
  "Jwt": {
    "Vendor": {
      "SigningKey": "dev-only-vendor-signing-key-change-in-production-must-be-at-least-32-chars",
      "Issuer": "vendor-identity",
      "Audience": "vendor-portal"
    },
    "Backoffice": {
      "SigningKey": "dev-only-admin-signing-key-change-in-production-must-be-at-least-32-chars",
      "Issuer": "admin-identity",
      "Audience": "backoffice"
    }
  }
}
```

### Which Domain BCs Need Multi-Scheme JWT?

| Domain BC | Vendor Scheme Needed? | Admin Scheme Needed? | Admin Roles |
|-----------|----------------------|---------------------|-------------|
| Product Catalog | ✅ (vendor assignment) | ✅ (content editing) | CopyWriter, SystemAdmin |
| Pricing | ❌ | ✅ (set/schedule price) | PricingManager, SystemAdmin |
| Inventory | ❌ | ✅ (adjust, replenish, alerts) | WarehouseClerk, OperationsManager, SystemAdmin |
| Orders | ❌ | ✅ (cancel order) | CustomerService, OperationsManager, SystemAdmin |
| Customer Identity | ❌ | ✅ (customer lookup) | CustomerService, OperationsManager, SystemAdmin |
| Payments | ❌ | ✅ (read-only: payment history) | CustomerService, OperationsManager, SystemAdmin |

**Note:** BCs that don't currently have ANY auth (Orders, Inventory, Payments, Fulfillment) will need JWT validation added. This is addressed in [§6 API Extension Strategy](#6-api-extension-strategy).

---

## 4. Frontend Technology

### Recommendation: Blazor WASM

**Commit to Blazor WASM for Backoffice**, diverging from the original event modeling doc's React/Next.js recommendation. Here's why:

### Decision Matrix

| Factor | Blazor WASM | React (Next.js) | Weight |
|--------|-------------|-----------------|--------|
| **Team velocity** | High — patterns proven in Vendor Portal | Low — new toolchain, no existing patterns | ⭐⭐⭐ |
| **Code reuse** | High — AuthState, AuthStateProvider, HttpClient patterns, MudBlazor components, SignalR client all transfer directly | None — full rewrite of auth, HTTP, SignalR, UI | ⭐⭐⭐ |
| **Reference architecture coherence** | High — "here's how to build a BFF + WASM portal" is one story | Low — "here's how to integrate React with a .NET SignalR backend" is a different reference architecture | ⭐⭐ |
| **Data tables / charts** | MudBlazor DataGrid + MudChart cover 90% of admin needs; Blazorise or Radzen for the rest | React-Table + Recharts are more mature and flexible for complex visualizations | ⭐⭐ |
| **SSR / initial load** | WASM has no SSR (payload ~5MB, first load ~2-3s) | Next.js SSR gives meaningful content on first paint | ⭐ |
| **Desktop-only context** | Internal tool, managed desktops, Chrome/Edge — WASM payload is cached after first load | SSR matters more for public-facing or mobile-first apps | ⭐ |
| **Maintenance cost** | C# end-to-end — one language, one toolchain | JS/TS frontend + C# backend — two build systems, two dependency trees | ⭐⭐ |

**Total score: Blazor WASM wins 5-2** (weighted by practical impact on delivery timeline).

### Addressing the Original React Recommendation

The event modeling doc (2026-03-07) recommended React/Next.js because:

> *"This context is also an opportunity to explore non-Blazor frontend technology"*

This is a valid learning goal, but it conflicts with the primary goal of **delivering operational value**. The Backoffice's role-scoped dashboards, real-time SignalR alerts, and RBAC-gated mutations are complex enough without simultaneously learning a new frontend framework.

**Counter-proposal:** If the team wants a React reference, the **Operations Dashboard BC** (CONTEXTS.md line 3904) is a better candidate. It's a developer-facing tool with heavy chart/visualization needs (d3.js, event stream rendering) where React's ecosystem genuinely shines, and it has no RBAC complexity.

### What Blazor WASM Reuses from Vendor Portal

| Component | Vendor Portal Source | Backoffice Target | Changes |
|-----------|---------------------|---------------------|---------|
| `VendorAuthState.cs` | In-memory JWT storage | `AdminAuthState.cs` | Replace VendorTenantId with AdminRole-based properties; remove tenant concept |
| `VendorAuthStateProvider.cs` | Custom AuthenticationStateProvider | `AdminAuthStateProvider.cs` | Identical pattern; swap claim names |
| `VendorAuthService.cs` | Login/logout/refresh HTTP calls | `AdminAuthService.cs` | Point to BackofficeIdentity.Api endpoints |
| `TokenRefreshService.cs` | Background timer-based refresh | Same class, reused | Identical logic (15-min access token pattern) |
| `VendorHubService.cs` | SignalR connection management | `AdminHubService.cs` | Point to `/hub/admin`; role-based group messages |
| `Program.cs` WASM setup | Named HttpClients, MudBlazor, auth | Same pattern | Different API URLs, different auth state type |

### MudBlazor Adequacy for Admin UI

MudBlazor (v9.1.0, already in `Directory.Packages.props`) provides:

| Admin Need | MudBlazor Component | Adequate? |
|------------|---------------------|-----------|
| Data tables with sort/filter/paginate | `MudDataGrid<T>` | ✅ Excellent |
| KPI metric cards | `MudPaper` + `MudText` | ✅ Good (proven in Vendor Portal) |
| Real-time alert badges | `MudBadge` + `MudIcon` | ✅ Good |
| Price history charts | `MudChart` (line, bar) | 🟡 Basic — adequate for Phase 1/2 |
| Role-filtered sidebar | `MudNavMenu` + `AuthorizeView` | ✅ Excellent |
| Form inputs for mutations | `MudTextField`, `MudNumericField`, `MudDatePicker` | ✅ Excellent |
| CSV export | Browser download via `IJSRuntime` | ✅ Standard pattern |

**Phase 3 risk:** If executive dashboards require complex interactive charts (drill-down, real-time streaming charts), MudChart may be insufficient. **Mitigation:** Evaluate Blazorise Charts or a JS interop wrapper for Recharts/Chart.js at that point. This is a Phase 3 concern, not a blocking Phase 1 decision.

---

## 5. SignalR Hub Design

### Recommendation: Single Hub, Role-Based Groups, Multi-Group Membership

```csharp
// AdminPortal.Api/Hubs/AdminPortalHub.cs
[Authorize(AuthenticationSchemes = "Backoffice")]
public sealed class AdminPortalHub : Hub
{
    private readonly ILogger<AdminPortalHub> _logger;

    // Role-to-group mapping: which SignalR groups each role joins.
    //
    // OperationsManager has supervisory visibility into warehouse, customer service,
    // and executive metrics — they are the "air traffic controller" who needs to see
    // cross-functional alerts to coordinate responses. They don't need copywriter/pricing
    // visibility because content and pricing changes are not time-sensitive operational concerns.
    //
    // SystemAdmin joins ALL groups programmatically (computed below, not hardcoded)
    // to avoid maintenance burden when new roles are added.
    private static readonly Dictionary<string, string[]> RoleGroupMappings;

    static AdminPortalHub()
    {
        var baseRoleMappings = new Dictionary<string, string[]>
        {
            ["CopyWriter"] = ["role:copywriter"],
            ["PricingManager"] = ["role:pricingmanager"],
            ["WarehouseClerk"] = ["role:warehouseclerk"],
            ["CustomerService"] = ["role:customerservice"],
            ["OperationsManager"] = ["role:operations", "role:executive", "role:warehouseclerk", "role:customerservice"],
            ["Executive"] = ["role:executive"],
        };

        // SystemAdmin inherits ALL role groups — computed to stay in sync automatically
        var allGroups = baseRoleMappings.Values.SelectMany(g => g).Distinct().ToArray();
        baseRoleMappings["SystemAdmin"] = allGroups;

        RoleGroupMappings = baseRoleMappings;
    }

    public AdminPortalHub(ILogger<AdminPortalHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var adminUserId = Context.User?.FindFirst("AdminUserId")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (adminUserId is null || role is null)
        {
            _logger.LogWarning("Admin hub connection rejected: missing AdminUserId or Role claims");
            Context.Abort();
            return;
        }

        // Always join user-specific group (for targeted messages like ForceLogout)
        await Groups.AddToGroupAsync(Context.ConnectionId, $"admin-user:{adminUserId}");

        // Join role-based groups (including inherited groups for supervisory roles)
        if (RoleGroupMappings.TryGetValue(role, out var groups))
        {
            foreach (var group in groups)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, group);
            }
        }

        _logger.LogInformation("Admin hub connected: user={AdminUserId} role={Role} groups={Groups}",
            adminUserId, role, string.Join(", ", RoleGroupMappings.GetValueOrDefault(role, [])));

        await base.OnConnectedAsync();
    }
}
```

### Why Single Hub?

| Option | Pros | Cons |
|--------|------|------|
| **Single hub** (`/hub/admin`) | One connection per client; simpler auth; group routing handles isolation; proven in Vendor Portal | All message types flow through one hub |
| **Multiple hubs** (`/hub/admin/operations`, `/hub/admin/inventory`, etc.) | Stronger type safety per hub | Multiple WebSocket connections per client; more complex client-side management; each hub needs separate auth; Wolverine `opts.UseSignalR()` complexity increases |

**Single hub wins** because:
- Vendor Portal's single `VendorPortalHub` works well with dual groups
- Wolverine's `opts.UseSignalR()` publishes to hub groups, not to specific hubs
- Admin client only maintains one WebSocket connection (important for mobile/tablet warehouse devices)
- Role-based group membership handles message routing cleanly

### Role Overlap Strategy

The key insight is that **OperationsManager and SystemAdmin have supervisory visibility** — they need to see alerts from multiple role groups. The `RoleGroupMappings` dictionary makes this explicit:

```
OperationsManager joins: role:operations, role:executive, role:warehouseclerk, role:customerservice
SystemAdmin joins: ALL role groups
```

This means when a `LowStockAlertRaised` message is sent to `role:warehouseclerk`, both WarehouseClerk users AND OperationsManager users receive it — because OperationsManager is a member of that group.

### SignalR Message Interfaces

Following the Vendor Portal pattern (`IVendorTenantMessage`, `IVendorUserMessage`), define marker interfaces for the Backoffice:

```csharp
// AdminPortal/RealTime/IAdminRoleMessage.cs
namespace AdminPortal.RealTime;

/// <summary>
/// Marker for SignalR messages routed to one or more role-scoped hub groups.
/// The TargetGroups property specifies which groups receive this message.
/// </summary>
public interface IAdminRoleMessage
{
    /// <summary>Hub group names this message should be sent to (e.g., "role:operations", "role:executive").</summary>
    IReadOnlyList<string> TargetGroups { get; }
}

// AdminPortal/RealTime/IAdminUserMessage.cs
namespace AdminPortal.RealTime;

/// <summary>
/// Marker for SignalR messages routed to a specific admin user (e.g., ForceLogout).
/// </summary>
public interface IAdminUserMessage
{
    Guid AdminUserId { get; }
}
```

### Hub Message Examples

```csharp
// AdminPortal/RealTime/AdminHubMessages.cs
namespace AdminPortal.RealTime;

/// <summary>
/// Live metric update for executive and operations dashboards.
/// Pushed when OrderPlaced, OrderCancelled, or PaymentCaptured events are received.
/// </summary>
public sealed record LiveMetricUpdated(
    string MetricName,  // "revenue_today", "orders_today", "active_orders"
    decimal Value,
    DateTimeOffset UpdatedAt) : IAdminRoleMessage
{
    public IReadOnlyList<string> TargetGroups { get; } = ["role:executive", "role:operations"];
}

/// <summary>
/// Severity-tagged alert for operations. Examples: payment failure spike, stockout, delivery failure.
/// </summary>
public sealed record AlertRaised(
    string AlertType,   // "PaymentFailure", "StockOut", "DeliveryFailed", "RefundFailed"
    string Severity,    // "Info", "Warning", "Critical"
    string Message,
    DateTimeOffset RaisedAt,
    IReadOnlyList<string> TargetGroups) : IAdminRoleMessage;

/// <summary>
/// Low-stock alert for warehouse and operations roles.
/// </summary>
public sealed record AdminLowStockAlertRaised(
    string Sku,
    string WarehouseId,
    int CurrentQuantity,
    int ThresholdQuantity,
    DateTimeOffset DetectedAt) : IAdminRoleMessage
{
    public IReadOnlyList<string> TargetGroups { get; } = ["role:warehouseclerk", "role:operations"];
}
```

### Wolverine SignalR Transport Configuration

```csharp
// AdminPortal.Api Program.cs — SignalR routing
opts.UseSignalR();

opts.Publish(x =>
{
    x.MessagesImplementing<IAdminRoleMessage>();
    x.ToSignalR();
});

opts.Publish(x =>
{
    x.MessagesImplementing<IAdminUserMessage>();
    x.ToSignalR();
});
```

> **Note on Wolverine SignalR routing:** Wolverine's `ToSignalR()` currently uses the `IHubContext<Hub>` to send to groups. The `IAdminRoleMessage.TargetGroups` approach requires a custom dispatcher because Wolverine's built-in SignalR transport derives a single group name from a single property (as in `IVendorTenantMessage`).
>
> **Recommended resolution (Phase 1 spike):** Create a Wolverine handler that consumes `IAdminRoleMessage` and manually iterates `TargetGroups`, calling `IHubContext<AdminPortalHub>.Clients.Group(group).SendAsync(...)` for each. This is a 20-line handler, not a framework extension. If performance becomes a concern (many groups per message), evaluate `ISignalRGroupResolver` in a later phase.

---

## 6. API Extension Strategy

### Recommendation: Admin Endpoints Live in Domain BCs

Admin-facing endpoints should live **inside the domain BC APIs**, not in AdminPortal.Api. AdminPortal.Api is a composing BFF that calls domain BC endpoints — it does not proxy raw requests.

### Why Endpoints in Domain BCs?

| Factor | Endpoints in Domain BC | Endpoints in Backoffice BFF |
|--------|----------------------|------------------------------|
| **Proximity to business logic** | ✅ Handler is next to the aggregate/projection it operates on | ❌ BFF must serialize command, send HTTP, domain BC deserializes again |
| **Wolverine integration** | ✅ Direct access to Marten sessions, aggregate handlers, `[WriteAggregate]` | ❌ BFF cannot use Wolverine aggregate workflows for remote BCs |
| **Existing pattern** | ✅ ProductCatalog.Api already has `[Authorize(Policy = "Backoffice")]` endpoints | — |
| **Team scaling** | ✅ Domain team owns their admin endpoints alongside their core API | ❌ All admin endpoints funneled through one team/project |
| **Testing** | ✅ Alba integration tests in domain BC test project | ❌ BFF tests require mocking or running full domain BC |

### What Backoffice BFF Does (and Doesn't Do)

**AdminPortal.Api DOES:**
- Authenticate the admin user (JWT Bearer from BackofficeIdentity)
- Authorize based on admin role (RBAC policies)
- Compose multi-BC queries (e.g., customer lookup fans out to CustomerIdentity + Orders)
- Transform responses into admin-specific view models
- Subscribe to RabbitMQ integration events and push via SignalR
- Validate admin-facing input before forwarding to domain BCs
- Translate domain BC error responses into admin-friendly problem details

**AdminPortal.Api DOES NOT:**
- Execute business logic (that's the domain BC's job)
- Store transactional data (only lightweight Marten projections for dashboard metrics)
- Proxy raw requests (it composes and translates)

### Adding Auth to Currently Unauthenticated BCs

Orders, Inventory, Payments, and Fulfillment APIs currently have **no authentication**. Adding admin auth must not break internal service-to-service calls (e.g., the Order saga calling Inventory for stock reservation).

**Strategy: Auth Required for Admin Endpoints Only**

```csharp
// Inventory.Api Program.cs — Phase 2 (when admin endpoints are added)

// Add JWT validation for Backoffice only
var adminJwtKey = builder.Configuration["Jwt:Admin:SigningKey"]
    ?? "dev-only-admin-signing-key-change-in-production-must-be-at-least-32-chars";

builder.Services.AddAuthentication()
    .AddJwtBearer("Backoffice", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "admin-identity",
            ValidateAudience = true,
            ValidAudience = "backoffice",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(adminJwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminWarehouseClerk", policy => policy
        .AddAuthenticationSchemes("Backoffice")
        .RequireAuthenticatedUser()
        .RequireRole("WarehouseClerk", "OperationsManager", "SystemAdmin"));
});

// Existing internal endpoints remain unauthenticated.
// New admin endpoints use [Authorize(Policy = "AdminWarehouseClerk")].
// Wolverine message handlers (saga callbacks) are never HTTP — they bypass auth entirely.
```

**Key insight:** Wolverine message handlers (RabbitMQ subscribers, saga callbacks) bypass HTTP authentication entirely — they're invoked by the message transport, not by HTTP requests. Only HTTP endpoints (`[WolverineGet]`, `[WolverinePost]`) need auth attributes. This means adding `[Authorize]` to new admin endpoints does NOT affect existing internal message flows.

### Admin Endpoints Per Domain BC (Phase 2)

| Domain BC | New Admin Endpoints | Protected By |
|-----------|-------------------|--------------|
| **Product Catalog** | `PUT /api/catalog/products/{sku}/description` (new) | AdminCopyWriter |
| **Product Catalog** | Existing vendor-assignment endpoints (rename policy) | VendorAdmin |
| **Pricing** | `PUT /api/pricing/products/{sku}/price` | AdminPricingManager |
| **Pricing** | `POST /api/pricing/products/{sku}/price/schedule` | AdminPricingManager |
| **Pricing** | `DELETE /api/pricing/products/{sku}/price/schedule/{scheduleId}` | AdminPricingManager |
| **Inventory** | `POST /api/inventory/{sku}/adjust` | AdminWarehouseClerk |
| **Inventory** | `POST /api/inventory/{sku}/replenish` | AdminWarehouseClerk |
| **Inventory** | `POST /api/inventory/alerts/{alertId}/acknowledge` | AdminWarehouseClerk |
| **Orders** | `POST /api/orders/{orderId}/cancel` (admin variant) | AdminCustomerService |
| **Customer Identity** | `GET /api/identity/customers?email={email}` (admin search) | AdminCustomerService |

### Backoffice BFF Composition Examples

```csharp
// AdminPortal.Api/Queries/GetCustomerServiceView.cs
// This is the BFF composition — fans out to multiple domain BCs

[Authorize(Policy = "RequireCustomerService")]
[WolverineGet("/api/admin/customers")]
public static async Task<IResult> Handle(
    [FromQuery] string email,
    ICustomerIdentityAdminClient customerClient,
    IOrdersAdminClient ordersClient,
    CancellationToken ct)
{
    // Fan-out: parallel calls to two domain BCs
    var customerTask = customerClient.SearchByEmailAsync(email, ct);
    var ordersTask = Task.CompletedTask; // defer until we have customerId

    var customer = await customerTask;
    if (customer is null)
        return Results.NotFound();

    var recentOrders = await ordersClient.GetRecentOrdersAsync(customer.CustomerId, limit: 10, ct);

    // Compose into admin-specific view model
    return Results.Ok(new CustomerServiceView(
        CustomerId: customer.CustomerId,
        Email: customer.Email,
        FirstName: customer.FirstName,
        LastName: customer.LastName,
        RecentOrders: recentOrders));
}
```

---

## 7. Audit Trail Pattern

### Recommendation: `AdminUserId` from JWT Claim, Forwarded in Command Payloads

The audit trail pattern has two parts:

1. **Authentication layer** (AdminPortal.Api): Extracts `AdminUserId` from the verified JWT claim.
2. **Command forwarding**: Includes `adminUserId` in every mutating request body sent to domain BCs.
3. **Domain BC**: Records `adminUserId` in the domain event, making the event stream the audit trail.

### Pattern: JWT Claim → Command Body → Domain Event

```
Admin User (Browser)
  │ JWT Bearer: { AdminUserId: "abc-123", Role: "WarehouseClerk" }
  ▼
AdminPortal.Api  [Authorize(Policy = "RequireWarehouseClerk")]
  │ Extract AdminUserId from HttpContext.User.FindFirst("AdminUserId")
  │ Include in request body to domain BC
  ▼
Inventory.Api  POST /api/inventory/{sku}/adjust
  Body: { warehouseId: "WH-01", quantity: -3, reason: "DamagedGoods", adminUserId: "abc-123" }
  │ Handler records adminUserId in domain event
  ▼
Marten Event Store:
  InventoryAdjusted { Sku, WarehouseId, Quantity, Reason, AdminUserId, Timestamp }
```

### Why Not Propagate the JWT to Domain BCs Directly?

The Backoffice BFF **does** propagate the JWT to domain BCs (via the `Authorization: Bearer` header on HTTP client calls). Domain BCs validate the JWT and can extract `AdminUserId` from it. However, **the command body should ALSO include `adminUserId`** because:

1. **Explicit over implicit:** The domain event needs `adminUserId` in its data. Extracting it from `HttpContext.User` in every handler is repetitive. Including it in the command body makes the audit attribution part of the business contract.
2. **Wolverine handler purity:** If the command includes `adminUserId`, the handler is a pure function of its inputs. If the handler must reach into `HttpContext.User`, it has a hidden dependency.
3. **Message replay:** If domain events are replayed or commands are retried from a queue, `HttpContext` won't exist. Having `adminUserId` in the command body ensures audit attribution survives message replay.

### Enforcing "No Admin Action Without Attribution"

AdminPortal.Api validates that every outbound mutating request includes a non-empty `adminUserId`:

```csharp
// AdminPortal/Validation/AdminCommandValidator.cs
// Base validator applied to all admin commands forwarded to domain BCs

public abstract class AdminCommandValidator<T> : AbstractValidator<T> where T : IAdminCommand
{
    protected AdminCommandValidator()
    {
        RuleFor(x => x.AdminUserId)
            .NotEmpty()
            .WithMessage("AdminUserId is required for audit trail");
    }
}

// Marker interface for commands that require admin attribution
public interface IAdminCommand
{
    Guid AdminUserId { get; }
}
```

### PII Access Logging (Customer Service Endpoints)

CONTEXTS.md line 3718: *"PII accessed via Backoffice (customer emails, addresses) must be logged per access for GDPR compliance audit."*

For customer service read endpoints (not mutations), there's no domain event to carry the audit trail. Instead, use structured logging in the BFF:

```csharp
// AdminPortal.Api — PII access logging middleware (applied to customer-facing queries)
[Authorize(Policy = "RequireCustomerService")]
[WolverineGet("/api/admin/customers")]
public static async Task<IResult> Handle(
    [FromQuery] string email,
    HttpContext httpContext,
    ICustomerIdentityAdminClient client,
    ILogger<GetCustomerServiceView> logger,
    CancellationToken ct)
{
    var adminUserId = httpContext.User.FindFirst("AdminUserId")?.Value;

    // GDPR audit: log PII access with admin attribution.
    // HashForLog uses SHA-256 to enable log correlation without storing plaintext PII in logs.
    // Implementation: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12]
    logger.LogInformation("PII_ACCESS: AdminUser={AdminUserId} accessed customer by email={EmailHash}",
        adminUserId, HashForLog(email));

    var customer = await client.SearchByEmailAsync(email, ct);
    // ...
}
```

---

## 8. Evolving BC Considerations

### Product Catalog Evolution: Document Store → Event Sourcing

CONTEXTS.md documents that Product Catalog is planned to evolve from Marten document store to event sourcing. This means the API surface will change — products will be managed via event-sourced aggregates instead of CRUD document operations.

### Recommendation: Interface-Based HTTP Clients in Backoffice

Backoffice should access domain BCs through **typed HTTP client interfaces** (the same pattern already defined in CONTEXTS.md line 3809). This provides a stable seam:

```csharp
// AdminPortal/Clients/IProductCatalogAdminClient.cs
public interface IProductCatalogAdminClient
{
    Task<ProductContentView?> GetProductContentAsync(string sku, CancellationToken ct);
    Task<IReadOnlyList<ProductContentListItem>> SearchProductsAsync(string query, CancellationToken ct);
    Task<HttpResponseMessage> UpdateDescriptionAsync(string sku, UpdateDescriptionRequest request, CancellationToken ct);
}

// AdminPortal.Api/Clients/ProductCatalogAdminClient.cs
public sealed class ProductCatalogAdminClient : IProductCatalogAdminClient
{
    private readonly HttpClient _httpClient;

    public ProductCatalogAdminClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<ProductContentView?> GetProductContentAsync(string sku, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"/api/catalog/products/{sku}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductContentView>(ct);
    }
    // ...
}
```

When Product Catalog evolves its API surface, only `ProductCatalogAdminClient` needs to change — the interface consumed by BFF handlers remains stable.

### API Versioning: Not Yet

**Don't introduce API versioning at this stage.** Here's why:

1. **Internal consumers only.** Backoffice BFF is the only consumer of admin endpoints. There are no third-party API consumers to break.
2. **Co-deployed.** In CritterSupply's architecture, all BCs are deployed together from the same repo. A breaking API change in ProductCatalog can be accompanied by a corresponding client update in AdminPortal in the same PR.
3. **Complexity cost.** URL-based versioning (`/api/v1/...` → `/api/v2/...`) adds routing complexity and doubles the endpoint surface during migration periods.
4. **When to add it.** Introduce versioning if/when CritterSupply has external API consumers (public API for third-party integrations) or when domain BCs are deployed independently with different release cadences.

---

## 9. Implementation Phasing

### Phase 0: BackofficeIdentity BC (Prerequisite)

**Scope:** Identity only — no portal, no frontend

| Task | Effort | Dependency |
|------|--------|------------|
| Create `BackofficeIdentity` project (EF Core, DbContext, schema `backofficeidentity`) | 1 session | None |
| Create `BackofficeIdentity.Api` (JWT issuer, login/logout/refresh, seed data) | 1-2 sessions | BackofficeIdentity project |
| Add `AdminRole` enum to `Messages.Contracts` | < 1 hour | None |
| Add `AdminUserCreated`, `AdminUserDeactivated`, `AdminUserRoleChanged` to `Messages.Contracts` | < 1 hour | AdminRole enum |
| Write ADR 0026: BackofficeIdentity (documents decisions from this research) | 1 session | This document |
| Integration tests (Alba + TestContainers) | 1 session | BackofficeIdentity.Api |

**Deliverable:** `POST /api/admin-identity/auth/login` returns a valid JWT with AdminUserId and AdminRole claims.

### Phase 1: AdminPortal.Api + Blazor WASM Shell (Read-Only)

**Scope:** BFF with read endpoints, SignalR hub, dashboard shell

| Task | Effort | Dependency |
|------|--------|------------|
| Create `AdminPortal` domain project (clients, composition, realtime interfaces) | 1 session | Phase 0 |
| Create `AdminPortal.Api` (JWT auth, RBAC policies, SignalR hub, RabbitMQ subscriptions) | 2 sessions | Phase 0 |
| Create `AdminPortal.Web` (Blazor WASM, auth flow, role-based navigation) | 2 sessions | AdminPortal.Api |
| Executive dashboard (live KPI cards via SignalR) | 1-2 sessions | AdminPortal.Api + Web |
| Operations dashboard (order pipeline, alert feed) | 1-2 sessions | AdminPortal.Api + Web |
| Customer service: customer lookup (read-only) | 1 session | Customer Identity BC endpoints |
| Customer service: order detail with saga state | 1 session | Orders BC endpoints |
| Multi-issuer JWT in ProductCatalog.Api (rename existing policy) | < 1 session | Phase 0 |
| Write ADR 0027: Multi-Issuer JWT Strategy | 1 session | This document |
| Write ADR 0028: Blazor WASM for Backoffice | 1 session | This document |

**Deliverable:** Admin users can log in, see role-filtered dashboards, and look up customer orders.

### Phase 2: Write Operations

**Scope:** Content editing, pricing, inventory management, order cancellation

| Task | Effort | Dependency |
|------|--------|------------|
| CopyWriter: product description update | 1-2 sessions | Product Catalog admin endpoints |
| PricingManager: set base price, schedule price change | 2 sessions | Pricing BC admin endpoints |
| WarehouseClerk: adjust inventory, receive stock, acknowledge alerts | 2 sessions | Inventory BC admin endpoints |
| CustomerService: cancel order | 1 session | Orders BC admin endpoint |
| Add auth to Inventory.Api, Orders.Api (admin scheme only) | 1 session | Phase 0 signing key config |
| Audit trail validation (ensure all mutations carry adminUserId) | 1 session | All write endpoints |

**Deliverable:** All 7 admin roles can perform their assigned tasks end-to-end.

### Phase 3: Polish & Advanced Features

**Scope:** Store credit, report exports, advanced UI

| Task | Effort | Dependency |
|------|--------|------------|
| CustomerService: issue store credit | 1 session | Store Credit BC (not yet built) |
| Executive: CSV/Excel report exports | 1-2 sessions | Analytics BC (not yet built) |
| Tab visibility API for token refresh (ADR 0025 must-fix) | < 1 session | AdminPortal.Web |
| Session expiry modal (ADR 0025 must-fix) | < 1 session | AdminPortal.Web |
| Warehouse: barcode scanning integration | 2 sessions | AdminPortal.Web |
| SystemAdmin: user management CRUD | 2 sessions | BackofficeIdentity.Api |

---

## 10. ADRs to Draft

Based on this research, the following ADRs should be drafted before implementation begins:

| ADR # | Title | Key Decision |
|-------|-------|-------------|
| 0026 | BackofficeIdentity BC: Separate Identity Store | Separate BC, EF Core, mirrors VendorIdentity; no tenant concept; 7-role AdminRole enum |
| 0027 | Multi-Issuer JWT Strategy for Domain BCs | Named JWT Bearer schemes (`"Vendor"`, `"Backoffice"`); rename existing `"Backoffice"` policy to `"VendorAdmin"` |
| 0028 | Blazor WASM for Backoffice Frontend | Blazor WASM over React/Next.js for consistency; reserve React for Operations Dashboard BC |
| 0029 | Backoffice SignalR Hub Design | Single hub, role-based groups, multi-group membership for supervisory roles |

---

## 11. Product Owner Decisions

The following questions were raised during research and resolved with Product Owner input:

1. **Single role vs multi-role per user?**
   - **PO Decision:** Keep single role per user. Make OperationsManager the "Swiss Army knife" — inherits read access to inventory, pricing history, and product content (not writes). For small teams (<15 people), most staff get OperationsManager. Specialized roles (CopyWriter, PricingManager) only created when the team grows. Revisit multi-role composition at 50+ admin users.
   - **Architect alignment:** ✅ Single role with permission inheritance stays. Update the permission matrix to add OperationsManager read access to pricing history and product content.

2. **Password policy for admin users?**
   - **PO Decision:** Argon2id hashing, minimum 12 characters in Phase 1. Defer complexity and rotation to Phase 3 or corporate SSO integration.
   - **Architect alignment:** ✅ Agreed.

3. **Admin user lifecycle: invitation vs direct creation?**
   - **PO Decision:** Use invitation flow (72-hour token, email link, self-service password setup). SystemAdmin is the only role that can invite. Seed one SystemAdmin for dev environments. On departure: immediate deactivation, force session termination via SignalR, soft-deactivate (never delete — audit trail must survive). Support reactivation (`AdminUserReactivated`) for contractors and seasonal staff.
   - **Architect alignment:** ✅ Invitation flow matches BackofficeIdentity design. Add `deactivatedAt` check against JWT `iat` for immediate session invalidation.

4. **Concurrent admin edits?**
   - **PO Decision:** Last-write-wins for Phase 1. When Product Catalog evolves to event sourcing, use optimistic concurrency. Defer conflict resolution UI to Phase 3.
   - **Architect alignment:** ✅ Agreed.

5. **Store Credit BC dependency?**
   - **PO Decision:** Defer to Phase 3. CS workaround: manual tracking via order notes (see #6 below). When Store Credit BC launches, import manually-tracked credits as `StoreCreditIssued` events. Store credit becomes urgent only if return policy defaults to store credit instead of refund.
   - **Architect alignment:** ✅ Agreed.

## 12. Product Owner — Additional Requirements

The following requirements emerged from the PO business review and should be integrated into the Backoffice plan:

### Phase 1 Additions (Critical)

| # | Requirement | Rationale | Effort |
|---|------------|-----------|--------|
| 1 | **Order notes/internal comments** for CS reps | Day-one need. Without it, CS reps use Slack/sticky notes and context is lost on shift changes. | 1 session |
| 2 | **WarehouseClerk alert viewing + acknowledgment** (move from Phase 2) | Low-cost addition since SignalR alerts already flow in Phase 1. Clerk can see and ack alerts before write tools arrive in Phase 2. | <1 session |
| 3 | **OperationsManager read access to pricing history + product content** | Ops lead needs cross-functional visibility. Read-only, no writes. | <1 session |
| 4 | **Full customer order history** (not just "recent 5") | CS reps need to see *all* orders for a customer with search/filter. "Recent 5" is the summary card; add "view all" link. | <1 session |

### Phase 2 Additions

| # | Requirement | Rationale | Effort |
|---|------------|-----------|--------|
| 5 | **Receiving discrepancy notes** | Clerk records "Expected 100, received 92 — 8 units damaged in transit." Invaluable for vendor dispute resolution. Just a text field on the receiving form. | <1 session |
| 6 | **Escalation workflow** (CS → OperationsManager) | CS rep flags unresolvable issue (`OrderEscalated` event → SignalR alert to `role:operations` group). Simple "flag for review" action. | 1 session |
| 7 | **Floor price visibility** | PricingManager sees floor price when setting a new price: "Current floor: $12.00. You are setting: $14.99." | <1 session |

### Planning / Future Requirements

| # | Requirement | Priority | Phase |
|---|------------|----------|-------|
| 8 | **returns-management.feature** | Add Gherkin feature file now (even if `@future` tagged). Returns BC + Backoffice returns dashboard is the biggest missing feature area. | Planning |
| 9 | **Promotions BC** as a future bounded context | Coupon codes, automatic discounts, bundle pricing. Separate from Pricing BC. Backoffice will need promotion management tooling. | Phase 4+ |
| 10 | **Bulk operations** pattern for all write roles | First request from every role will be "can I do this in bulk?" Build single-item ops first, but ensure data model supports batch commands. | Phase 3 |
| 11 | **Audit log viewer** for SystemAdmin | Query audit trail: "Show me everything Jane Smith did in the last 30 days." Read projection in Backoffice subscribing to domain events. | Phase 3 |
| 12 | **ChannelManager role** for Listings BC | New role when Listings/Marketplaces BC launches. Owns channel strategy: which products listed where, at what price, with what content. | Phase 4+ |

### Executive Dashboard KPIs (PO-Validated)

The 7 KPIs that matter for the daily executive dashboard:

| # | KPI | Source BC | Comparison |
|---|-----|-----------|------------|
| 1 | Gross Revenue (Today) | Orders | vs same day last week |
| 2 | Order Count (Today) | Orders | vs same day last week |
| 3 | Average Order Value | Calculated | weekly trend |
| 4 | Cart-to-Order Conversion Rate | Shopping + Orders | weekly trend |
| 5 | Payment Failure Rate | Payments | ⚠️ >5% warning, 🔴 >10% critical |
| 6 | Fulfillment Pipeline (by saga state) | Orders | real-time distribution |
| 7 | Low-Stock SKU Count | Inventory | ⚠️ >10 warning, 🔴 >25 critical |

**Every metric shows delta vs comparison period** with color coding: 🟢 up >5%, 🟡 flat ±5%, 🔴 down >5%.

### CS Day-to-Day Reality (PO-Validated Priority)

| Rank | CS Action | % of Tickets | Phase 1 Status |
|------|-----------|-------------|----------------|
| 1 | "Where is my order?" (WISMO) | 35-40% | ✅ Order detail with saga timeline |
| 2 | Refund/return initiation | 20-25% | 🟡 Deferred to Returns BC |
| 3 | Order modification (address change) | 15-20% | 🟡 Future |
| 4 | Order cancellation | 10-15% | ✅ Planned |
| 5 | Account/payment issues | 5-10% | 🟡 Partially (customer lookup) |

## 13. UX Engineer Review

A comprehensive UX research document has been created at **[Backoffice UX Research](backoffice-ux-research.md)**, covering:

- **Navigation architecture**: Role-filtered sidebar hiding inaccessible items, grouped by domain area
- **Dashboard layout**: Per-role dedicated dashboard pages with reusable KPI card components
- **Data tables**: Server-side pagination via `MudTable<T>`, debounced search, status = icon + color + text (never color alone)
- **Form patterns**: Inline editing for single fields, modals for multi-field, confirmation dialogs for destructive actions with consequence description
- **Real-time alerts**: Three tiers (Critical: persistent toast + optional sound, Warning: badge + feed, Info: feed only), alert center as right-side `MudDrawer`
- **Accessibility**: WCAG 2.1 AA target, keyboard hotkeys for CS reps, `aria-live` for alert feeds
- **Session experience**: Modal overlay on expiry (preserves page state), role badge in header
- **Mobile/tablet**: WarehouseClerk only gets responsive design (768px+ tablet); all others desktop-first (1024px+)

---

## Appendix A: Port Allocation (Updated)

| Service | Port | Status |
|---------|------|--------|
| Product Catalog.Api | 5133 | ✅ Live |
| Orders.Api | 5231 | ✅ Live |
| Payments.Api | 5232 | ✅ Live |
| Inventory.Api | 5233 | ✅ Live |
| Fulfillment.Api | 5234 | ✅ Live |
| Customer Identity.Api | 5235 | ✅ Live |
| Shopping.Api | 5236 | ✅ Live |
| Storefront.Api | 5237 | ✅ Live |
| Storefront.Web | 5238 | ✅ Live |
| Vendor Portal.Api | 5239 | ✅ Live |
| Vendor Identity.Api | 5240 | ✅ Live |
| Vendor Portal.Web | 5241 | ✅ Live |
| Pricing.Api | 5242 | ✅ Live |
| **Backoffice.Api** | **5243** | 🟡 Planned |
| **Backoffice.Web** | **5244** | 🟡 Planned |
| **Backoffice Identity.Api** | **5245** | 🟡 Planned |

## Appendix B: Comparison — VendorIdentity vs BackofficeIdentity

| Aspect | VendorIdentity | BackofficeIdentity |
|--------|---------------|---------------|
| Users | External vendor partners | Internal employees |
| Roles | 3 (Admin, CatalogManager, ReadOnly) | 7 (CopyWriter → SystemAdmin) |
| Tenant concept | ✅ Multi-tenant (VendorTenantId) | ❌ Single-organization |
| JWT issuer | `vendor-identity` | `admin-identity` |
| JWT audience | `vendor-portal` | `backoffice` |
| Signing key | Vendor-specific key | Admin-specific key (separate) |
| Custom claims | VendorUserId, VendorTenantId, VendorTenantStatus | AdminUserId |
| Password hashing | Argon2id | Argon2id |
| Token lifecycle | 15-min access + 7-day refresh | 15-min access + 7-day refresh |
| Seed data | Per-tenant (Demo Vendor org) | Per-role (7 users, one per role) |
| EF Core schema | `vendoridentity` | `backofficeidentity` |
| Integration events | VendorTenantCreated, VendorUserActivated, etc. | AdminUserCreated, AdminUserDeactivated, AdminUserRoleChanged |
| Invitation flow | 72-hour invitation tokens (SHA-256 hashed) | Direct creation by SystemAdmin (no invitation) |

## Appendix C: Comparison — VendorPortal vs AdminPortal SignalR

| Aspect | VendorPortal Hub | AdminPortal Hub |
|--------|-----------------|-----------------|
| Path | `/hub/vendor-portal` | `/hub/admin` |
| Auth | JWT Bearer (vendor-identity) | JWT Bearer (admin-identity) |
| Group strategy | Dual: `vendor:{tenantId}` + `user:{userId}` | Multi: `admin-user:{userId}` + `role:*` groups |
| Group count per connection | 2 (tenant + user) | 1-7 (user + role groups based on role) |
| Message routing | Tenant-wide or user-specific | Role-scoped (with supervisory inheritance) |
| Marker interfaces | `IVendorTenantMessage`, `IVendorUserMessage` | `IAdminRoleMessage`, `IAdminUserMessage` |
| Message examples | `LowStockAlertRaised`, `SalesMetricUpdated` | `LiveMetricUpdated`, `AlertRaised`, `AdminLowStockAlertRaised` |
