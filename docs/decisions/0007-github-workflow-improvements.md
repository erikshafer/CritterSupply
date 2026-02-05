# ADR 0007: GitHub Workflow Improvements for Growing Microservices Architecture

**Status:** ⚠️ Proposed (Discussion Document)

**Date:** 2026-02-05

**Context:**

CritterSupply has grown from a simple reference architecture into a sophisticated multi-bounded-context e-commerce system with:
- **16 projects** across 7 bounded contexts (Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog)
- **11 test projects** (integration and unit tests)
- **Infrastructure dependencies:** PostgreSQL, RabbitMQ (via Docker Compose)
- **Future growth:** Customer Experience BFF (Blazor + SSE), Vendor Portal, Vendor Identity, Returns BC

The current GitHub workflow (`dotnet.yml`) is a basic CI pipeline that works well for the current backend-focused architecture but has limitations as the system grows and frontend UIs are introduced.

**Current Workflow Analysis:**

```yaml
# .github/workflows/dotnet.yml (simplified)
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - Checkout code
      - Start containers (docker-compose --profile ci)
      - Cache NuGet packages
      - Setup .NET 10
      - Restore dependencies
      - Build solution
      - Run all tests (--no-build, -parallel none)
      - Stop containers
```

**Strengths:**
- ✅ Simple and maintainable
- ✅ Uses docker-compose for reproducible infrastructure
- ✅ NuGet package caching for faster builds
- ✅ Runs on every PR and push to main
- ✅ 10-minute timeout prevents runaway builds

**Limitations:**
- ❌ Monolithic job - entire solution builds/tests as single unit
- ❌ No parallelization - test suite runs serially (`-parallel none`)
- ❌ No path filtering - all bounded contexts rebuild even if only one changed
- ❌ No deployment automation
- ❌ No frontend build/test steps (upcoming Blazor UI)
- ❌ No Docker image building
- ❌ No environment-specific configurations
- ❌ No performance metrics or test result artifacts
- ❌ No security scanning (CodeQL, dependency scanning)
- ❌ No quality gates (code coverage thresholds)

**Decision:**

This ADR proposes a **phased approach** to modernizing the GitHub workflow to support CritterSupply's growth while maintaining simplicity and avoiding premature optimization.

## Proposed Improvements

### Phase 1: Optimize Current Backend-Only CI (Immediate - No Breaking Changes)

**Goal:** Improve build/test performance without changing architecture

**Changes:**

1. **Add Path-Based Triggering** - Only rebuild affected bounded contexts
   ```yaml
   on:
     pull_request:
       paths:
         - 'src/Order Management/**'
         - 'tests/Order Management/**'
         - '.github/workflows/**'
   ```

2. **Parallelize Tests** - Remove `-parallel none` restriction
   - Current: Tests run serially due to shared Postgres/RabbitMQ
   - Solution: Use Testcontainers per test class (already in use) + remove restriction
   - Expected: 30-50% faster test execution

3. **Add Test Result Artifacts** - Upload test results and logs
   ```yaml
   - name: Upload Test Results
     if: always()
     uses: actions/upload-artifact@v4
     with:
       name: test-results
       path: '**/*.trx'
   ```

4. **Add Build Matrix** - Test against multiple .NET versions (future-proofing)
   ```yaml
   strategy:
     matrix:
       dotnet-version: ['10.0.x']  # Add '11.0.x' when available
   ```

**Benefits:**
- ✅ Faster CI feedback loop
- ✅ Better observability (test artifacts)
- ✅ No architectural changes required
- ✅ Low risk, high reward

**Estimated Effort:** 2-4 hours

---

### Phase 2: Multi-Job Pipeline for Bounded Contexts (Future - Frontend Integration)

**Goal:** Enable independent BC builds and frontend integration

**Trigger:** When Customer Experience BFF (Blazor) is production-ready (Cycle 16+)

**Architecture:**

