# ADR 0009: .NET Aspire v13.1 Integration (Hybrid Architecture)

**Status:** ✅ Accepted (Docker Compose Primary + Aspire Optional)

**Date:** 2026-02-16 (Proposed) | 2026-03-04 (Accepted and Implemented)

## Context

CritterSupply uses Docker Compose for local development orchestration of infrastructure dependencies (PostgreSQL, RabbitMQ, Jaeger). Each bounded context API must be started manually using `dotnet run` with specific project paths. While functional, this approach requires manual coordination.

**Docker Compose Pain Points:**
- Manual startup of 8+ API projects and 1 Blazor Web app (infrastructure only starts via `docker-compose`)
- No unified dashboard to monitor .NET service health
- No built-in telemetry aggregation for .NET services (logging, metrics, distributed tracing)

**Why Not Full Aspire for Infrastructure?**
- **Wolverine.RabbitMQ doesn't integrate with Aspire's service discovery** - Aspire 13.1+ uses dynamic port allocation, but Wolverine expects fixed ports in `appsettings.json`
- **Marten requires explicit connection strings** - Cannot use Aspire's auto-injected connection strings without significant API refactoring
- **Fixed ports are simpler** - Docker Compose provides predictable ports (5433, 5672, 15672) that match existing `appsettings.json` files

**Key Requirements:**
1. **Preserve Docker Compose for infrastructure** - PostgreSQL, RabbitMQ, Jaeger run with fixed ports
2. **Preserve TestContainers for testing** - Integration tests remain unchanged
3. **Minimal changes to existing API projects** - No connection string refactoring required
4. **Support both Aspire and non-Aspire workflows** - Developers can use docker-compose alone or docker-compose + Aspire

## Decision

**Adopt Docker Compose as the primary development workflow with Aspire as an optional enhancement:**

- **Docker Compose (Primary)**: Infrastructure + manual `dotnet run` for APIs
  - Simplest onboarding: Just Docker Desktop + .NET 10
  - Fastest iteration: Native hot reload, no container rebuilds
  - Best debugging: F5 in IDE works seamlessly

- **.NET Aspire (Optional)**: Enhanced developer experience for advanced users
  - Unified dashboard for logs/traces/metrics across all .NET services
  - Single command to start all 9 services
  - OpenTelemetry observability built-in

- **Hybrid Architecture**: Infrastructure always runs via `docker-compose --profile infrastructure up -d` with fixed ports, whether using Docker Compose-only or Aspire

### Implementation Approach

#### 1. Create Two New Projects

**CritterSupply.AppHost** (Aspire orchestration project):
```
src/CritterSupply.AppHost/
├── CritterSupply.AppHost.csproj
├── Program.cs
└── appsettings.json
```

**CritterSupply.ServiceDefaults** (shared Aspire configuration):
```
src/CritterSupply.ServiceDefaults/
├── CritterSupply.ServiceDefaults.csproj
└── Extensions.cs
```

#### 2. Update API Projects

