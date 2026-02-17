# Microsoft Aspire v13.1 Implementation Analysis
## CritterSupply Project - Executive Summary

**Date:** 2026-02-16  
**Analysis By:** GitHub Copilot  
**Project:** CritterSupply (.NET 10.0 Event-Driven E-Commerce Reference Architecture)

---

## 📊 Executive Summary

Implementing Microsoft Aspire v13.1 in CritterSupply is **feasible and recommended** with **medium complexity** and **4-6 hours estimated effort**. The solution's architecture is well-suited for Aspire adoption with minimal breaking changes required.

### Quick Assessment

| Category | Rating | Notes |
|----------|--------|-------|
| **Feasibility** | ✅ High | .NET 10.0 fully supports Aspire 13.1 |
| **Complexity** | ⚠️ Medium | Additive changes, minimal refactoring |
| **Effort** | 🕒 4-6 hours | One focused development session |
| **Risk** | ✅ Low | Preserves existing Docker/TestContainers workflows |
| **ROI** | 🚀 High | Dramatically improves developer experience |

---

## 🎯 What This Implementation Provides

### Developer Experience Improvements

**Before (Current State):**
```bash
# 10+ manual steps to start development
docker-compose --profile all up -d
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
dotnet run --project "src/Payments/Payments.Api/Payments.Api.csproj"
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"
# ... 6 more APIs + Blazor app (9 total commands)
```

**After (With Aspire):**
```bash
# Single command starts everything
cd src/CritterSupply.AppHost
dotnet run

# Aspire dashboard opens → All services visible with:
# - Real-time health checks
# - Aggregated logs and traces
# - Service dependency graph
# - Automatic service discovery
```

### Key Benefits

1. **Unified Dashboard** - Single pane of glass for all 9 services + infrastructure
2. **Service Discovery** - No hardcoded connection strings or ports in code
3. **OpenTelemetry Built-In** - Distributed tracing, metrics, and structured logging
4. **Modern .NET Best Practice** - Aligns with Microsoft's cloud-native direction
5. **Zero Breaking Changes** - Docker Compose and TestContainers continue working

---

## 🏗️ Current Architecture Analysis

### What Makes CritterSupply Aspire-Friendly

✅ **Already .NET 10.0** - No framework upgrade needed  
✅ **Central Package Management** - Easy to add Aspire packages  
✅ **Consistent API Structure** - All 8 APIs follow same pattern  
✅ **Schema-per-BC Pattern** - Single Postgres DB works perfectly with Aspire  
✅ **RabbitMQ Integration** - Wolverine auto-provisioning compatible with Aspire  
✅ **TestContainers for Tests** - Hermetic tests unaffected by Aspire  

### Current Project Inventory

| Component | Count | Status |
|-----------|-------|--------|
| **API Projects** | 8 | Orders, Payments, Shopping, Inventory, Fulfillment, CustomerIdentity, ProductCatalog, Storefront.Api |
| **Blazor Web App** | 1 | Storefront.Web (customer-facing UI) |
| **Infrastructure** | 2 | PostgreSQL (port 5433), RabbitMQ (ports 5672/15672) |
| **Test Projects** | 11 | Integration + Unit tests using Alba + TestContainers |
| **Bounded Contexts** | 8 | Event-sourced domains with Marten + Wolverine |

---

## 🔧 Implementation Overview

### What Gets Created (New Projects)

1. **CritterSupply.AppHost** - Aspire orchestration project  
   - Configures all 9 services + Postgres + RabbitMQ
   - ~100 lines of code

2. **CritterSupply.ServiceDefaults** - Shared Aspire configuration  
   - OpenTelemetry, health checks, service discovery
   - ~150 lines of code

### What Gets Modified

| File Type | Count | Change Type | Complexity |
|-----------|-------|-------------|------------|
| **API Program.cs** | 8 | Add 2 lines | Low |
| **Blazor Program.cs** | 1 | Add 3 lines + service discovery | Low |
| **.csproj Files** | 9 | Add ServiceDefaults reference | Low |
| **Directory.Packages.props** | 1 | Add 6 Aspire package versions | Low |
| **Solution File** | 1 | Add 2 new projects | Low |

**Total Line Changes:** ~50 lines added across 20 files (surgical modifications)

### What Stays Unchanged

✅ **Domain Logic** - Zero changes to handlers, aggregates, events  
✅ **Wolverine Configuration** - Message handling continues as-is  
✅ **Marten Configuration** - Event sourcing/document store unchanged  
✅ **Integration Tests** - TestContainers still used for test isolation  
✅ **CI/CD Workflow** - GitHub Actions continues using Docker Compose  
✅ **appsettings.json** - Configuration files remain valid (Aspire adds on top)

---

## ⚠️ Potential Gotchas & Mitigations

