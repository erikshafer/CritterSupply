---
name: adding-new-bounded-context
description: Step-by-step checklist for adding a new bounded context (BC) to CritterSupply. Covers every infrastructure and configuration step, from domain planning through Docker, Aspire, and integration tests.
---

# Adding a New Bounded Context

A comprehensive checklist and reference guide for adding a new bounded context to CritterSupply. Every step is covered — including the infrastructure and configuration steps that are most easily forgotten.

> **Reference ADR:** [ADR 0027: Per-Bounded-Context Postgres Databases](../decisions/0027-per-bc-postgres-databases.md) — explains why each BC has its own database and the init-script pattern used here.

---

## Quick Checklist

Use this as a skim-first reference before working through each section in detail.

**Planning**
- [ ] Defined BC name, domain responsibility, and message contracts in CONTEXTS.md
- [ ] Decided on persistence strategy (Marten event sourcing / document store / EF Core / none)
- [ ] Chose single-project vs domain+API split
- [ ] Reserved a port from the CLAUDE.md allocation table

**Code**
- [ ] Created .NET project(s) with correct naming convention
- [ ] Added project(s) to `CritterSupply.slnx` with `dotnet sln add`
- [ ] Added `ProjectReference` to `Messages.Contracts` and `CritterSupply.ServiceDefaults`
- [ ] `Program.cs` wired up: Wolverine + Marten (or EF Core), reads `GetConnectionString("postgres")`
- [ ] `appsettings.json` has `"postgres"` key with correct `Database=<bcname>`
- [ ] `Properties/launchSettings.json` set to the reserved port
- [ ] Gherkin `.feature` files written before implementation begins

**Database** ⚠️ Most commonly forgotten
- [ ] Added `CREATE DATABASE <bcname>;` to `docker/postgres/create-databases.sh`
- [ ] Recreated the Docker volume (or ran `CREATE DATABASE` manually on the live container)

**Docker**
- [ ] Added service block to `docker-compose.yml` under `# ===== BOUNDED CONTEXT APIS =====`
- [ ] Dockerfile created at `src/<BCFolder>/<ProjectName>.Api/Dockerfile`

**Aspire**
- [ ] Registered project in `src/CritterSupply.AppHost/AppHost.cs`

**Documentation & Tests**
- [ ] `CONTEXTS.md` updated with messages published/subscribed
- [ ] Port allocation table in `CLAUDE.md` updated
- [ ] `docs/skills/README.md` updated if adding new skill docs
- [ ] Integration `TestFixture.cs` created using TestContainers pattern
- [ ] Integration tests written for happy path and key error cases

---

## When to Use This Skill

Use this guide whenever you are:
- Adding an entirely new bounded context (e.g., Returns, Vendor Portal, Shipping)
- Spinning up an experimental BC for a spike or proof-of-concept
- Onboarding a contributor who needs to understand the full BC scaffolding process

For adding features *inside* an existing BC, see [Vertical Slice Organization](./vertical-slice-organization.md) instead.

---

## Step 1 — Plan & Domain Design

Before touching any code, get clarity on the following.

### 1a. Define the BC's responsibility

Answer these questions:
- What is the single business capability this BC owns?
- What commands does it accept?
- What events does it publish?
- What events from other BCs does it subscribe to?
- Does it need its own persistent state, or is it message-only?

Document the answers in **CONTEXTS.md** before writing a single line of code. CONTEXTS.md is the architectural source of truth — code follows from it, not the other way around.

### 1b. Choose a persistence strategy

| Strategy | Use when |
|---|---|
| **Marten event sourcing** | BC has rich aggregate lifecycle (orders, carts, shipments) |
| **Marten document store** | BC needs queryable read models without event history |
| **EF Core** | BC has relational data with complex FK relationships (e.g., Customer Identity) |
| **No persistence** | Pure message router / pass-through (rare) |

