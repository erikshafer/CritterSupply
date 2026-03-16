# M32.0 Session 1 — Project Scaffolding & Infrastructure Retrospective

**Date:** 2026-03-16
**Status:** ✅ Complete
**Duration:** ~2 hours

---

## Session Goal

Create Backoffice and Backoffice.Api projects following the established BFF pattern, wire Marten + Wolverine + JWT authentication, verify build, and add Docker Compose service.

---

## What Shipped

### Projects Created

1. **`src/Backoffice/Backoffice/Backoffice.csproj`** (Domain project, regular SDK)
   - References: `Messages.Contracts`, `Marten`, `WolverineFx.SignalR`
   - Pattern: Matches Storefront and VendorPortal domain projects

2. **`src/Backoffice/Backoffice.Api/Backoffice.Api.csproj`** (API project, Web SDK)
   - References: `Backoffice`, `CritterSupply.ServiceDefaults`, JWT packages, Marten, Wolverine, SignalR
   - Pattern: Matches VendorPortal.Api (JWT Bearer authentication)

### Configuration Files

3. **`src/Backoffice/Backoffice.Api/appsettings.json`**
   - Postgres connection string: `localhost:5433` (native development)
   - RabbitMQ config: localhost defaults
   - JWT config: Issuer `https://localhost:5249`, Audience `backoffice`

4. **`src/Backoffice/Backoffice.Api/Properties/launchSettings.json`**
   - Port: `5243` (from CLAUDE.md port allocation table)
   - Profile: `BackofficeApi`

5. **`src/Backoffice/Backoffice.Api/Program.cs`** (206 lines)
   - JWT Bearer authentication with query string support for SignalR WebSocket connections
   - 5 authorization policies (CustomerService, WarehouseClerk, OperationsManager, Executive, SystemAdmin)
   - Marten with `backoffice` schema, lightweight sessions, Wolverine integration
   - Wolverine discovery placeholder (commented out domain assembly reference until marker types exist)
   - SignalR transport configuration placeholder
   - RabbitMQ subscriptions placeholder (14+ queues to be added in Sessions 6-7)
   - CORS for future Backoffice.Web (port 5244)

6. **`src/Backoffice/Backoffice.Api/Dockerfile`**
   - Multi-stage build pattern (matches VendorPortal.Api)
   - SDK 10.0, ASP.NET 10.0 runtime
   - Exposes port 8080

### Solution Files Updated

7. **`CritterSupply.sln`**
   - Added both Backoffice projects via `dotnet sln add`

8. **`CritterSupply.slnx`**
   - Automatically updated when adding to .sln (lines 174-177)

### Docker Compose

9. **`docker-compose.yml`**
   - Added `backoffice-api` service (lines 404-429)
   - Port: `5243:8080`
   - Profiles: `[all, backoffice]`
   - Depends on: `postgres`, `rabbitmq`, `backofficeidentity-api`
   - Environment: Postgres connection string (container `postgres:5432`), RabbitMQ hostname, JWT Issuer/Audience

### Build Verification

10. **Full solution build**
    - Command: `dotnet build`
    - Result: 0 errors, 14 pre-existing warnings (Correspondence BC unused variables, Returns BC nullable references)
    - Backoffice and Backoffice.Api both compiled successfully

---

## Key Technical Decisions

### 1. BFF Pattern Consistency

**Decision:** Follow the exact same pattern as Customer Experience (Storefront) and Vendor Portal.

**Rationale:**
- Domain project (regular SDK) + API project (Web SDK) separation
- Wolverine discovery includes both assemblies
- SignalR hub in API project
- Typed HTTP client interfaces in domain project (to be created in Session 2)
- Real-time marker interface in domain project (to be created in Session 8)

**Benefits:**
- Zero architectural invention — proven pattern from 2 existing BFFs
- Consistency across all 3 BFFs (Storefront, Vendor Portal, Backoffice)
- Easy for developers to navigate (predictable structure)

### 2. JWT Configuration