### 1. Service Discovery Learning Curve

**Gotcha:** Developers must understand how Aspire injects connection strings.

**Example:**
```csharp
// Before (hardcoded)
client.BaseAddress = new Uri("http://localhost:5237");

// After (service discovery)
client.BaseAddress = new Uri(
    builder.Configuration["services:storefront-api:http:0"] 
    ?? "http://localhost:5237"); // Fallback for non-Aspire
```

**Mitigation:**
- Include fallback URLs for non-Aspire scenarios (Docker Compose still works)
- Document configuration key format in ASPIRE-GUIDE.md
- Add code comments explaining Aspire service discovery

### 2. PostgreSQL Schema-per-BC Pattern

**Gotcha:** CritterSupply uses single Postgres DB with multiple schemas (one per BC).

**Confirmation:** ✅ **No issue** - Aspire's `.AddDatabase("crittersupply")` creates shared DB. Each BC already configures its own schema via `opts.DatabaseSchemaName`.

**Example (Orders BC):**
```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(martenConnectionString); // Aspire-injected connection string
    opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant(); // "orders" schema
    // ... rest unchanged
});
```

### 3. RabbitMQ Queue Provisioning

**Gotcha:** Wolverine auto-provisions RabbitMQ queues. Does Aspire interfere?

**Confirmation:** ✅ **No issue** - Aspire only starts RabbitMQ container. Wolverine's `.AutoProvision()` continues working.

**Example:**
```csharp
// Wolverine configuration unchanged
opts.UseRabbitMq(rabbit =>
{
    // Aspire injects hostname/port via configuration
    rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
    // ... rest unchanged
})
.AutoProvision(); // Wolverine creates queues as before
```

### 4. TestContainers Independence

**Gotcha:** Integration tests use TestContainers for isolated infrastructure. Will Aspire break tests?

**Confirmation:** ✅ **No issue** - Test projects DON'T reference AppHost or ServiceDefaults. Tests remain hermetic.

**Design Decision:**
- Tests continue using TestContainers for Postgres/RabbitMQ
- No dependency on Aspire AppHost for test execution
- CI/CD continues using Docker Compose (no Aspire workload needed)

### 5. Port Allocation

**Gotcha:** Aspire may assign different ports than currently hardcoded (52XX series).

**Impact:** Low - Service discovery eliminates need for hardcoded ports. Only affects developers directly calling APIs via curl/Postman.

**Mitigation:**
- Aspire dashboard shows actual port assignments
- Update `.http` test files to reference `{{port}}` variables
- Document port lookup in ASPIRE-GUIDE.md

### 6. Aspire Workload Installation

**Gotcha:** Developers must install Aspire workload (`dotnet workload install aspire`).

**Impact:** One-time setup step (30 seconds).

**Mitigation:**
- Add to README.md prerequisites
- Include in onboarding documentation
- Fallback to Docker Compose if workload not installed

---

## 📈 Effort Breakdown

### Time Estimates

| Phase | Duration | Tasks |
|-------|----------|-------|
| **Setup** | 1-2 hours | Create AppHost/ServiceDefaults projects, add to solution, update Directory.Packages.props |
| **API Updates** | 2-3 hours | Add ServiceDefaults reference to 9 projects, update Program.cs (2 lines each) |
| **Testing** | 1 hour | Verify Aspire launch, run integration tests, test Blazor UI, validate RabbitMQ |
| **Documentation** | 30 mins | Update README.md, CLAUDE.md, create ASPIRE-GUIDE.md |

**Total:** 4-6 hours (single focused session)

### Complexity Justification

**Why Medium (not Low):**
- 9 projects to update (API changes are trivial but count adds up)
- Service discovery requires understanding new configuration pattern
- Testing across 8 bounded contexts + Blazor UI
- Documentation updates for developer onboarding

**Why Medium (not High):**
- No domain logic changes (pure infrastructure)
- Aspire is additive (not replacing existing infrastructure)
- TestContainers remain unchanged (low testing risk)
- Rollback is simple (remove 2 projects, revert Program.cs changes)

---

## 🚀 Recommended Implementation Plan

### Phase 1: Foundation (1-2 hours)

1. Install Aspire workload: `dotnet workload install aspire`
2. Add Aspire packages to `Directory.Packages.props`
3. Create `CritterSupply.ServiceDefaults` project
4. Create `CritterSupply.AppHost` project
5. Add projects to solution file

### Phase 2: Integration (2-3 hours)

6. Update all 8 API projects (add ServiceDefaults reference, update Program.cs)
7. Update Storefront.Web (add ServiceDefaults reference, update service discovery)
8. Configure AppHost with all BCs, Postgres, RabbitMQ

### Phase 3: Validation (1 hour)