See [Marten Event Sourcing](./marten-event-sourcing.md), [Marten Document Store](./marten-document-store.md), and [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) for detailed guidance on each.

### 1c. Choose a project layout

**Domain + API split** (preferred for most BCs):
```
src/<BCFolder>/
├── <ProjectName>/          # Domain logic — classlib SDK
│   └── <ProjectName>.csproj
└── <ProjectName>.Api/      # Web host — Web SDK
    └── <ProjectName>.Api.csproj
```

**Single project** (only for very simple BFFs or spike work):
```
src/<BCFolder>/
└── <ProjectName>.Api/
    └── <ProjectName>.Api.csproj
```

See [Vertical Slice Organization](./vertical-slice-organization.md) for the full folder structure inside each project.

### 1d. Reserve a port

Check the **Port Allocation Table** in `CLAUDE.md` and take the next available port (currently starting from **5241**). Record it there immediately to prevent conflicts.

| BC | Port | Status |
|---|---|---|
| Orders | 5231 | ✅ Assigned |
| Payments | 5232 | ✅ Assigned |
| Inventory | 5233 | ✅ Assigned |
| Fulfillment | 5234 | ✅ Assigned |
| Customer Identity | 5235 | ✅ Assigned |
| Shopping | 5236 | ✅ Assigned |
| Product Catalog | 5133 | ✅ Assigned |
| Storefront API (BFF) | 5237 | ✅ Assigned |
| Storefront Web | 5238 | ✅ Assigned |
| Vendor Portal | 5239 | 📋 Reserved |
| Vendor Identity | 5240 | 📋 Reserved |
| **Your new BC** | **5241+** | — |

### 1e. Write Gherkin feature files first

Write at least 2–3 `.feature` files capturing key user stories **before coding**. Place them in `docs/features/<bcname>/`.

```gherkin
# docs/features/returns/return-request.feature
Feature: Return Request
  As a customer
  I want to request a return for an order item
  So that I can receive a refund or replacement

  Background:
    Given I have a delivered order with at least one item

  Scenario: Customer submits a valid return request
    Given the order was delivered within the last 30 days
    When I submit a return request for item "KIBBLE-XL"
    Then the return is created with status "Pending"
    And a ReturnRequested event is published

  Scenario: Return window has expired
    Given the order was delivered more than 30 days ago
    When I submit a return request
    Then the request is rejected with "Return window has expired"
```

See [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) for writing step definitions.

---

## Step 2 — Create the .NET Projects

### 2a. Scaffold the projects

```bash
# Domain project (classlib)
dotnet new classlib -n <ProjectName> \
  -f net10.0 \
  -o src/<BCFolder>/<ProjectName>

# API project (webapi)
dotnet new webapi -n <ProjectName>.Api \
  -f net10.0 \
  -o src/<BCFolder>/<ProjectName>.Api
```

> **Naming convention:** PascalCase, no spaces (e.g., `Returns`, `Returns.Api`). Folder names may contain spaces to match existing patterns (`Customer Identity/`, `Product Catalog/`) but project names must not.

### 2b. Add to the solution immediately

Skipping this step causes the project to be invisible to the IDE and CI:

```bash
# Add both projects to the .slnx solution
dotnet sln CritterSupply.slnx add src/<BCFolder>/<ProjectName>/<ProjectName>.csproj
dotnet sln CritterSupply.slnx add src/<BCFolder>/<ProjectName>.Api/<ProjectName>.Api.csproj
```

Verify:
```bash
dotnet sln CritterSupply.slnx list | grep <ProjectName>
```

### 2c. Add project references

**Domain project** (`<ProjectName>.csproj`) — classlib SDK:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="WolverineFx" />
    <PackageReference Include="WolverineFx.Marten" />
    <PackageReference Include="Marten" />
    <PackageReference Include="FluentValidation" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shared\Messages.Contracts\Messages.Contracts.csproj" />
  </ItemGroup>