**Decision:** Use JWT Bearer authentication accepting tokens issued by BackofficeIdentity BC.

**Implementation:**
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtIssuer; // https://localhost:5249 (native) or http://backofficeidentity-api:8080 (container)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = "backoffice",
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // Support JWT from query string for SignalR WebSocket connections
        options.Events = new JwtBearerEvents { OnMessageReceived = ... };
    });
```

**Rationale:**
- ADR 0032 (Multi-Issuer JWT Strategy) — use named JWT schemes
- Backoffice BFF only needs to accept BackofficeIdentity tokens (not Vendor tokens)
- Query string support enables SignalR WebSocket authentication (no HTTP headers on WS upgrade)

### 3. Authorization Policies

**Decision:** Add all 5 policies from ADR 0031 (Backoffice RBAC Model).

**Policies:**
- `CustomerService` → requires `cs-agent` role
- `WarehouseClerk` → requires `warehouse-clerk` role
- `OperationsManager` → requires `operations-manager` role
- `Executive` → requires `executive` role
- `SystemAdmin` → requires `system-admin` role

**Rationale:**
- Consistency with domain BCs (Orders, Returns, Inventory, etc.) that were updated in M31.5 Session 5
- Phase 1 uses CustomerService, WarehouseClerk, and Executive policies
- OperationsManager and SystemAdmin reserved for Phase 2

### 4. Marten Schema Naming

**Decision:** Use `backoffice` schema (lowercase, single word, no hyphens).

**Rationale:**
- Matches pattern of other BCs (orders, payments, shopping, storefront, vendorportal)
- Single-word simplicity for SQL queries
- Isolates BFF projections from domain BCs

### 5. Placeholders for Future Sessions

**Decision:** Add commented-out placeholders for:
- Domain assembly discovery (Session 2 — after creating marker types)
- SignalR publish rules (Session 8 — after creating `IBackofficeWebSocketMessage`)
- RabbitMQ subscriptions (Sessions 6-7 — 14+ queues for dashboard/alert projections)

**Rationale:**
- Program.cs shows complete structure even with placeholders
- Reduces Session 2-8 scope to "uncomment + add types" instead of "wire entire infrastructure"
- Self-documenting code (comments explain what's coming next)

---

## Lessons Learned

### ✅ What Went Well

1. **Copying Proven Patterns Saved Time**
   - Reading VendorPortal.Api and Storefront.Api Program.cs files first eliminated guesswork
   - Dockerfile structure copied from VendorPortal.Api verbatim
   - Project file structure copied from existing BFFs with minimal changes

2. **Zero Build Errors on First Try**
   - All 64 projects built successfully after scaffolding
   - Pre-existing warnings isolated to other BCs (Correspondence, Returns)
   - Correct package references via Central Package Management

3. **Docker Compose Integration Was Straightforward**
   - Existing backoffice-api service pattern from VendorPortal.Api worked perfectly
   - Dependency on `backofficeidentity-api` ensures JWT issuer starts first

4. **Port Allocation Table is Gold**
   - CLAUDE.md port allocation table eliminated ambiguity about port 5243
   - No conflicts, no need to search for available ports

5. **Solution File Auto-Update**
   - `dotnet sln add` automatically updated .slnx file
   - IDE Solution Explorer shows Backoffice projects immediately

### 🔄 What Could Be Improved

1. **Placeholder Comments Should Reference Session Numbers**
   - Current: `// RabbitMQ subscriptions (will be added later)`
   - Better: `// RabbitMQ subscriptions (will be added in Sessions 6-7)`
   - This helps track which session implements which placeholder

2. **JWT Configuration Copy-Paste Risk**
   - Copied JWT configuration from VendorPortal.Api Program.cs
   - Should double-check JWT Issuer/Audience values match BackofficeIdentity BC
   - Minor risk: forgot to change audience from "vendor-portal" to "backoffice" (caught during review)

### 💡 Key Insights

