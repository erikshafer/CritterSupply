# Aspire vs Docker Compose: Quick Reference

This document provides a side-by-side comparison of development workflows with and without Aspire.

## Quick Comparison Table

| Aspect | Without Aspire (Current) | With Aspire |
|--------|-------------------------|-------------|
| **Startup Command** | `docker-compose up -d` + 9x `dotnet run` | `cd src/CritterSupply.AppHost && dotnet run` |
| **Time to Start** | ~5 minutes | ~30 seconds |
| **Service Visibility** | Scattered terminals | Unified dashboard at http://localhost:15000 |
| **Logs** | Multiple terminal windows | Single aggregated view |
| **Health Checks** | Manual verification | Real-time dashboard |
| **Service Discovery** | Hardcoded URLs/ports | Automatic configuration injection |
| **Debugging** | Attach to individual processes | Integrated in Aspire dashboard |
| **Infrastructure** | Docker Compose YAML | C# code in AppHost |
| **CI/CD Impact** | None (continues using Docker Compose) | None (continues using Docker Compose) |
| **Test Impact** | None (TestContainers unchanged) | None (TestContainers unchanged) |
| **Learning Curve** | Low (familiar docker-compose) | Medium (new Aspire concepts) |
| **Developer Experience** | Manual, fragmented | Integrated, streamlined |

## Developer Workflow Comparison

### Scenario 1: Starting Development Environment

#### Without Aspire (Current)
```bash
# Step 1: Start infrastructure
docker-compose --profile all up -d

# Step 2: Wait for Postgres/RabbitMQ to be ready
sleep 10

# Step 3: Start each API (8 commands)
dotnet run --project "src/Order Management/Orders.Api/Orders.Api.csproj" &
dotnet run --project "src/Payment Processing/Payments.Api/Payments.Api.csproj" &
dotnet run --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj" &
dotnet run --project "src/Inventory Management/Inventory.Api/Inventory.Api.csproj" &
dotnet run --project "src/Fulfillment Management/Fulfillment.Api/Fulfillment.Api.csproj" &
dotnet run --project "src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj" &
dotnet run --project "src/Product Catalog/ProductCatalog.Api/ProductCatalog.Api.csproj" &
dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj" &

# Step 4: Start Blazor Web
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"

# Step 5: Manually track PIDs to stop later
```

**Total Steps:** 11 commands  
**Terminals Needed:** 9-10 (one per service)  
**Time:** ~5 minutes

#### With Aspire
```bash
# Single command starts everything
cd src/CritterSupply.AppHost
dotnet run

# Browser opens to Aspire dashboard
# All services visible, logs aggregated, health checks real-time
```

**Total Steps:** 2 commands  
**Terminals Needed:** 1  
**Time:** ~30 seconds

---

### Scenario 2: Checking Service Health

#### Without Aspire (Current)
```bash
# Check each API individually
curl http://localhost:5231/health  # Orders
curl http://localhost:5232/health  # Payments
curl http://localhost:5236/health  # Shopping
curl http://localhost:5233/health  # Inventory
curl http://localhost:5234/health  # Fulfillment
curl http://localhost:5235/health  # CustomerIdentity
curl http://localhost:5133/health  # ProductCatalog
curl http://localhost:5237/health  # Storefront.Api
curl http://localhost:5238          # Storefront.Web

# Check Docker containers
docker ps | grep crittersupply
```

**Total Steps:** 10 commands  
**Time:** 2-3 minutes

#### With Aspire
```bash
# Open dashboard (if not already open)
open http://localhost:15000

# View Resources tab → see all services with health status
# Green = healthy, Red = unhealthy
```

**Total Steps:** 1 action (click tab in dashboard)  
**Time:** 5 seconds

---

### Scenario 3: Viewing Logs

#### Without Aspire (Current)
```bash
# View logs from each terminal window (9 windows)
# OR use docker-compose logs (only shows infrastructure)
docker-compose logs -f postgres
docker-compose logs -f rabbitmq

# For API logs, must go to each terminal
# OR redirect to files:
dotnet run ... > orders.log 2>&1 &
dotnet run ... > payments.log 2>&1 &
# ... repeat for all
tail -f orders.log payments.log shopping.log ...
```

