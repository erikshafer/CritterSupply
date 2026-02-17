# .NET Aspire v13.1 Implementation Guide for CritterSupply

This document provides step-by-step instructions for implementing .NET Aspire v13.1 in the CritterSupply solution.

## Prerequisites

### Developer Workstation Requirements

1. **.NET 10.0 SDK** (already installed ✅)
   ```bash
   dotnet --version
   # Should output: 10.0.x
   ```

2. **.NET Aspire Workload**
   ```bash
   # Install Aspire workload
   dotnet workload install aspire
   
   # Verify installation
   dotnet workload list
   # Should show: aspire
   ```

3. **Docker Desktop** (already required ✅)
   - Needed for Aspire to run PostgreSQL and RabbitMQ containers
   - CritterSupply already requires Docker for docker-compose workflow

### NuGet Package Versions

Aspire 13.1 packages to be added to `Directory.Packages.props`:

```xml
<!-- Aspire -->
<PackageVersion Include="Aspire.Hosting.AppHost" Version="13.1.0" />
<PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.1.0" />
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="13.1.0" />
<PackageVersion Include="Aspire.Npgsql" Version="13.1.0" />
<PackageVersion Include="Aspire.RabbitMQ.Client" Version="13.1.0" />
<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="13.1.0" />
```

## Implementation Steps

### Step 1: Create Aspire Projects

#### 1.1 Create ServiceDefaults Project

```bash
cd src/
dotnet new aspire-servicedefaults -n CritterSupply.ServiceDefaults
```

**Expected Structure:**
```
src/CritterSupply.ServiceDefaults/
├── CritterSupply.ServiceDefaults.csproj
└── Extensions.cs
```

**Update `Extensions.cs`** to include CritterSupply-specific configuration:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();
            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health checks
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
```

#### 1.2 Create AppHost Project

```bash
cd src/
dotnet new aspire-apphost -n CritterSupply.AppHost
```

**Expected Structure:**
```
src/CritterSupply.AppHost/
├── CritterSupply.AppHost.csproj
├── Program.cs
└── appsettings.json
```

**Update `Program.cs`** with CritterSupply bounded contexts:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// ==========================================
// Infrastructure Resources
// ==========================================

// PostgreSQL - Single shared database with schema-per-BC pattern
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "latest")
    .WithPgAdmin()  // Optional: Add pgAdmin for DB management
    .AddDatabase("crittersupply");

// RabbitMQ - Message broker for integration events
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// ==========================================
// Bounded Context APIs
// ==========================================

// Orders BC - Order lifecycle and checkout orchestration
var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Payments BC - Payment authorization and capture
var paymentsApi = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Shopping BC - Cart management
var shoppingApi = builder.AddProject<Projects.Shopping_Api>("shopping-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Inventory BC - Stock levels and reservations
var inventoryApi = builder.AddProject<Projects.Inventory_Api>("inventory-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Fulfillment BC - Shipping and delivery
var fulfillmentApi = builder.AddProject<Projects.Fulfillment_Api>("fulfillment-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Customer Identity BC - Customer profiles and addresses
var customerIdentityApi = builder.AddProject<Projects.CustomerIdentity_Api>("customeridentity-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Product Catalog BC - Product definitions and pricing
var productCatalogApi = builder.AddProject<Projects.ProductCatalog_Api>("productcatalog-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

// Storefront BFF - Backend for Frontend (API layer)
var storefrontApi = builder.AddProject<Projects.Storefront_Api>("storefront-api")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    // BFF composes from other BCs
    .WithReference(shoppingApi)
    .WithReference(ordersApi)
    .WithReference(productCatalogApi)
    .WithReference(customerIdentityApi);

// ==========================================
// Frontend Applications
// ==========================================

// Storefront Web - Blazor Server UI for customer experience
var storefrontWeb = builder.AddProject<Projects.Storefront_Web>("storefront-web")
    .WithReference(storefrontApi);  // Blazor app calls BFF API

builder.Build().Run();
```

**Update `appsettings.json`** (optional customization):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Aspire": "Information"
    }
  }
}
```

#### 1.3 Add Projects to Solution

```bash
cd /home/runner/work/CritterSupply/CritterSupply
dotnet sln add src/CritterSupply.ServiceDefaults/CritterSupply.ServiceDefaults.csproj
dotnet sln add src/CritterSupply.AppHost/CritterSupply.AppHost.csproj
```

### Step 2: Update Directory.Packages.props

Add Aspire package versions to centralized package management:

```xml
<!-- Add to <ItemGroup> in Directory.Packages.props -->

<!-- Aspire Hosting -->
<PackageVersion Include="Aspire.Hosting.AppHost" Version="13.1.0" />
<PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.1.0" />
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="13.1.0" />