```yaml
jobs:
  # Detect changes to determine which BCs to build
  changes:
    runs-on: ubuntu-latest
    outputs:
      orders: ${{ steps.filter.outputs.orders }}
      payments: ${{ steps.filter.outputs.payments }}
      shopping: ${{ steps.filter.outputs.shopping }}
      # ... other BCs
      frontend: ${{ steps.filter.outputs.frontend }}
    steps:
      - uses: dorny/paths-filter@v3
        id: filter
        with:
          filters: |
            orders:
              - 'src/Order Management/**'
              - 'tests/Order Management/**'
            payments:
              - 'src/Payment Processing/**'
              - 'tests/Payment Processing/**'
            frontend:
              - 'src/Customer Experience/**'
              - 'tests/Customer Experience/**'

  # Backend BC builds (run in parallel)
  build-orders:
    needs: changes
    if: needs.changes.outputs.orders == 'true'
    runs-on: ubuntu-latest
    steps:
      - Checkout
      - Start infrastructure (docker-compose)
      - Restore/Build/Test Orders BC only
      - Upload test results

  build-payments:
    needs: changes
    if: needs.changes.outputs.payments == 'true'
    # ... similar to build-orders

  # Frontend build (separate job with frontend-specific steps)
  build-frontend:
    needs: changes
    if: needs.changes.outputs.frontend == 'true'
    runs-on: ubuntu-latest
    steps:
      - Checkout
      - Setup .NET 10
      - Build Blazor project
      - Run frontend tests (Bunit, Playwright)
      - Build static assets
      - Upload frontend artifacts

  # Integration smoke tests (runs after all builds pass)
  integration-smoke-tests:
    needs: [build-orders, build-payments, ..., build-frontend]
    runs-on: ubuntu-latest
    steps:
      - Start all services with docker-compose
      - Run end-to-end smoke tests
      - Verify cross-BC integration
```

**Benefits:**
- ✅ Parallel BC builds (5-10 min → 2-3 min typical case)
- ✅ Only build what changed
- ✅ Frontend-specific tooling (Bunit, Playwright)
- ✅ Faster feedback for focused changes
- ✅ Clear separation of concerns

**Drawbacks:**
- ⚠️ More complex workflow (10-15 jobs vs 1)
- ⚠️ Requires careful dependency management
- ⚠️ Harder to debug (need to check multiple job logs)

**Estimated Effort:** 1-2 days

---

### Phase 3: Docker Image Building & Registry (Future - Deployment Readiness)

**Goal:** Build and publish Docker images for each BC

**Trigger:** When deploying to staging/production environments

**Changes:**

1. **Add Dockerfiles** for each BC API project
   ```dockerfile
   # Example: src/Order Management/Orders.Api/Dockerfile
   FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
   WORKDIR /app
   EXPOSE 8080

   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   WORKDIR /src
   COPY ["src/Order Management/Orders.Api/Orders.Api.csproj", "Orders.Api/"]
   COPY ["src/Order Management/Orders/Orders.csproj", "Orders/"]
   COPY ["src/Shared/Messages.Contracts/Messages.Contracts.csproj", "Messages.Contracts/"]
   RUN dotnet restore "Orders.Api/Orders.Api.csproj"
   COPY . .
   WORKDIR "/src/Orders.Api"
   RUN dotnet build -c Release -o /app/build

   FROM build AS publish
   RUN dotnet publish -c Release -o /app/publish

   FROM base AS final
   WORKDIR /app
   COPY --from=publish /app/publish .
   ENTRYPOINT ["dotnet", "Orders.Api.dll"]
   ```

2. **Add Docker Build/Push Steps** to workflow
   ```yaml
   - name: Build and Push Docker Image
     uses: docker/build-push-action@v5
     with:
       context: .
       file: src/Order Management/Orders.Api/Dockerfile
       push: true
       tags: |
         ghcr.io/erikshafer/crittersupply-orders:latest
         ghcr.io/erikshafer/crittersupply-orders:${{ github.sha }}
       cache-from: type=gha
       cache-to: type=gha,mode=max
   ```