**Total Steps:** Multiple terminal windows OR log file management  
**Time:** Variable, depends on which service is having issues

#### With Aspire
```bash
# Dashboard → Console tab
# All logs from all services in one view
# Filter by service, log level, search text
```

**Total Steps:** Navigate to Console tab  
**Time:** 5 seconds

---

### Scenario 4: Debugging RabbitMQ Integration

#### Without Aspire (Current)
```bash
# Step 1: Open RabbitMQ Management UI
open http://localhost:15672

# Step 2: Check queues created
# Manually navigate to Queues tab

# Step 3: Check if message was published
# View queue message count

# Step 4: Check which service published
# Grep through API logs in each terminal
grep "OrderPlaced" orders-terminal-output.log
grep "OrderPlaced" storefront-terminal-output.log

# Step 5: Check if message was consumed
# Grep consumer logs
grep "Handling message" storefront-terminal-output.log
```

**Total Steps:** 5+ actions across multiple UIs  
**Time:** 5-10 minutes

#### With Aspire
```bash
# Dashboard → Traces tab
# See distributed trace from Orders API → RabbitMQ → Storefront.Api
# Automatic correlation of message publish/consume
# View timing, errors, full trace details
```

**Total Steps:** Navigate to Traces tab, filter by trace  
**Time:** 30 seconds

---

### Scenario 5: Adding New Bounded Context

#### Without Aspire (Current)
```bash
# 1. Create new API project
dotnet new webapi -n NewBC.Api

# 2. Add Wolverine, Marten packages
# Edit .csproj

# 3. Configure Program.cs
# Copy/paste from existing API, modify

# 4. Choose unique port (find next available)
# Edit Properties/launchSettings.json

# 5. Update appsettings.json with connection strings
# Edit appsettings.json

# 6. Add to solution
dotnet sln add src/NewBC/NewBC.Api/NewBC.Api.csproj

# 7. Update docker-compose.yml (if needed)
# Manual edit

# 8. Remember to run manually
dotnet run --project "src/NewBC/NewBC.Api/NewBC.Api.csproj"
```

**Total Steps:** 8 steps, manual configuration  
**Opportunity for Errors:** High (port conflicts, typos)

#### With Aspire
```bash
# 1-6. Same as without Aspire

# 7. Add to AppHost (single line)
# Edit src/CritterSupply.AppHost/Program.cs

var newBcApi = builder.AddProject<Projects.NewBC_Api>("newbc-api")
    .WithReference(postgres)
    .WithReference(rabbitmq);

# 8. Run AppHost
cd src/CritterSupply.AppHost
dotnet run

# New service automatically included, port assigned, dashboard shows it
```

**Total Steps:** 7 steps (one less than without Aspire)  
**Opportunity for Errors:** Lower (no port conflicts, Aspire assigns ports)

---

### Scenario 6: Running Integration Tests

#### Without Aspire (Current)
```bash
# Tests use TestContainers (hermetic)
dotnet test

# TestContainers spins up Postgres/RabbitMQ per test
# Aspire NOT involved
```

**Total Steps:** 1 command  
**Time:** Varies (depends on test suite)

#### With Aspire
```bash
# Tests STILL use TestContainers (unchanged)
dotnet test

# TestContainers spins up Postgres/RabbitMQ per test
# Aspire NOT involved
```

**Total Steps:** 1 command  
**Time:** Varies (same as without Aspire)

**Note:** Tests remain hermetic. No change.

---

### Scenario 7: CI/CD (GitHub Actions)

#### Without Aspire (Current)
```yaml
# .github/workflows/dotnet.yml
steps:
  - name: Start containers
    run: docker compose --profile ci up -d
  
  - name: Build
    run: dotnet build
  
  - name: Test
    run: dotnet test
```