<!-- Aspire Components -->
<PackageVersion Include="Aspire.Npgsql" Version="13.1.0" />
<PackageVersion Include="Aspire.RabbitMQ.Client" Version="13.1.0" />

<!-- Service Discovery -->
<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="13.1.0" />
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="13.1.0" />
```

### Step 3: Update API Projects

Each API project needs to reference ServiceDefaults and call `AddServiceDefaults()`.

#### 3.1 Add ProjectReference to ServiceDefaults

**For ALL API projects** (Orders.Api, Payments.Api, Shopping.Api, Inventory.Api, Fulfillment.Api, CustomerIdentity.Api, ProductCatalog.Api, Storefront.Api):

```xml
<!-- Add to each .Api.csproj file -->
<ItemGroup>
  <ProjectReference Include="..\..\CritterSupply.ServiceDefaults\CritterSupply.ServiceDefaults.csproj" />
</ItemGroup>
```

**Example for Orders.Api:**
```bash
# Edit src/Orders/Orders.Api/Orders.Api.csproj
```

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
    <PropertyGroup>
        <ImplicitUsings>enable</ImplicitUsings>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
        <PackageReference Include="Swashbuckle.AspNetCore" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="WolverineFx.Http.Marten" />
        <PackageReference Include="WolverineFx.RabbitMQ" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Orders\Orders.csproj" />
        <!-- NEW: Add ServiceDefaults reference -->
        <ProjectReference Include="..\..\CritterSupply.ServiceDefaults\CritterSupply.ServiceDefaults.csproj" />
    </ItemGroup>
</Project>
```

**Repeat for all 7 other API projects.**

#### 3.2 Update Program.cs in API Projects

Add `builder.AddServiceDefaults()` call **early** in Program.cs (before other services).

**Example for Orders.Api Program.cs:**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orders;
using Orders.Checkout;
using Orders.Placement;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// NEW: Add Aspire ServiceDefaults
// ========================================
builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// Connection string now populated by Aspire service discovery
var martenConnectionString = builder.Configuration.GetConnectionString("marten")
                             ?? throw new Exception("The connection string for Marten was not found");

builder.Services.AddMarten(opts =>
{
    opts.Connection(martenConnectionString);
    opts.AutoCreateSchemaObjects = AutoCreate.All;
    opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant();
    opts.DisableNpgsqlLogging = true;

    opts.Schema.For<Order>()
        .Identity(x => x.Id)
        .UseNumericRevisions(true)
        .Index(x => x.CustomerId);

    opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);
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
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    opts.Discovery.IncludeType<Order>();
    opts.Discovery.IncludeType<Checkout>();

    // RabbitMQ configuration now uses Aspire connection string
    var rabbitConfig = builder.Configuration.GetSection("RabbitMQ");
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
        rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    })
    .AutoProvision();

    opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
        .ToRabbitQueue("storefront-notifications");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

builder.Services.AddWolverineHttp();

var app = builder.Build();

// ========================================
// NEW: Map Aspire default endpoints
// ========================================
app.MapDefaultEndpoints();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Orders API");
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/api/v1/health").AllowAnonymous();
    // These are now handled by MapDefaultEndpoints() above
    // app.MapHealthChecks("/health");
    // app.MapHealthChecks("/alive", new HealthCheckOptions
    // {
    //     Predicate = r => r.Tags.Contains("live")
    // }).AllowAnonymous();
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

**Key Changes:**
1. Add `builder.AddServiceDefaults();` at the top (after `CreateBuilder()`)
2. Add `app.MapDefaultEndpoints();` before middleware configuration
3. Comment out redundant health check mappings (now in ServiceDefaults)

**Repeat for all 7 other API projects** (Payments, Shopping, Inventory, Fulfillment, CustomerIdentity, ProductCatalog, Storefront.Api).

### Step 4: Update Storefront.Web (Blazor App)

#### 4.1 Add ProjectReference to ServiceDefaults

```xml
<!-- Edit src/Customer Experience/Storefront.Web/Storefront.Web.csproj -->

<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MudBlazor" />
  </ItemGroup>

  <!-- NEW: Add ServiceDefaults reference -->
  <ItemGroup>
    <ProjectReference Include="..\..\CritterSupply.ServiceDefaults\CritterSupply.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

#### 4.2 Update Program.cs to Use Service Discovery

**Before (hardcoded URL):**
```csharp
builder.Services.AddHttpClient("StorefrontApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5237");
});
```

**After (service discovery):**
```csharp
using MudBlazor.Services;
using Storefront.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// NEW: Add Aspire ServiceDefaults
// ========================================
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// ========================================
// NEW: Use service discovery for BFF API
// ========================================
builder.Services.AddHttpClient("StorefrontApi", client =>
{
    // Aspire injects service URL via configuration
    client.BaseAddress = new Uri(builder.Configuration["services:storefront-api:http:0"] 
        ?? "http://localhost:5237"); // Fallback for non-Aspire runs
});