</Project>
```

**API project** (`<ProjectName>.Api.csproj`) — Web SDK:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Swashbuckle.AspNetCore" />
    <PackageReference Include="WolverineFx.Http.Marten" />
    <PackageReference Include="WolverineFx.RabbitMQ" />
    <PackageReference Include="WolverineFx.FluentValidation" />
    <PackageReference Include="OpenTelemetry" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\<ProjectName>\<ProjectName>.csproj" />
    <ProjectReference Include="..\..\CritterSupply.ServiceDefaults\CritterSupply.ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

> All package versions are managed centrally in `Directory.Packages.props`. Do not specify version attributes in individual `.csproj` files.

### 2d. Wire up `Program.cs`

Use this as your starting template (adapted from the Fulfillment BC):

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using <ProjectName>;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// ⚠️ Connection string key is always "postgres" — never "marten" or the BC name
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? throw new InvalidOperationException("Connection string 'postgres' not found.");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // BC-scoped schema inside its own database
        opts.DatabaseSchemaName = "<bcname>";
        opts.DisableNpgsqlLogging = true;

        // Register aggregates here — e.g. event-sourced snapshots
        // opts.Projections.Snapshot<MyAggregate>(SnapshotLifecycle.Inline);
    })
    .AddAsyncDaemon(DaemonMode.Solo)
    .UseLightweightSessions()
    .IntegrateWithWolverine(config =>
    {
        config.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Host.UseWolverine(opts =>
{
    // Include handlers from the domain assembly
    opts.Discovery.IncludeAssembly(typeof(<SomeDomainType>).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddWolverineHttp();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/{documentName}/swagger.json";
    });
    app.UseSwaggerUI(opts =>
    {
        opts.RoutePrefix = "api";
        opts.SwaggerEndpoint("/api/v1/swagger.json", "<ProjectName> API");
    });
}

app.MapDefaultEndpoints();          // /health, /alive from Aspire defaults

if (app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/api/v1/health").AllowAnonymous();
}

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

[ExcludeFromCodeCoverage]
public partial class Program { }
```

> **EF Core instead of Marten?** Replace the Marten block with `builder.Services.AddDbContext<YourDbContext>(...)` and swap `WolverineFx.Http.Marten` for `WolverineFx.Http`. See [EF Core + Wolverine Integration](./efcore-wolverine-integration.md).

### 2e. Create `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "postgres": "Host=localhost;Port=5433;Database=<bcname>;Username=postgres;Password=postgres"
  },
  "RabbitMQ": {
    "hostname": "localhost",
    "virtualhost": "/",
    "port": 5672,
    "username": "guest",
    "password": "guest"
  }
}
```

> **Key is always `"postgres"`** — this was consolidated across all BCs in ADR 0027. `GetConnectionString("postgres")` in `Program.cs` reads this key. The Docker Compose environment variable maps to `ConnectionStrings__postgres` (double underscore = nesting).

### 2f. Create `Properties/launchSettings.json`

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "<ProjectName>Api": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "launchUrl": "api",
      "applicationUrl": "http://localhost:<PORT>",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Replace `<PORT>` with the port you reserved in Step 1d.

---

## Step 3 — Add the PostgreSQL Database ⚠️

> **This is the most commonly forgotten step.** The BC will start but immediately crash with a database connection error if you skip it.

CritterSupply runs a single shared Postgres container (`crittersupply-postgres`). Each BC connects to its own logical database within that server (e.g., `orders`, `payments`). The databases are created by an init script that runs once when the Docker volume is first **initialized**.

### 3a. Update `docker/postgres/create-databases.sh`

Add your new BC's database to the `psql` heredoc:

```bash
#!/bin/bash
# Creates one database per bounded context in the shared Postgres server.
# This script runs automatically on first container start via /docker-entrypoint-initdb.d/.