3. **Use GitHub Container Registry (GHCR)** for image storage
   - Free for public repositories
   - Integrated with GitHub Actions
   - No external dependencies

**Benefits:**
- ✅ Deployable artifacts produced by CI
- ✅ Immutable versioning (SHA tags)
- ✅ Ready for Kubernetes/container orchestration
- ✅ No external registry costs

**Estimated Effort:** 2-3 days (includes Dockerfile creation for all BCs)

---

### Phase 4: Deployment Automation (Future - Production Deployment)

**Goal:** Automated deployments to staging and production

**Trigger:** When hosting environment is defined (AWS ECS, Azure AKS, GCP GKE, etc.)

**Options:**

**Option A: GitOps with Kubernetes (Recommended for Production)**
```yaml
# .github/workflows/deploy-staging.yml
on:
  push:
    branches: [main]

jobs:
  deploy-staging:
    runs-on: ubuntu-latest
    steps:
      - Checkout
      - Update Kubernetes manifests with new image tags
      - Commit changes to GitOps repo (ArgoCD/Flux)
      - ArgoCD automatically syncs to cluster
```

**Option B: Direct Deployment (Simpler, Less Resilient)**
```yaml
# .github/workflows/deploy-staging.yml
on:
  push:
    branches: [main]

jobs:
  deploy-staging:
    runs-on: ubuntu-latest
    steps:
      - Checkout
      - Configure kubectl
      - Apply Kubernetes manifests
      - Wait for rollout completion
```

**Option C: Managed Services (Least Ops Overhead)**
- AWS App Runner / Azure Container Apps / GCP Cloud Run
- Simplest deployment model (no Kubernetes)
- Auto-scaling, load balancing built-in
- Higher cost, less control

**Estimated Effort:** 1-2 weeks (includes infrastructure setup)

---

### Phase 5: Quality & Security Gates (Future - Enterprise Readiness)

**Goal:** Enforce quality standards and detect vulnerabilities

**Changes:**

1. **CodeQL Security Scanning**
   ```yaml
   - name: Initialize CodeQL
     uses: github/codeql-action/init@v3
     with:
       languages: csharp

   - name: Autobuild
     uses: github/codeql-action/autobuild@v3

   - name: Perform CodeQL Analysis
     uses: github/codeql-action/analyze@v3
   ```

2. **Dependency Scanning**
   ```yaml
   - name: Run Dependabot
     # Configured in .github/dependabot.yml
   ```

3. **Code Coverage Enforcement**
   ```yaml
   - name: Test with Coverage
     run: dotnet test --collect:"XPlat Code Coverage"

   - name: Upload to Codecov
     uses: codecov/codecov-action@v4
     with:
       token: ${{ secrets.CODECOV_TOKEN }}
       fail_ci_if_error: true
   ```

4. **SonarCloud Analysis** (Optional)
   - Static code analysis
   - Technical debt tracking
   - Duplicate code detection

**Benefits:**
- ✅ Early vulnerability detection
- ✅ Code quality metrics
- ✅ Compliance requirements (SOC2, etc.)

**Estimated Effort:** 1-2 days

---

### Phase 6: Performance & Load Testing (Future - Scale Validation)

**Goal:** Automated performance regression detection

**Trigger:** When performance SLAs are defined

**Changes:**

1. **Benchmark Tests with BenchmarkDotNet**
   ```csharp
   [MemoryDiagnoser]
   public class OrderProcessingBenchmarks
   {
       [Benchmark]
       public async Task ProcessOrder() { /* ... */ }
   }
   ```

2. **Load Testing with k6 or Locust**
   ```yaml
   - name: Run Load Tests
     run: k6 run tests/load/checkout-flow.js
   ```