var app = builder.Build();

// ========================================
// NEW: Map Aspire default endpoints
// ========================================
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### Step 5: Update Solution File

Add new projects to the solution:

```bash
dotnet sln add src/CritterSupply.ServiceDefaults/CritterSupply.ServiceDefaults.csproj
dotnet sln add src/CritterSupply.AppHost/CritterSupply.AppHost.csproj
```

Update `.slnx` (if using Visual Studio 2022+):

```xml
<!-- Add to CritterSupply.slnx -->
<Folder Name="/Infrastructure/">
  <Project Path="src/CritterSupply.AppHost/CritterSupply.AppHost.csproj" />
  <Project Path="src/CritterSupply.ServiceDefaults/CritterSupply.ServiceDefaults.csproj" />
</Folder>
```

### Step 6: Testing

#### 6.1 Build Solution

```bash
dotnet restore
dotnet build
```

**Expected:** No build errors. All projects compile successfully.

#### 6.2 Run Aspire AppHost

```bash
cd src/CritterSupply.AppHost
dotnet run
```

**Expected:**
- Aspire dashboard opens at `http://localhost:15000`
- PostgreSQL and RabbitMQ containers start
- All 8 API projects launch
- Storefront.Web Blazor app launches
- Dashboard shows green health checks for all resources

#### 6.3 Verify Aspire Dashboard

Open browser to `http://localhost:15000`.

**Dashboard Tabs:**
- **Resources**: Shows all services (APIs, Postgres, RabbitMQ)
- **Console**: Logs from all services
- **Structured**: Structured logs with filtering
- **Traces**: Distributed tracing (OpenTelemetry)
- **Metrics**: Performance metrics

**Verify:**
- ✅ All resources show "Running" status
- ✅ Health checks are green
- ✅ Logs appear in Console tab
- ✅ Traces show HTTP requests between services

#### 6.4 Test Blazor UI

1. Navigate to Storefront.Web (check port in Aspire dashboard, usually `http://localhost:5238`)
2. Browse products, add items to cart
3. Proceed to checkout
4. Verify SSE real-time updates working

#### 6.5 Test RabbitMQ Integration

1. Open RabbitMQ Management UI: `http://localhost:15672` (guest/guest)
2. Check queues created by Wolverine (e.g., `storefront-notifications`)
3. Trigger order placement in UI
4. Verify message published to queue and consumed

#### 6.6 Run Integration Tests

```bash
cd /home/runner/work/CritterSupply/CritterSupply
dotnet test
```

**Expected:** All tests pass. TestContainers continue working independently of Aspire.

### Step 7: Update Documentation

#### 7.1 Update README.md

Add Aspire instructions to README.md:

````markdown
## ⏩ How to Run