9. Build solution: `dotnet build`
10. Run AppHost: `cd src/CritterSupply.AppHost && dotnet run`
11. Verify Aspire dashboard at `http://localhost:15000`
12. Test Blazor UI end-to-end (cart → checkout → order)
13. Verify RabbitMQ message flow
14. Run integration tests: `dotnet test` (should pass unchanged)

### Phase 4: Documentation (30 mins)

15. Update README.md with Aspire instructions
16. Update CLAUDE.md with Aspire patterns
17. Create ADR 0009 (Aspire Integration decision record)
18. Create ASPIRE-GUIDE.md (developer reference)

---

## ✅ Acceptance Criteria

Implementation is complete when:

- [ ] `dotnet run` in AppHost starts all 9 services + infrastructure
- [ ] Aspire dashboard opens at `http://localhost:15000` showing all resources
- [ ] All services show "Running" status with green health checks
- [ ] Storefront.Web accessible and functional (cart + checkout flows)
- [ ] RabbitMQ messages flow correctly (verify in RabbitMQ Management UI)
- [ ] `dotnet test` passes all integration tests unchanged
- [ ] Docker Compose workflow still works (fallback option)
- [ ] GitHub Actions CI/CD pipeline passes unchanged
- [ ] Documentation updated (README, CLAUDE.md, ADR, ASPIRE-GUIDE)

---

## 🎯 Deliverables

### Code Changes

1. **New Projects** (2)
   - `src/CritterSupply.AppHost/` - Orchestration
   - `src/CritterSupply.ServiceDefaults/` - Shared configuration

2. **Modified Projects** (9)
   - All 8 API projects + Storefront.Web (minor Program.cs changes)

3. **Updated Files** (2)
   - `Directory.Packages.props` - Add Aspire packages
   - `CritterSupply.slnx` - Add new projects

### Documentation

1. **ADR 0009** - Aspire Integration decision record
2. **ASPIRE-IMPLEMENTATION-GUIDE.md** - Step-by-step instructions
3. **ASPIRE-SUMMARY.md** - This executive summary (can be shared with stakeholders)
4. **README.md** - Updated with Aspire quick start
5. **CLAUDE.md** - Updated with Aspire development guidelines

---

## 🔄 Rollback Strategy

If Aspire causes issues, rollback is straightforward:

1. **Remove new projects** (2 commands)
   ```bash
   dotnet sln remove src/CritterSupply.AppHost/CritterSupply.AppHost.csproj
   dotnet sln remove src/CritterSupply.ServiceDefaults/CritterSupply.ServiceDefaults.csproj
   rm -rf src/CritterSupply.AppHost
   rm -rf src/CritterSupply.ServiceDefaults
   ```

2. **Revert API projects** (remove 2 lines per Program.cs)
   ```diff
   - builder.AddServiceDefaults();
   - app.MapDefaultEndpoints();
   ```

3. **Revert Storefront.Web** (restore hardcoded URL)
   ```diff
   - client.BaseAddress = new Uri(builder.Configuration["services:storefront-api:http:0"] ?? "http://localhost:5237");
   + client.BaseAddress = new Uri("http://localhost:5237");
   ```

4. **Continue using Docker Compose** (existing workflow)

**Rollback Time:** 30 minutes

---

## 🏁 Conclusion

### Recommendation: **✅ Proceed with Aspire Implementation**

**Justification:**
- Low risk (preserves existing workflows)
- High value (dramatically improves developer experience)
- Manageable effort (4-6 hours)
- Aligns with .NET best practices (future-proof)
- No breaking changes to core architecture

### Next Steps

1. **Schedule Implementation** - Book 4-6 hour focused session
2. **Install Prerequisites** - Ensure `dotnet workload install aspire` on dev machine
3. **Follow Implementation Guide** - Use `docs/ASPIRE-IMPLEMENTATION-GUIDE.md`
4. **Validate with Team** - Demo Aspire dashboard to team after implementation
5. **Gather Feedback** - Monitor developer experience for 1-2 weeks

### Success Metrics

After 2 weeks of Aspire usage, measure:
- **Time to start full stack** (Before: ~5 mins, After: ~30 seconds)
- **Developer satisfaction** (survey team on DX improvements)
- **Onboarding time** (new developers getting up to speed)
- **Debugging efficiency** (unified logs/traces vs scattered terminals)

---

## 📚 References

- **ADR 0009:** Aspire Integration Decision Record (`docs/decisions/0008-aspire-integration.md`)
- **Implementation Guide:** Step-by-step instructions (`docs/ASPIRE-IMPLEMENTATION-GUIDE.md`)
- **Official Docs:** [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- **Aspire 13.1 Release:** [What's New](https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/aspire-13)

---

**Document Version:** 1.0  
**Last Updated:** 2026-02-16  
**Status:** Analysis Complete - Ready for Implementation
