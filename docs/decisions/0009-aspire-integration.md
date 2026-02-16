# ADR 0009: .NET Aspire v13.1 Integration

**Status:** ⚠️ Proposed

**Date:** 2026-02-16

## Context

CritterSupply currently uses Docker Compose for local development orchestration of infrastructure dependencies (PostgreSQL, RabbitMQ). Each bounded context API must be started manually using `dotnet run` with specific project paths. While functional, this approach has several limitations:

**Current Pain Points:**
- Manual startup of 8+ API projects and 1 Blazor Web app
- No unified dashboard to monitor service health
- Connection strings and configuration scattered across appsettings.json files
- No automatic service discovery between bounded contexts
- Developers must remember which ports each service uses
- TestContainers still needed for integration tests (separate from dev infrastructure)
- No built-in telemetry aggregation (logging, metrics, distributed tracing)

**Aspire Value Proposition:**
- Single `dotnet run` command starts entire application stack
- Unified dashboard showing all services, dependencies, and health checks
- Built-in service discovery (no hardcoded connection strings in code)
- Automatic telemetry collection (OpenTelemetry out-of-the-box)
- PostgreSQL and RabbitMQ containerization integrated with Aspire hosting
- Modern .NET developer experience aligned with Microsoft recommendations

**Key Requirements:**
1. **Preserve Docker Compose for CI/CD** - GitHub Actions workflow must continue working
2. **Preserve TestContainers for testing** - Integration tests should remain unchanged
3. **Minimal changes to existing API projects** - Surgical updates only
4. **Support both Aspire and non-Aspire workflows** - Developers can opt-in to Aspire

## Decision

**Adopt .NET Aspire v13.1 as the recommended local development orchestration tool** while maintaining Docker Compose for CI/CD and TestContainers for integration testing.

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

**Option A: Aspire (Recommended for local dev)**
```bash
# Start entire stack with Aspire
cd src/CritterSupply.AppHost
dotnet run

# Aspire dashboard opens at http://localhost:15000
# All services, logs, traces, metrics visible in one place
```

**Option B: Docker Compose + Manual (Still supported)**
```bash
# Start infrastructure only
docker-compose --profile all up -d

# Start individual APIs manually
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
```

**Option C: Hybrid (Infrastructure only)**
```bash
# Use Docker Compose for infra, run specific API via dotnet CLI
docker-compose --profile all up -d
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"
```

## Rationale

### Why Aspire?

1. **Modern .NET Best Practice**: Aspire is Microsoft's recommended approach for .NET distributed applications
2. **Developer Experience**: Single command to start entire stack vs 9+ manual commands
3. **Observability**: Built-in dashboard with logs, traces, metrics aggregation
4. **Service Discovery**: Eliminates hardcoded connection strings and port numbers
5. **Future-Proof**: Aligns with .NET ecosystem direction (Aspire is long-term investment)

### Why Keep Docker Compose?

1. **CI/CD Simplicity**: Docker Compose proven in GitHub Actions environment
2. **No Breaking Changes**: Existing workflows continue working
3. **Fallback Option**: Developers can use Docker Compose if Aspire issues arise
4. **Infrastructure-Only Use Case**: Sometimes devs only need Postgres/RabbitMQ

### Why Keep TestContainers?

1. **Test Isolation**: Each test run gets fresh infrastructure (no shared state)
2. **Parallel Execution**: Tests can run concurrently without conflicts
3. **CI Compatibility**: Works seamlessly in GitHub Actions
4. **No External Dependencies**: Tests don't require Aspire AppHost running

## Consequences

### Positive

✅ **Dramatically improved local dev experience** - Single command starts everything
✅ **Unified observability** - All logs, traces, metrics in Aspire dashboard
✅ **Service discovery** - No more hardcoded URLs/ports in code
✅ **Health monitoring** - Real-time status of all services and dependencies
✅ **Easier onboarding** - New developers run one command to get started
✅ **Alignment with .NET ecosystem** - Following Microsoft best practices
✅ **OpenTelemetry by default** - Production-ready observability patterns

### Neutral

⚪ **Two orchestration options** - Aspire (local dev) + Docker Compose (CI/CD)
⚪ **Learning curve** - Developers need to understand Aspire basics
⚪ **Additional projects** - AppHost + ServiceDefaults add to solution complexity

### Negative

❌ **Aspire workload requirement** - Developers must install `dotnet workload install aspire`
❌ **Version compatibility** - Aspire 13.1 requires .NET 10.0+ (already using .NET 10.0, so minimal risk)
❌ **Potential breaking changes** - Aspire is still evolving (13.x series)
❌ **Two sources of truth** - docker-compose.yml and AppHost Program.cs must stay in sync

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

## References

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire 13.1 Release Notes](https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/aspire-13)
- [PostgreSQL with Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/database/postgresql-component)
- [RabbitMQ with Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/messaging/rabbitmq-component)
- [Service Discovery in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- CritterSupply BACKLOG.md (existing Aspire plan)