set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "postgres" <<-EOSQL
    CREATE DATABASE orders;
    CREATE DATABASE payments;
    CREATE DATABASE inventory;
    CREATE DATABASE fulfillment;
    CREATE DATABASE customeridentity;
    CREATE DATABASE shopping;
    CREATE DATABASE productcatalog;
    CREATE DATABASE storefront;
    CREATE DATABASE <bcname>;  -- ADD YOUR NEW BC HERE
EOSQL

echo "CritterSupply: all bounded context databases created successfully."
```

Keep the list in the same order as the BCs appear in `docker-compose.yml`. The script is the canonical source of all databases — if a database is missing here, it won't exist.

### 3b. Recreate the Docker volume

> **Why is this necessary?** Docker's official Postgres image runs the scripts in `/docker-entrypoint-initdb.d/` **only when the data directory is completely empty** — i.e., on the very first container start against a fresh volume. If a volume already exists (even one created 5 minutes ago), the init scripts are skipped entirely. Updating the script file has no effect on an existing volume.

```bash
# 1. Stop all running services
docker-compose down

# 2. Remove the existing Postgres volume
docker volume rm crittersupply_postgres

# 3. Restart infrastructure — the init script will run on the fresh volume
docker-compose --profile infrastructure up -d
```

Verify the new database was created:
```bash
docker exec crittersupply-postgres psql -U postgres -c "\l" | grep <bcname>
```

**Alternative (no data loss to other BCs):** If you cannot afford to wipe local data, connect directly and run the statement by hand:
```bash
docker exec -it crittersupply-postgres psql -U postgres -c "CREATE DATABASE <bcname>;"
```
This is fine for local development but remember to also update `create-databases.sh` so that the database is recreated automatically when others clone the repo and start fresh.

---

## Step 4 — Update `docker-compose.yml`

Add a new service block in the `# ===== BOUNDED CONTEXT APIS =====` section, following the established pattern exactly. Keep services in alphabetical order within their section.

```yaml
<bcname>-api:
  container_name: crittersupply-<bcname>
  build:
    context: .
    dockerfile: src/<BCFolder>/<ProjectName>.Api/Dockerfile
  ports:
    - "<PORT>:8080"
  environment:
    ASPNETCORE_ENVIRONMENT: Development
    ASPNETCORE_URLS: http://+:8080
    # ⚠️ Key is ConnectionStrings__postgres (double underscore = "ConnectionStrings:postgres" in .NET config)
    ConnectionStrings__postgres: "Host=postgres;Port=5432;Database=<bcname>;Username=postgres;Password=postgres"
    RabbitMQ__hostname: rabbitmq
    OTEL_EXPORTER_OTLP_ENDPOINT: http://jaeger:4317
    OTEL_SERVICE_NAME: <ProjectName>.Api
  depends_on:
    postgres:
      condition: service_healthy
    rabbitmq:
      condition: service_healthy
  networks:
    - backend
  profiles: [all, <bcname>]
```