### Requirements

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker Desktop](https://docs.docker.com/engine/install/)
- [.NET Aspire Workload](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling) (for local development)

Install Aspire workload:
```bash
dotnet workload install aspire
```

### 🛠️ Local Development

#### Option A: Aspire (Recommended)

**Single command to start entire stack:**

```bash
cd src/CritterSupply.AppHost
dotnet run
```

- Aspire dashboard opens at `http://localhost:15000`
- All services, logs, traces, metrics in one place
- Storefront Web UI available at port shown in dashboard

#### Option B: Docker Compose + Manual (Legacy)

```bash
# 1. Start infrastructure
docker-compose --profile all up -d

# 2. Build solution
dotnet build

# 3. Run specific BC (e.g., Orders)
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"

# 4. Run Storefront
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```
````

#### 7.2 Update CLAUDE.md

Add Aspire section to development guidelines:

```markdown
### Aspire Orchestration

CritterSupply supports .NET Aspire for local development orchestration:

**AppHost Location:** `src/CritterSupply.AppHost/`

**ServiceDefaults Location:** `src/CritterSupply.ServiceDefaults/`

**Running with Aspire:**
```bash
cd src/CritterSupply.AppHost
dotnet run
```

**Dashboard:** http://localhost:15000

**Key Patterns:**
- Service discovery via `builder.Configuration["services:service-name:http:0"]`
- OpenTelemetry automatic instrumentation
- Health checks at `/health` and `/alive` endpoints
- PostgreSQL connection string injected as `ConnectionStrings:marten`
- RabbitMQ connection details injected into Wolverine configuration
```

#### 7.3 Create ASPIRE-GUIDE.md

```bash
# This document (ASPIRE-IMPLEMENTATION-GUIDE.md) can be renamed to ASPIRE-GUIDE.md after implementation
mv docs/ASPIRE-IMPLEMENTATION-GUIDE.md docs/ASPIRE-GUIDE.md
```

## Rollback Plan

If Aspire integration causes issues, rollback is straightforward:

1. **Remove new projects:**
   ```bash
   dotnet sln remove src/CritterSupply.AppHost/CritterSupply.AppHost.csproj
   dotnet sln remove src/CritterSupply.ServiceDefaults/CritterSupply.ServiceDefaults.csproj
   rm -rf src/CritterSupply.AppHost
   rm -rf src/CritterSupply.ServiceDefaults
   ```

2. **Revert API projects:**
   - Remove `<ProjectReference>` to ServiceDefaults
   - Remove `builder.AddServiceDefaults()` call
   - Remove `app.MapDefaultEndpoints()` call

3. **Revert Storefront.Web:**
   - Restore hardcoded `http://localhost:5237` in HttpClient configuration
   - Remove ServiceDefaults reference

4. **Continue using Docker Compose:**
   ```bash
   docker-compose --profile all up -d
   dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
   ```

## Troubleshooting

### Issue: Aspire Dashboard Not Opening

**Symptoms:** `dotnet run` in AppHost succeeds but dashboard doesn't open.

**Solutions:**
1. Check port 15000 isn't already in use: `lsof -i :15000`
2. Manually navigate to `http://localhost:15000`
3. Check Aspire logs: `dotnet run --verbosity detailed`

### Issue: Services Failing to Start

**Symptoms:** Resources show "Failed" status in Aspire dashboard.

**Solutions:**
1. Check Console logs in dashboard for specific errors
2. Verify PostgreSQL/RabbitMQ containers started: `docker ps`
3. Check for port conflicts (8 API services + Blazor = 9 ports needed)
4. Verify connection strings injected correctly: inspect service environment variables in dashboard

### Issue: Integration Tests Failing

**Symptoms:** `dotnet test` fails after Aspire integration.

**Solutions:**
1. Verify TestContainers still working: `docker ps` during test run
2. Ensure test projects DON'T reference AppHost or ServiceDefaults
3. Check test fixtures still spin up isolated infrastructure
4. Run single test to isolate issue: `dotnet test --filter "FullyQualifiedName~TestMethodName"`

### Issue: Service Discovery Not Working in Storefront.Web

**Symptoms:** Blazor app can't connect to Storefront.Api.

**Solutions:**
1. Verify Storefront.Api reference in AppHost: `.WithReference(storefrontApi)`
2. Check configuration key: `builder.Configuration["services:storefront-api:http:0"]`
3. Inspect injected configuration in Aspire dashboard (click on Storefront.Web resource → Environment)
4. Fallback to hardcoded URL if Aspire not running

### Issue: RabbitMQ Messages Not Flowing

**Symptoms:** Events published but not consumed by handlers.

**Solutions:**
1. Verify RabbitMQ container started: check Aspire dashboard Resources tab
2. Check queue creation: RabbitMQ Management UI at `http://localhost:15672`
3. Verify Wolverine auto-provisioning still enabled: `.AutoProvision()` in Program.cs
4. Check message routing: inspect Exchanges and Bindings in RabbitMQ Management UI

## Verification Checklist

After implementation, verify:

- [ ] `dotnet workload install aspire` succeeds on dev machine
- [ ] `dotnet build` succeeds for entire solution
- [ ] `dotnet run` in AppHost starts all services
- [ ] Aspire dashboard opens at `http://localhost:15000`
- [ ] All resources show "Running" status in dashboard
- [ ] PostgreSQL and RabbitMQ containers running (`docker ps`)
- [ ] Storefront.Web accessible (check port in dashboard)
- [ ] Add item to cart → SSE real-time update works
- [ ] Place order → OrderPlaced event flows to RabbitMQ
- [ ] `dotnet test` passes all integration tests
- [ ] Docker Compose workflow still works (fallback option)
- [ ] CI/CD GitHub Actions workflow still passes

## Next Steps

After successful Aspire implementation:

1. **Update ADR 0009** - Change status from "Proposed" to "Accepted"
2. **Document Developer Onboarding** - Add "Install Aspire workload" to setup guide
3. **Create Video Tutorial** - Screen recording of Aspire dashboard walkthrough
4. **Monitor for Issues** - Gather feedback from team on developer experience
5. **Consider Future Enhancements:**
   - Azure Container Apps deployment (Aspire manifest generation)
   - Distributed tracing with Application Insights
   - Advanced health checks (database connectivity, RabbitMQ connectivity)

## References

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Service Discovery in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- [Aspire Components Gallery](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/components-overview)
- [ADR 0009: Aspire Integration](./decisions/0008-aspire-integration.md)