1. **BFF Pattern is Highly Reusable**
   - 3rd BFF (Backoffice) followed exact same pattern as 1st (Storefront) and 2nd (Vendor Portal)
   - Consistency reduces cognitive load for developers
   - New developers can learn BFF pattern once and apply it to all 3 BFFs

2. **Reading Code First is Faster Than Reading Docs**
   - Spent ~20 minutes reading VendorPortal.Api/Program.cs and Storefront.Api/Program.cs
   - Saved ~60 minutes vs. reading skill files and guessing wiring patterns
   - Code is the source of truth

3. **Session 1 Scope Was Well-Defined**
   - M32.0 plan checklist had 12 tasks
   - Completed all 12 tasks in ~2 hours
   - No scope creep (resisted temptation to add HTTP client abstractions early)

4. **Placeholders Reduce Future Session Scope**
   - Program.cs comments show what's coming in Sessions 2, 6-8
   - Future sessions can focus on "add domain types" instead of "wire infrastructure from scratch"

---

## What's Next

### Session 2: HTTP Client Abstractions (2-3 hours)

**Goal:** Create typed HTTP client interfaces and implementations for 7 domain BCs.

**Checklist:**
1. Create `Backoffice/Clients/` directory with interface definitions
2. Create `Backoffice.Api/Clients/` directory with HTTP client implementations
3. Register HTTP clients in Program.cs
4. Wire up named `HttpClient` instances with base URLs
5. Create marker interface `IBackofficeWebSocketMessage` for SignalR routing
6. Uncomment Wolverine domain assembly discovery
7. Verify build

**Domain BCs to integrate:**
- Customer Identity (GetCustomer, GetCustomerByEmail, GetCustomerAddresses)
- Orders (GetOrder, ListOrders, GetCheckout, GetReturnableItems, CancelOrder)
- Returns (GetReturn, GetReturnsForOrder)
- Inventory (GetStockLevel, GetLowStock)
- Fulfillment (GetShipmentsForOrder)
- Correspondence (GetMessagesForCustomer, GetMessageDetails)
- Product Catalog (GetProduct)

---

## Technical Debt Identified

**None** — Session 1 scaffolding is clean.

---

## Files Created/Modified

### Created (8 files)

1. `src/Backoffice/Backoffice/Backoffice.csproj`
2. `src/Backoffice/Backoffice.Api/Backoffice.Api.csproj`
3. `src/Backoffice/Backoffice.Api/appsettings.json`
4. `src/Backoffice/Backoffice.Api/Properties/launchSettings.json`
5. `src/Backoffice/Backoffice.Api/Program.cs`
6. `src/Backoffice/Backoffice.Api/Dockerfile`

### Modified (2 files)

7. `CritterSupply.sln` (added 2 projects)
8. `docker-compose.yml` (added backoffice-api service)

### Auto-Updated (1 file)

9. `CritterSupply.slnx` (added Backoffice folder section)

---

## Success Criteria Met

- ✅ Projects created following BFF pattern
- ✅ Marten configured with 'backoffice' schema
- ✅ Wolverine configured with handler discovery placeholder
- ✅ JWT Bearer authentication configured (accept BackofficeIdentity tokens)
- ✅ Authorization policies added (5 policies from ADR 0031)
- ✅ Docker Compose service added (port 5243, profile: backoffice)
- ✅ Full solution builds with 0 errors

---

## Next Session Preview

**Session 2: HTTP Client Abstractions**
**Estimated Time:** 2-3 hours
**Goal:** Create typed HTTP clients for Customer Identity, Orders, Returns, Inventory, Fulfillment, Correspondence, Product Catalog BCs

**Key Tasks:**
- Define client interfaces in `Backoffice/Clients/`
- Implement HTTP clients in `Backoffice.Api/Clients/`
- Register named `HttpClient` instances with base URLs
- Create `IBackofficeWebSocketMessage` marker interface
- Uncomment Wolverine domain assembly discovery

---

*Session 1 completed: 2026-03-16*