**Key notes:**
- `Host=postgres` (not `localhost`) — services resolve each other by container name inside the Docker network.
- Port `5432` inside Docker (not `5433` — that's the host-side mapping for local dev).
- The `profiles` array contains `all` (starts with everything) plus a BC-specific tag (starts just this BC for targeted development).
- Do **not** add the BC to the `infrastructure` profile — that is reserved for Postgres, RabbitMQ, and Jaeger.

**Example — running only your new BC and its infrastructure:**
```bash
docker-compose --profile infrastructure up -d
docker-compose --profile <bcname> up -d
```

---

## Step 5 — Create a Dockerfile

Every API project needs a `Dockerfile` at `src/<BCFolder>/<ProjectName>.Api/Dockerfile`. Copy and adapt the pattern used by all existing BCs:

```dockerfile
# Multi-stage build for <ProjectName>.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution-level package management files
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy the entire src/ directory (tests are excluded via .dockerignore)
COPY src/ src/

# Restore
RUN dotnet restore "src/<BCFolder>/<ProjectName>.Api/<ProjectName>.Api.csproj"

# Build
WORKDIR /src/src/<BCFolder>/<ProjectName>.Api
RUN dotnet build <ProjectName>.Api.csproj -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish <ProjectName>.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "<ProjectName>.Api.dll"]
```

> The `.dockerignore` at the repo root already excludes `tests/` and other unnecessary directories. You do not need to modify it.

Verify the image builds before committing:
```bash
docker build -f src/<BCFolder>/<ProjectName>.Api/Dockerfile -t <bcname>-api-test .
```

---

## Step 6 — Update Aspire AppHost

Register the new project in `src/CritterSupply.AppHost/AppHost.cs` so it appears in the Aspire dashboard with unified observability:

```csharp
// <ProjectName> BC — <one-line description> (port <PORT>)
var <bcname>Api = builder.AddProject<Projects.<ProjectName>_Api>("crittersupply-aspire-<bcname>-api");
```

**Important naming convention:** The `Projects.<ProjectName>_Api` type is generated by the Aspire SDK from your `.csproj` filename. Dots become underscores, so `Returns.Api.csproj` → `Projects.Returns_Api`.

**About Aspire's role in CritterSupply:**
Aspire provides **unified observability** (traces, logs, metrics in one dashboard) and is the recommended way to run all BC APIs locally. It is **not** used for service discovery — APIs communicate via static ports defined in `appsettings.json`. This is because Wolverine.RabbitMQ does not integrate with Aspire's dynamic service discovery model (see [ADR 0009](../decisions/0009-aspire-integration.md)).

If your BC calls other BCs over HTTP (BFF pattern), add `WithReference`:
```csharp
var <bcname>Api = builder.AddProject<Projects.<ProjectName>_Api>("crittersupply-aspire-<bcname>-api")
    .WithReference(ordersApi)
    .WithReference(productCatalogApi);
```

---

## Step 7 — Update `CONTEXTS.md`

CONTEXTS.md is the **architectural source of truth**. If CONTEXTS.md disagrees with code, code is wrong.

Add a section for your new BC covering:

1. **Responsibility** — What business capability does this BC own?
2. **Commands** — What commands does it accept?
3. **Domain Events** — What events does it emit?
4. **Integration Messages Published** — What messages does it put on the bus?
5. **Integration Messages Subscribed** — What messages from other BCs does it consume?
6. **Orchestration vs. Choreography** — How does it interact with other BCs?

Example structure:
```markdown
## Returns BC

**Responsibility:** Manages the full lifecycle of customer return requests, from submission through resolution (refund or replacement).

### Commands
- `RequestReturn` — Customer submits a return for one or more order line items
- `ApproveReturn` — Staff approves a pending return request
- `RejectReturn` — Staff rejects a return request with a reason

### Domain Events
- `ReturnRequested` — A new return has been initiated
- `ReturnApproved` — A return has been approved; triggers refund or replacement
- `ReturnRejected` — A return has been rejected

### Integration Messages Published
- `ReturnApprovedEvent` → Payments BC (trigger refund)
- `ReturnApprovedEvent` → Inventory BC (trigger restocking, if applicable)

### Integration Messages Subscribed
- `OrderDelivered` ← Fulfillment BC (enables returns for delivered orders only)

### Pattern: Choreography
Returns publishes `ReturnApprovedEvent` and lets Payments and Inventory react independently. No saga orchestration needed for the basic happy path.
```

---

## Step 8 — Write Integration Tests

Integration tests for a new BC follow the TestContainers + Alba pattern used by every other BC. They spin up a real ephemeral Postgres container — **no changes to `create-databases.sh` are needed for tests**.

### 8a. Create the test project

```bash
dotnet new xunit -n <ProjectName>.Api.IntegrationTests \
  -f net10.0 \
  -o tests/<BCFolder>/<ProjectName>.Api.IntegrationTests

dotnet sln CritterSupply.slnx add \
  tests/<BCFolder>/<ProjectName>.Api.IntegrationTests/<ProjectName>.Api.IntegrationTests.csproj
```

Add references:
```xml
<ItemGroup>
  <PackageReference Include="Alba" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <PackageReference Include="Testcontainers.PostgreSql" />
  <PackageReference Include="WolverineFx" />
  <PackageReference Include="Shouldly" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\..\..\src\<BCFolder>\<ProjectName>.Api\<ProjectName>.Api.csproj" />
</ItemGroup>
```

### 8b. Create `TestFixture.cs`

```csharp
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace <ProjectName>.Api.IntegrationTests;

/// <summary>
/// Provides a real PostgreSQL container (via TestContainers) and an Alba host
/// for full integration testing of the <ProjectName> bounded context.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("<bcname>_test_db")
        .WithName($"<bcname>-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Required for WebApplicationFactory + JasperFx command processing
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override the connection string with the ephemeral test container
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_postgres.GetConnectionString());
                });

                // Disable RabbitMQ and other external transports during tests
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>Gets a Marten session for direct database assertions.</summary>
    public IDocumentSession GetDocumentSession() =>
        Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

    /// <summary>Gets the document store for advanced operations.</summary>
    public IDocumentStore GetDocumentStore() =>
        Host.Services.GetRequiredService<IDocumentStore>();

    /// <summary>Cleans all documents between tests that require isolation.</summary>
    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    /// <summary>
    /// Sends a message through Wolverine and waits for all cascading work to complete
    /// before returning control to the test.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync(async ctx => await ctx.InvokeAsync(message));
    }

    /// <summary>
    /// Makes an HTTP call via Alba while tracking all Wolverine message side effects.
    /// </summary>
    protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });

        return (tracked, result);
    }
}
```

### 8c. Register the collection fixture

```csharp
// CollectionDefinition.cs
namespace <ProjectName>.Api.IntegrationTests;

[CollectionDefinition("<ProjectName> Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>;
```

### 8d. Write a first integration test

```csharp
// Features/Returns/RequestReturnTests.cs
using Shouldly;

namespace <ProjectName>.Api.IntegrationTests.Features.Returns;

[Collection("<ProjectName> Integration Tests")]
public class RequestReturnTests(TestFixture fixture)
{
    [Fact]
    public async Task POST_valid_return_request_returns_201()
    {
        var (_, result) = await fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new
            {
                OrderId = Guid.NewGuid(),
                Sku = "KIBBLE-XL",
                Reason = "Product arrived damaged"
            }).ToUrl("/api/returns");

            s.StatusCodeShouldBe(201);
        });

        result.ShouldNotBeNull();
    }
}
```

See [CritterStack Testing Patterns](./critterstack-testing-patterns.md) and [TestContainers Integration Tests](./testcontainers-integration-tests.md) for comprehensive testing guidance.

---

## Step 9 — Update Port Allocation in `CLAUDE.md`

Update the Port Allocation Table in `CLAUDE.md` to mark your port as assigned:

```markdown
| Returns | 5241 | ✅ Assigned | Returns/ |
```

This prevents future developers from accidentally claiming the same port.

---

## Step 10 — Update `docs/skills/README.md`

If your new BC introduces a new shared pattern or cross-cutting concern, add a reference to it in the skills README so it is discoverable.

For a standard BC (no new patterns), no README update is needed.

---

## Common Mistakes & How to Avoid Them

### ❌ Forgetting `create-databases.sh`

**Symptom:** BC starts up but immediately throws `3D000: database "<bcname>" does not exist`.

**Fix:** Add the `CREATE DATABASE <bcname>;` line to `docker/postgres/create-databases.sh` and recreate the volume.

---

### ❌ Not recreating the Docker volume

**Symptom:** You added the database to `create-databases.sh` but the error persists after `docker-compose up`.

**Why:** Docker only runs init scripts when the data directory is empty. An existing `crittersupply_postgres` volume skips them entirely — the changed script file is never executed.

**Fix:**
```bash
docker-compose down
docker volume rm crittersupply_postgres
docker-compose --profile infrastructure up -d
```

---

### ❌ Using the wrong connection string key

**Symptom:** `InvalidOperationException: Connection string 'marten' not found` or similar.

**Fix:** The connection string key is `"postgres"` everywhere — in `appsettings.json`, `GetConnectionString("postgres")`, and `ConnectionStrings__postgres` in Docker Compose. See [ADR 0027](../decisions/0027-per-bc-postgres-databases.md) for the rationale.

---

### ❌ Forgetting `dotnet sln add`

**Symptom:** Project builds locally but CI fails; IDE does not **recognize** the project.

**Fix:** Run `dotnet sln CritterSupply.slnx add <path-to.csproj>` immediately after creating the project.

---

### ❌ Using `Host=localhost` in Docker Compose environment

**Symptom:** BC service cannot reach Postgres inside Docker; connection refused.

**Fix:** Use `Host=postgres` (the Docker service name) in `docker-compose.yml`. Use `Host=localhost` only in `appsettings.json` (for `dotnet run` outside Docker).

---

### ❌ Missing `[ExcludeFromCodeCoverage] public partial class Program {}`

**Symptom:** Alba cannot resolve the `Program` type in the test project.

**Fix:** Add the partial class declaration at the bottom of `Program.cs` — this is required for `AlbaHost.For<Program>(...)` to work with minimal hosting.

---

### ❌ `create-databases.sh` fails with "cannot execute: required file not found" on Windows

**Symptom:** Postgres container crashes on first start with:
```
/docker-entrypoint-initdb.d/create-databases.sh: cannot execute: required file not found
```

**Why:** On Windows, git defaults to `core.autocrlf=true`, which silently converts Unix line endings (LF) to Windows line endings (CRLF) on checkout. The shebang line becomes `#!/bin/bash\r`, and the Linux kernel looks for the interpreter `/bin/bash\r` — a path that does not exist.

This is guarded against in `.gitattributes` (`*.sh text eol=lf`), which forces shell scripts to always use LF line endings regardless of git client configuration.

**If you encounter this despite `.gitattributes`:** The file may have been committed before the attribute was added, or was edited with a Windows editor that did not honour `.gitattributes`. Fix it by running:
```bash
# Re-normalise line endings for all tracked shell scripts
git add --renormalize .
git commit -m "Fix CRLF line endings in shell scripts"
```

Then recreate the volume:
```bash
docker-compose down
docker volume rm crittersupply_postgres
docker-compose --profile infrastructure up -d
```

---

## See Also

- [Vertical Slice Organization](./vertical-slice-organization.md) — File and folder structure inside a BC
- [Marten Event Sourcing](./marten-event-sourcing.md) — Event-sourced aggregates and projections
- [Marten Document Store](./marten-document-store.md) — Document store patterns for read models
- [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) — Relational persistence with Wolverine
- [TestContainers Integration Tests](./testcontainers-integration-tests.md) — Infrastructure setup for integration tests
- [CritterStack Testing Patterns](./critterstack-testing-patterns.md) — Alba HTTP scenarios, Wolverine message tracking
- [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) — Gherkin features and step definitions
- [Wolverine Message Handlers](./wolverine-message-handlers.md) — Command/query handlers, HTTP endpoints
- [ADR 0027: Per-Bounded-Context Postgres Databases](../decisions/0027-per-bc-postgres-databases.md) — Why each BC has its own database
- **CONTEXTS.md** — Architectural source of truth for BC boundaries and integration contracts
- **CLAUDE.md** — Port allocation table, project structure conventions, coding standards