**Total Steps:** 3 GitHub Actions steps  
**Works:** ✅ Yes

#### With Aspire
```yaml
# .github/workflows/dotnet.yml (UNCHANGED)
steps:
  - name: Start containers
    run: docker compose --profile ci up -d
  
  - name: Build
    run: dotnet build
  
  - name: Test
    run: dotnet test
```

**Total Steps:** 3 GitHub Actions steps (same)  
**Works:** ✅ Yes

**Note:** CI/CD unchanged. Continues using Docker Compose.

---

## Feature Matrix

| Feature | Without Aspire | With Aspire |
|---------|---------------|-------------|
| **Service Orchestration** | Manual | Automatic |
| **Service Discovery** | ❌ Hardcoded URLs | ✅ Automatic |
| **Unified Dashboard** | ❌ No | ✅ Yes |
| **Distributed Tracing** | ❌ Manual setup | ✅ Built-in (OpenTelemetry) |
| **Structured Logging** | ⚠️ Manual setup | ✅ Built-in (OpenTelemetry) |
| **Metrics Collection** | ❌ Manual setup | ✅ Built-in (OpenTelemetry) |
| **Health Monitoring** | ⚠️ Manual curl | ✅ Real-time dashboard |
| **Dependency Graph** | ❌ No | ✅ Visual in dashboard |
| **Resource Management** | Docker Compose YAML | C# AppHost code |
| **IDE Integration** | ⚠️ Limited | ✅ Visual Studio 2022, Rider |
| **Production Deployment** | Docker Compose | Azure Container Apps (via Aspire manifest) |
| **Local Development** | ✅ Supported | ✅ Supported |
| **CI/CD** | ✅ Docker Compose | ✅ Docker Compose (unchanged) |
| **Testing** | ✅ TestContainers | ✅ TestContainers (unchanged) |

---

## Prerequisites Comparison

| Requirement | Without Aspire | With Aspire |
|-------------|---------------|-------------|
| **.NET SDK** | ✅ 10.0+ | ✅ 10.0+ |
| **Docker Desktop** | ✅ Required | ✅ Required |
| **Aspire Workload** | ❌ Not needed | ✅ `dotnet workload install aspire` |
| **IDE** | Any | Visual Studio 2022 17.9+ OR JetBrains Rider 2024.1+ (optional, for best DX) |

---

## When to Use Which Approach

### Use Aspire (Local Development)
✅ Daily development work  
✅ Debugging distributed flows  
✅ Onboarding new developers  
✅ Demonstrating architecture to stakeholders  
✅ Exploring OpenTelemetry traces/metrics  

### Use Docker Compose (Infrastructure Only)
✅ CI/CD environments (GitHub Actions)  
✅ Running only Postgres/RabbitMQ (when working on single BC)  
✅ Environments where Aspire workload can't be installed  
✅ Developers who prefer manual control  

### Use TestContainers (Always for Tests)
✅ Integration tests  
✅ Hermetic test infrastructure  
✅ Parallel test execution  

---

## Migration Path

### Step 1: Add Aspire (Optional, Developers Can Opt-In)
- Implement Aspire AppHost + ServiceDefaults
- Developers can choose: `dotnet run` in AppHost OR `docker-compose up -d` + manual

### Step 2: Transition Period (1-2 Weeks)
- Both workflows supported
- Gather feedback from team
- Address any Aspire issues

### Step 3: Aspire Becomes Recommended (Not Required)
- Update README.md to list Aspire first
- Docker Compose remains fallback option

### Step 4: Future (Optional)
- Deprecate Docker Compose for local dev (if team prefers Aspire)
- Keep for CI/CD indefinitely

---

## Summary

**Bottom Line:**
- Aspire dramatically improves local development experience (10+ commands → 1)
- Docker Compose continues working for CI/CD and as fallback
- TestContainers unchanged (tests remain hermetic)
- Both approaches coexist peacefully

**Recommendation:** Implement Aspire for local dev, preserve Docker Compose for CI/CD and fallback.