Each API project (Orders.Api, Payments.Api, etc.) will:
- Add reference to `CritterSupply.ServiceDefaults`
- Call `builder.AddServiceDefaults()` in Program.cs
- Remove hardcoded connection strings (use Aspire's service discovery)

**Before:**
```csharp
var martenConnectionString = builder.Configuration.GetConnectionString("marten")
    ?? throw new Exception("The connection string for Marten was not found");
```

**After:**
```csharp
// Aspire injects connection string via service discovery
var martenConnectionString = builder.Configuration.GetConnectionString("marten")
    ?? throw new Exception("The connection string for Marten was not found");
// No change needed - Aspire populates ConnectionStrings:marten automatically
```

#### 3. AppHost Configuration

The AppHost will configure:
- **PostgreSQL**: Single shared database with schema-per-BC pattern
- **RabbitMQ**: Message broker for integration events
- **8 API Projects**: All bounded context APIs (Orders, Payments, Shopping, etc.)
- **1 Blazor Web App**: Storefront.Web
- **Service References**: Inter-service HTTP communication (e.g., Storefront.Api → Shopping.Api)

Example AppHost Program.cs:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("crittersupply");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// Bounded Context APIs
var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

var paymentsApi = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// ... (repeat for all 8 APIs)

// Blazor Web App
builder.AddProject<Projects.Storefront_Web>("storefront-web")
    .WithReference(storefrontApi); // BFF reference

builder.Build().Run();
```

#### 4. ServiceDefaults Configuration

ServiceDefaults will provide:
- OpenTelemetry configuration (logging, metrics, tracing)
- Health checks configuration
- Service discovery helpers
- Common middleware registration

#### 5. Testing Strategy

**Integration Tests (Unchanged):**
- Continue using TestContainers for PostgreSQL/RabbitMQ
- Alba fixtures remain unchanged
- No dependency on Aspire for test execution

**Rationale:** Integration tests should be hermetic and not depend on external orchestration. TestContainers provides isolated infrastructure per test run.

#### 6. CI/CD Strategy (GitHub Actions)

**Keep Docker Compose for CI/CD:**
- GitHub Actions workflow continues using `docker compose --profile ci up -d`
- No changes needed to `.github/workflows/dotnet.yml`
- CI environment doesn't need Aspire workload installed

**Rationale:** Docker Compose is battle-tested in CI environments and doesn't require .NET Aspire workload installation.

#### 7. Developer Workflow Options

**Option 1: Docker Compose Only (⭐ Recommended - Simplest)**
```bash
# Start infrastructure only
docker-compose --profile infrastructure up -d

# Start individual APIs manually as needed
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"
# ... etc
```

**Why this is the default:**
- ✅ No additional setup beyond Docker Desktop + .NET 10
- ✅ Fastest hot reload (native .NET process)
- ✅ Easiest debugging (F5 in IDE works seamlessly)
- ✅ Lower memory footprint
- ✅ All IDE tooling works (profilers, diagnostics, live unit testing)

**Option 2: Docker Compose + Aspire (Optional - Enhanced Observability)**
```bash
# Step 1: Start infrastructure with fixed ports
docker-compose --profile infrastructure up -d

# Step 2: Start .NET services via Aspire
dotnet run --project src/CritterSupply.AppHost

# Aspire dashboard opens at https://localhost:17265
# All .NET services, logs, traces, metrics visible in one place
```

**When to use Aspire:**
- You want unified dashboard for logs/traces/metrics
- You're demoing the system to stakeholders
- You're exploring OpenTelemetry patterns

**Option 3: Docker Compose All Services (Containerized Dev)**
```bash
# Start infrastructure + all 8 APIs + Blazor web in containers
docker-compose --profile all up --build

# Useful for: demos, onboarding, cross-BC integration testing
```

## Rationale

### Why Docker Compose First?

**Docker Compose as Primary Workflow:**
1. **Zero Additional Setup**: Just Docker Desktop + .NET 10 SDK
2. **Fastest Iteration**: Native hot reload, no container rebuilds
3. **Best Debugging**: F5 in Rider/Visual Studio works seamlessly
4. **Lower Memory**: Only infrastructure in containers (~300MB vs ~2GB for full stack)
5. **IDE Tooling**: Profilers, diagnostics, live unit testing all work
6. **Simplicity**: Predictable ports (5433, 5672), no magic
7. **CI/CD Proven**: Battle-tested in GitHub Actions

**Aspire as Optional Enhancement:**
1. **Enhanced Observability**: Unified dashboard for logs/traces/metrics across all .NET services
2. **Convenience**: Single command to start 9 services (vs 9 manual `dotnet run` commands)
3. **Service Discovery**: Automatic HTTP endpoint discovery for service-to-service calls
4. **Learning Tool**: Demonstrates modern .NET practices for reference architecture
5. **Demo-Friendly**: Polished UI for showcasing system to stakeholders

### Why NOT Full Aspire?

1. **Aspire 13.1+ uses dynamic ports** - `AddPostgres(port: 5433)` doesn't actually bind to host port 5433, it's just container metadata
2. **Wolverine.RabbitMQ reads `appsettings.json`** - Doesn't know how to read Aspire's injected connection strings
3. **Significant refactoring required** - Would need custom code to parse Aspire connection strings and configure Wolverine manually
4. **Docker Compose works perfectly** - Why fix what isn't broken?

### Why Keep TestContainers?

1. **Test Isolation**: Each test run gets fresh infrastructure (no shared state)
2. **Parallel Execution**: Tests can run concurrently without conflicts
3. **CI Compatibility**: Works seamlessly in GitHub Actions
4. **No External Dependencies**: Tests don't require Aspire AppHost or docker-compose running

## Consequences

### Positive

✅ **Improved local dev experience (optional)** - Aspire provides single command to start 9 .NET services
✅ **Unified observability (optional)** - Aspire dashboard shows logs, traces, metrics for .NET services
✅ **Docker Compose simplicity (primary)** - Infrastructure runs with fixed ports, no surprises
✅ **No refactoring required** - Connection strings remain in `appsettings.json`, no breaking changes
✅ **Service-to-service discovery** - Aspire provides HTTP endpoint discovery for BFF → API calls
✅ **Alignment with .NET ecosystem** - Following Microsoft best practices
✅ **OpenTelemetry by default** - Production-ready observability patterns

### Neutral

⚪ **Two orchestration options** - Aspire (local dev) + Docker Compose (CI/CD)
⚪ **Learning curve** - Developers need to understand Aspire basics
⚪ **Additional projects** - AppHost + ServiceDefaults add to solution complexity

### Negative

⚠️ **Optional Complexity** - Aspire adds learning curve for developers who opt-in
⚠️ **Two Orchestration Paths** - Maintainers must document both Docker Compose and Aspire workflows
⚠️ **Sync Burden** - docker-compose.yml and AppHost Program.cs must stay in sync when adding new BCs

### Migration Risk Assessment

**Low Risk:**
- PostgreSQL configuration (already using shared database with schema-per-BC)
- RabbitMQ configuration (standard AMQP connection)
- API project structure (no major refactoring needed)

**Medium Risk:**
- Connection string injection (Aspire uses different mechanism)
- Service-to-service HTTP calls (currently hardcoded URLs in Storefront.Web)
- Port allocation conflicts (Aspire may choose different ports)

**High Risk:**
- None identified

### Gotchas and Considerations

1. **Schema-per-BC Pattern**: CritterSupply uses single Postgres database with schema-per-BC. Aspire's PostgreSQL component supports this via `opts.DatabaseSchemaName` in Marten configuration (no changes needed).

2. **RabbitMQ Queue Provisioning**: Wolverine auto-provisions queues. Aspire's RabbitMQ component doesn't interfere with this (Wolverine continues working as-is).

3. **Hardcoded Ports**: Storefront.Web currently hardcodes `http://localhost:5237` for Storefront.Api. With Aspire, this becomes `builder.Configuration["services:storefront-api:http:0"]` (service discovery).

4. **TestContainers Independence**: Integration tests must NOT reference AppHost or ServiceDefaults. Tests remain hermetic.

5. **Docker Compose Synchronization**: When adding new BCs, must update both docker-compose.yml (if needed) and AppHost Program.cs.

6. **Aspire Dashboard Port**: Default is 15000 (may conflict with other dev tools). Configurable in AppHost.

7. **Resource Naming**: Aspire resource names (e.g., "postgres", "rabbitmq") must match configuration keys used in API projects.

## Alternatives Considered

### Alternative 1: Keep Docker Compose Only

**Pros:**
- No new projects or dependencies
- No learning curve for developers
- Works in CI/CD out-of-the-box

**Cons:**
- Manual startup of 9+ services (poor DX)
- No unified observability dashboard
- No service discovery (hardcoded connection strings)
- Not aligned with modern .NET ecosystem

**Rejected:** Poor developer experience doesn't align with CritterSupply's goal of being a reference architecture showcasing modern .NET practices.

### Alternative 2: Aspire-Only (Remove Docker Compose)

**Pros:**
- Single orchestration tool (simpler mental model)
- Forces all developers onto modern path

**Cons:**
- Breaks CI/CD workflows
- Removes flexibility for developers who prefer Docker Compose
- Higher migration risk (all-or-nothing approach)

**Rejected:** Too risky to remove Docker Compose entirely. Hybrid approach provides flexibility and incremental adoption.

### Alternative 3: Tye (Microsoft's predecessor to Aspire)

**Pros:**
- Similar developer experience to Aspire

**Cons:**
- Tye is deprecated in favor of Aspire
- Not a long-term viable option

**Rejected:** Aspire is the successor to Tye. No reason to adopt deprecated technology.

## Implementation Effort

### Estimated Time: 4-6 hours (one session)

**Breakdown:**

1. **Setup Aspire Projects (1-2 hours)**
   - Create AppHost project
   - Create ServiceDefaults project
   - Configure PostgreSQL and RabbitMQ resources

2. **Update API Projects (2-3 hours)**
   - Add ServiceDefaults references to 8 API projects + 1 Blazor app
   - Update Program.cs in each project (add `builder.AddServiceDefaults()`)
   - Update Storefront.Web to use service discovery for Storefront.Api reference

3. **Testing and Validation (1 hour)**
   - Start AppHost and verify all services launch
   - Run integration tests (should pass unchanged)
   - Verify RabbitMQ message flow between BCs
   - Test Blazor UI end-to-end

4. **Documentation Updates (30 mins)**
   - Update README.md with Aspire instructions
   - Update CLAUDE.md with Aspire patterns
   - Create docs/ASPIRE-GUIDE.md (developer onboarding)

### Complexity: Medium

**Why Medium (not High):**
- Aspire integration is additive (not replacing existing infrastructure)
- No changes to domain logic, event sourcing, or Wolverine handlers
- TestContainers and CI/CD remain unchanged (low risk)
- API projects require minimal changes (1-2 lines per Program.cs)

**Why Medium (not Low):**
- New concepts for team (Aspire workload, service discovery)
- Must update 9 projects (8 APIs + 1 Blazor)
- Service discovery requires replacing hardcoded URLs in Storefront.Web
- Potential for port conflicts or resource naming issues

## Implementation Notes (2026-03-04)

**Implementation Status:** ✅ Complete

**Projects Created:**
- `src/CritterSupply.AppHost/` - Aspire orchestration project with all 9 services + infrastructure configured
- `src/CritterSupply.ServiceDefaults/` - Shared Aspire configuration (OpenTelemetry, health checks, service discovery)

**Projects Updated:**
- All 9 API/Web projects (Orders.Api, Payments.Api, Inventory.Api, Fulfillment.Api, CustomerIdentity.Api, Shopping.Api, ProductCatalog.Api, Storefront.Api, Storefront.Web) now reference ServiceDefaults
- All Program.cs files updated with `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`
- Duplicate OpenTelemetry and health check configurations removed

**Key Enhancements:**
- ServiceDefaults includes `.AddSource("Wolverine")` and `.AddMeter("Wolverine")` to preserve Wolverine telemetry
- AppHost configures Jaeger (ports 16686, 4317, 4318) for distributed tracing
- All services configured with proper port allocations matching existing assignments
- PostgreSQL (5433), RabbitMQ (5672) ports preserved for backward compatibility

**Verification:**
- Solution builds successfully (0 errors, 0 warnings)
- AppHost builds with all Aspire hosting components present
- OpenTelemetry works both with AND without Aspire (connection string fallback preserved)
- TestContainers integration tests remain unchanged (hermetic)

**Documentation:**
- [docs/ASPIRE-GUIDE.md](../ASPIRE-GUIDE.md) - Comprehensive 400+ line guide
- [README.md](../../README.md) - Updated with Aspire Quick Start (recommended option)
- [CLAUDE.md](../../CLAUDE.md) - Updated with Aspire in Preferred Tools and Local Development Options

**Prerequisites:**
- None! Aspire 13.1+ uses the SDK approach (NuGet packages via `Aspire.AppHost.Sdk`). The deprecated `aspire` workload is not needed.

**Launch Command:**
```bash
dotnet run --project src/CritterSupply.AppHost
```

**Aspire Dashboard:** http://localhost:15000

**Note:** If you previously installed the workload, you can optionally remove it: `sudo dotnet workload uninstall aspire`

## References

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire 13.1 Release Notes](https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/aspire-13)
- [PostgreSQL with Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/database/postgresql-component)
- [RabbitMQ with Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/messaging/rabbitmq-component)
- [Service Discovery in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- CritterSupply BACKLOG.md (existing Aspire plan)
- [CritterSupply Aspire Guide](../ASPIRE-GUIDE.md)