3. **Performance Comparison**
   - Compare against baseline metrics
   - Fail CI if performance degrades >10%

**Estimated Effort:** 3-5 days

---

## Recommended Implementation Order

### Immediate (Before Cycle 17)
1. ✅ **Phase 1: Optimize Current CI** - Low effort, high impact
   - Add test parallelization
   - Add test result artifacts
   - Add basic path filtering

### Short-Term (Cycle 17-18, Q1 2026)
2. ✅ **Phase 5: Security Scanning** - Critical for production
   - Add CodeQL analysis
   - Enable Dependabot
   - Low effort, high security value

### Medium-Term (Cycle 19-22, Q2 2026)
3. ✅ **Phase 2: Multi-Job Pipeline** - After frontend is stable
   - Split BC builds into parallel jobs
   - Add frontend-specific tooling

4. ✅ **Phase 3: Docker Images** - When deployment is planned
   - Create Dockerfiles for all BCs
   - Push to GHCR

### Long-Term (Q3-Q4 2026)
5. ✅ **Phase 4: Deployment Automation** - When hosting is defined
   - Choose hosting platform
   - Implement GitOps or direct deployment

6. ✅ **Phase 6: Performance Testing** - When scale requirements are known
   - Add benchmark tests
   - Integrate load testing

---

## Rationale

**Why Phased Approach?**
- **Avoid over-engineering:** Don't build deployment pipelines before deciding on hosting
- **Learn incrementally:** Each phase provides feedback for the next
- **Maintain velocity:** Don't block feature development on DevOps work
- **Minimize risk:** Small changes are easier to debug and revert

**Why Prioritize Security Early?**
- CodeQL and Dependabot are free and low-effort
- Vulnerabilities are easier to fix early in development
- Establishes security culture before production deployment

**Why Delay Deployment Automation?**
- Hosting platform not yet chosen (AWS vs Azure vs GCP vs bare metal)
- Current focus is feature development (reference architecture)
- Deployment complexity depends on infrastructure choices (Kubernetes vs managed services)

---

## Consequences

### Positive
- ✅ **Faster CI feedback** - Phase 1 improvements reduce build time by 30-50%
- ✅ **Better observability** - Test artifacts and security reports
- ✅ **Scalable architecture** - Multi-job pipeline supports 20+ BCs in future
- ✅ **Security-first** - Early vulnerability detection
- ✅ **Production-ready path** - Clear roadmap to deployment automation

### Negative
- ⚠️ **Increased complexity** - More workflows to maintain (mitigated by phased approach)
- ⚠️ **Learning curve** - Team needs to understand new tooling (CodeQL, Docker, k8s)
- ⚠️ **Potential over-engineering** - Risk of building features that aren't needed (mitigated by demand-driven phases)

### Neutral
- 🔄 **Ongoing maintenance** - Workflows need periodic updates as GitHub Actions evolves
- 🔄 **Documentation burden** - Need to document workflow architecture (this ADR helps)

---

## Alternatives Considered

### Alternative 1: Keep Current Simple Workflow
**Pros:** Zero effort, zero risk
**Cons:** Doesn't scale, no deployment automation, slow feedback loop
**Verdict:** ❌ Rejected - System is already outgrowing current workflow

### Alternative 2: Full Microservices CI/CD Immediately
**Pros:** Future-proof, enterprise-grade
**Cons:** Massive over-engineering for current needs, 4-6 weeks of work
**Verdict:** ❌ Rejected - Violates YAGNI principle, blocks feature development

### Alternative 3: Use Third-Party CI Platform (CircleCI, Jenkins, etc.)
**Pros:** More features, more flexibility
**Cons:** Additional cost, external dependency, learning curve
**Verdict:** ❌ Rejected - GitHub Actions is sufficient, free for public repos

