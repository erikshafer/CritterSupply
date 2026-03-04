# .NET Aspire Guide for CritterSupply

This guide provides comprehensive documentation for using .NET Aspire to run CritterSupply locally.

## Table of Contents

- [What is .NET Aspire?](#what-is-net-aspire)
- [Quick Start](#quick-start)
- [Aspire Dashboard](#aspire-dashboard)
- [Architecture](#architecture)
- [Comparison with Docker Compose](#comparison-with-docker-compose)
- [Troubleshooting](#troubleshooting)
- [Advanced Topics](#advanced-topics)

---

## What is .NET Aspire?

**.NET Aspire** is Microsoft's opinionated, cloud-ready stack for building observable, production-ready distributed applications. For CritterSupply, Aspire provides:

- **Single-command startup**: `dotnet run --project src/CritterSupply.AppHost` starts all 9 services + infrastructure
- **Unified dashboard**: Live logs, distributed traces, metrics, and health checks in one place
- **Service discovery**: APIs automatically discover each other (no hardcoded URLs)
- **OpenTelemetry by default**: Distributed tracing across all bounded contexts
- **Hot reload**: Native .NET performance with fast iteration

**Official Docs:**
- [.NET Aspire Overview](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Service Discovery](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- [Deployment](https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview)

---

## Quick Start

### Step 1: No Installation Required

**Aspire 13.1+ uses the SDK approach** via NuGet packages (`Aspire.AppHost.Sdk`). The deprecated `aspire` workload is **not needed**.

**If you previously installed the workload, you can remove it:**

```bash
# Optional: Remove deprecated workload (if installed)
sudo dotnet workload uninstall aspire
```

**Why no workload?** Starting with Aspire 9, Microsoft transitioned from a workload-based model to an SDK-based model using NuGet packages. The `Aspire.AppHost.Sdk` package in `CritterSupply.AppHost.csproj` provides all necessary tooling.

### Step 2: Run CritterSupply with Aspire

```bash
# From the repository root
dotnet run --project src/CritterSupply.AppHost

# Wait for services to start (30-60 seconds)
```

**Console output:**
```
Building...
info: Aspire.Hosting.DistributedApplication[0]
      Aspire version: 13.1.1
info: Aspire.Hosting.DistributedApplication[0]
      Distributed application starting.
info: Aspire.Hosting.DistributedApplication[0]
      Application host directory is: /Users/.../CritterSupply/src/CritterSupply.AppHost
info: Aspire.Hosting.DistributedApplication[0]
      Now listening on: http://localhost:15000
info: Aspire.Hosting.DistributedApplication[0]
      Login to the dashboard at http://localhost:15000
```

### Step 3: Access Services

| Service | URL | Description |
|---------|-----|-------------|
| **Aspire Dashboard** | http://localhost:15000 | Unified observability dashboard |
| **Storefront Web** | http://localhost:5238 | Customer-facing Blazor app |
| **Storefront API (BFF)** | http://localhost:5237 | Backend-for-Frontend API |
| **Orders API** | http://localhost:5231 | Order orchestration |
| **Payments API** | http://localhost:5232 | Payment processing |
| **Inventory API** | http://localhost:5233 | Stock management |
| **Fulfillment API** | http://localhost:5234 | Shipping & delivery |
| **Customer Identity API** | http://localhost:5235 | Customer authentication |
| **Shopping API** | http://localhost:5236 | Cart management |
| **Product Catalog API** | http://localhost:5133 | Product data |
| **PostgreSQL** | localhost:5433 | Shared database (schema-per-BC) |
| **RabbitMQ Management** | http://localhost:15672 | Message broker UI (guest/guest) |
| **Jaeger UI** | http://localhost:16686 | Distributed tracing UI |

---

## Aspire Dashboard

The Aspire Dashboard is the central hub for monitoring and debugging your application.

### Dashboard Sections

#### 1. Resources Tab
- **Status**: Health check status for all services (✅ Running, ⚠️ Starting, ❌ Unhealthy)
- **Source**: Project paths for each service
- **Endpoints**: URLs for accessing services
- **Environment Variables**: Configuration per service

#### 2. Console Logs Tab
- **Real-time logs** from all services
- **Filter by service**: Click service name to isolate logs
- **Log levels**: Info, Warning, Error, Critical
- **Search**: Full-text search across all logs

#### 3. Structured Logs Tab
- **Structured log viewer** with JSON support
- **Filter by**: Service, log level, time range
- **Correlation**: Trace ID linking for distributed requests

#### 4. Traces Tab
- **Distributed tracing** powered by OpenTelemetry
- **Service map**: Visual representation of service dependencies
- **Trace details**: Span timings, HTTP status codes, errors
- **Wolverine integration**: Message handler spans included

#### 5. Metrics Tab
- **HTTP metrics**: Request rates, latencies, status codes
- **Wolverine metrics**: Message success/failure counters
- **Custom metrics**: Application-specific metrics
- **Time series charts**: Historical trends

---

## Architecture

### AppHost Structure

The `CritterSupply.AppHost` project orchestrates all services and infrastructure:

```
src/CritterSupply.AppHost/
├── CritterSupply.AppHost.csproj    # Project references to all APIs
├── AppHost.cs                      # Service configuration
└── appsettings.json                # Aspire configuration
```

**Key configuration in `AppHost.cs`:**

```csharp
// Infrastructure
var postgres = builder.AddPostgres("postgres", port: 5433)
    .WithPgAdmin()
    .AddDatabase("crittersupply");

var rabbitmq = builder.AddRabbitMQ("rabbitmq", port: 5672)
    .WithManagementPlugin();

var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "latest")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "ui")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc");

// Bounded Context APIs
var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithHttpEndpoint(port: 5231)
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaeger.GetEndpoint("otlp-grpc"))
    .WithEnvironment("OTEL_SERVICE_NAME", "Orders.Api");

// ... (8 more APIs)

// Blazor Web App
builder.AddProject<Projects.Storefront_Web>("storefront-web")
    .WithHttpEndpoint(port: 5238)
    .WithReference(storefrontApi);  // Service discovery!
```

### ServiceDefaults Integration

All API projects reference `CritterSupply.ServiceDefaults`, which provides:

- **OpenTelemetry**: Configured with Wolverine sources and meters
- **Health Checks**: `/health` and `/alive` endpoints
- **Service Discovery**: Automatic resolution of service URLs
- **Resilience**: Standard retry/timeout policies for HttpClient

**In each API's `Program.cs`:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// ... rest of configuration

var app = builder.Build();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// ... rest of middleware
```

---

## Comparison with Docker Compose

| Feature | .NET Aspire | Docker Compose |
|---------|-------------|----------------|
| **Startup Command** | `dotnet run --project src/CritterSupply.AppHost` | `docker-compose --profile all up` |
| **Startup Time** | ~30 seconds | ~60-90 seconds (image builds) |
| **Hot Reload** | ✅ Native .NET hot reload | ❌ Requires container rebuild |
| **Debugging** | ✅ Full IDE debugging (F5) | ⚠️ Remote debugging only |
| **Observability** | ✅ Unified dashboard (logs, traces, metrics) | ❌ Requires separate tools |
| **Service Discovery** | ✅ Automatic (Aspire) | ❌ Manual configuration |
| **Resource Usage** | ~2-3GB RAM (native processes) | ~3-4GB RAM (containers) |
| **CI/CD** | ✅ SDK-based (NuGet packages) | ✅ Docker-only (no SDK needed) |
| **Onboarding** | ✅ Single command (no setup) | ✅ Docker Desktop only |
| **Cross-Platform** | ✅ Windows, macOS, Linux | ✅ Windows, macOS, Linux |

### When to Use Aspire

✅ **Use Aspire for:**
- Daily feature development (fast iteration)
- Debugging across multiple services
- Exploring distributed traces and metrics
- System-wide integration testing

❌ **Use Docker Compose for:**
- CI/CD pipelines (simpler, battle-tested)
- Onboarding without .NET SDK
- Production-like container testing

---

## Troubleshooting

### Issue: Missing Aspire SDK packages

**Error:**
```
The current .NET SDK does not support targeting .NET Aspire 13.1
```

**Solution:**

This error typically means NuGet package restoration failed. Ensure:

```bash
# Restore NuGet packages
dotnet restore

# Verify Aspire.AppHost.Sdk is in Directory.Packages.props
grep "Aspire.AppHost.Sdk" Directory.Packages.props
```

**Note:** Aspire 13.1+ uses the SDK approach (NuGet packages). The deprecated `aspire` workload is **not needed** and should not be installed.

---

### Issue: Port conflicts

**Error:**
```
System.Net.Sockets.SocketException: Address already in use
```

**Solution:**
```bash
# Check what's using the port (e.g., 5231)
lsof -i :5231  # macOS/Linux
netstat -ano | findstr :5231  # Windows

# Kill the conflicting process or change port in AppHost.cs
```

---

### Issue: Services fail to start

**Symptom:** Services show ❌ Unhealthy in Aspire Dashboard

**Debug steps:**
1. Check Console Logs tab for startup errors
2. Verify PostgreSQL and RabbitMQ are healthy
3. Check connection strings in `appsettings.json`
4. Verify all projects build: `dotnet build`

**Common causes:**
- Missing Marten migrations
- RabbitMQ connection timeout
- Port already in use

---

### Issue: Can't access Aspire Dashboard

**Error:**
```
Unable to connect to http://localhost:15000
```

**Solution:**
```bash
# Ensure AppHost is running
dotnet run --project src/CritterSupply.AppHost

# Check AppHost console output for dashboard URL
# Sometimes Aspire uses a different port (15001, 15002, etc.)
```

---

### Issue: OpenTelemetry traces not appearing

**Symptom:** Traces tab is empty in Aspire Dashboard

**Debug steps:**
1. Verify Jaeger is running (check Resources tab)
2. Check `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable
3. Ensure ServiceDefaults is referenced in all APIs
4. Verify `builder.AddServiceDefaults()` is called in Program.cs

**Test with curl:**
```bash
# Send a request to trigger a trace
curl http://localhost:5231/api/orders

# Check Jaeger UI directly
open http://localhost:16686
```

---

### Issue: Service discovery not working

**Symptom:** APIs can't communicate (404 or connection refused)

**Debug steps:**
1. Check Resources tab for service endpoints
2. Verify `.WithReference()` in AppHost.cs
3. Check HttpClient configuration uses service discovery

**Example fix in Storefront.Api:**
```csharp
// ❌ Hardcoded (won't work with Aspire)
client.BaseAddress = new Uri("http://localhost:5236");

// ✅ Service discovery (Aspire injects this)
client.BaseAddress = new Uri(builder.Configuration["services:shopping-api:http:0"]);
```

---

## Advanced Topics

### Running Aspire in Docker

You can containerize Aspire itself for deployment:

```bash
# Build Aspire container image
docker build -f src/CritterSupply.AppHost/Dockerfile -t crittersupply-apphost .

# Run Aspire in container
docker run -p 15000:15000 crittersupply-apphost
```

---

### Customizing Service Configuration

**Override environment variables per service:**

```csharp
var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithEnvironment("Logging__LogLevel__Wolverine", "Debug")  // Verbose Wolverine logs
    .WithEnvironment("ConnectionStrings__marten", customConnectionString);
```

---

### Debugging with Aspire

**Attach debugger to specific service:**

1. Start Aspire: `dotnet run --project src/CritterSupply.AppHost`
2. In Rider/Visual Studio: **Run → Attach to Process**
3. Find process (e.g., `Orders.Api.exe`)
4. Set breakpoints and debug normally

---

### Aspire + TestContainers

Aspire and TestContainers coexist peacefully:

- **Aspire**: For local development and manual testing
- **TestContainers**: For integration tests (hermetic, isolated)

Integration tests **do not** reference AppHost or ServiceDefaults. They use Alba + TestContainers:

```csharp
public class OrdersApiFixture : WebApplicationFactory<Orders.Api.Program>
{
    private PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override connection string with TestContainers
            services.Configure<MartenOptions>(opts =>
            {
                opts.Connection(_postgres.GetConnectionString());
            });
        });
    }
}
```

---

## Stopping Aspire

**To stop all services:**

1. Press `Ctrl+C` in the terminal running AppHost
2. Aspire automatically stops all child processes (APIs, infrastructure)
3. Docker containers (Postgres, RabbitMQ, Jaeger) are stopped and removed

**Verify cleanup:**
```bash
# Should show no CritterSupply processes
ps aux | grep CritterSupply

# Should show no Aspire containers
docker ps | grep aspire
```

---

## Further Reading

- **Aspire Documentation**: https://learn.microsoft.com/en-us/dotnet/aspire/
- **Service Discovery**: https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview
- **Deployment**: https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview
- **ADR 0009**: [docs/decisions/0009-aspire-integration.md](../decisions/0009-aspire-integration.md)