### Alternative 4: Monorepo CI Tools (Nx, Turborepo)
**Pros:** Smart caching, dependency graph analysis
**Cons:** Adds dependency, overkill for .NET ecosystem
**Verdict:** ❌ Rejected - .NET has excellent built-in caching via MSBuild

---

## References

- **Current Workflow:** [.github/workflows/dotnet.yml](../../.github/workflows/dotnet.yml)
- **Bounded Contexts:** [CONTEXTS.md](../../CONTEXTS.md)
- **Test Infrastructure:** `skills/testcontainers-integration-tests.md`
- **Customer Experience Plan:** [docs/planning/cycles/cycle-16-customer-experience.md](../planning/cycles/cycle-16-customer-experience.md)
- **GitHub Actions Best Practices:** https://docs.github.com/en/actions/learn-github-actions/best-practices-for-workflows
- **Docker Multi-Stage Builds:** https://docs.docker.com/build/building/multi-stage/
- **GitHub Container Registry:** https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry

---

## Implementation Plan (Phase 1 - Immediate)

If approved, Phase 1 can be implemented immediately with these concrete steps:

### 1. Enable Test Parallelization (15 minutes)
```yaml
# Change in .github/workflows/dotnet.yml
- name: Test
-  run: dotnet test --no-build --logger:"console;verbosity=normal" -- -parallel none
+  run: dotnet test --no-build --logger:"console;verbosity=normal" --collect:"Code Coverage"
```

### 2. Add Test Result Artifacts (10 minutes)
```yaml
- name: Test
  run: dotnet test --no-build --logger:"trx;LogFileName=test-results.trx"

- name: Upload Test Results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-results
    path: '**/test-results.trx'
```

### 3. Add Path-Based Triggering (20 minutes)
```yaml
on:
  pull_request:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - 'tests/**'
      - '.github/workflows/**'
      - '*.props'
      - '*.slnx'
    paths-ignore:
      - '**.md'
      - 'docs/**'
```

### 4. Add CodeQL Security Scanning (30 minutes)
```yaml
# New file: .github/workflows/codeql.yml
name: CodeQL Security Analysis

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]
  schedule:
    - cron: '0 0 * * 1'  # Weekly on Mondays

jobs:
  analyze:
    name: Analyze
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      actions: read
      contents: read

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: csharp

      - name: Autobuild
        uses: github/codeql-action/autobuild@v3

      - name: Perform CodeQL Analysis
        uses: github/codeql-action/analyze@v3
```

### Total Effort for Phase 1: ~2 hours

---

## Next Steps

1. **Review and discuss** this ADR with the team
2. **Get approval** for Phase 1 implementation
3. **Implement Phase 1** improvements (2 hours)
4. **Measure impact** - Compare CI times before/after
5. **Plan Phase 2** when Customer Experience BFF is production-ready
6. **Revisit this ADR** quarterly to adjust priorities

---

## Discussion Points

Questions to consider before implementation:

1. **Is Phase 1 approved for immediate implementation?**
   - Low risk, high reward improvements
   - No architectural changes

2. **When should Phase 2 (multi-job pipeline) be implemented?**
   - Recommendation: After Cycle 16 (Customer Experience BFF) is complete
   - Gives us concrete frontend build requirements

3. **What is the target hosting platform for deployment?**
   - AWS ECS / EKS?
   - Azure Container Apps / AKS?
   - GCP Cloud Run / GKE?
   - Self-hosted Kubernetes?
   - Impacts Phase 4 implementation

4. **Are there specific performance SLAs that need validation?**
   - Impacts Phase 6 priority
   - Example: Checkout must complete in <2 seconds under 100 concurrent users

5. **Are there compliance requirements (SOC2, HIPAA, etc.)?**
   - May prioritize Phase 5 (security scanning) earlier
   - May require additional audit logging

---

**Status Note:** This is a **discussion document** to begin the conversation about future workflow improvements. No implementation changes have been made yet. The goal is to get feedback and align on priorities before investing engineering time.
