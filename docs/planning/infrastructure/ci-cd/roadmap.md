# GitHub Workflow Improvement Roadmap

> **Quick Reference:** This document summarizes the phased approach to improving CritterSupply's CI/CD pipeline as detailed in [ADR 0007](../decisions/0007-github-workflow-improvements.md).

---

## Current State (as of 2026-03-11) — Phases 1 & 2 Implemented

```
┌─────────────────────────────────────────────────────────────────┐
│   Two Parallel Jobs (unit-tests + integration-tests)            │
│                                                                 │
│  ┌──────────────────────────┐  ┌───────────────────────────┐   │
│  │     unit-tests job       │  │  integration-tests job    │   │
│  │ 1. Checkout code         │  │ 1. Checkout code          │   │
│  │ 2. Restore NuGet (cache) │  │ 2. Restore NuGet (cache)  │   │
│  │ 3. Build solution        │  │ 3. Build solution         │   │
│  │ 4. Run unit tests        │  │ 4. Run integration tests  │   │
│  │ 5. Upload .trx artifacts │  │    (Testcontainers)       │   │
│  │                          │  │ 5. Upload .trx artifacts  │   │
│  └──────────────────────────┘  └───────────────────────────┘   │
│  Runtime: ~3-4 minutes          Runtime: ~8-10 minutes         │
│                                                                 │
│  ✅ Path-based triggering (skips docs changes)                  │
│  ✅ Test result artifacts (.trx)                                │
│  ✅ Parallel jobs (faster feedback)                             │
│  ✅ Concurrency group (cancels stale runs)                      │
│  ✅ CodeQL scanning (codeql.yml)                                │
└─────────────────────────────────────────────────────────────────┘
```

**Total wall-clock time:** ~8-10 minutes (parallel — limited by integration-tests job)

---

## Previous State (as of 2026-02-05)

```
┌─────────────────────────────────────────┐
│   Single Monolithic CI Job             │
│                                         │
│  1. Checkout code                       │
│  2. Start Postgres + RabbitMQ           │
│  3. Restore NuGet packages              │
│  4. Build entire solution               │
│  5. Run all tests (serial)              │
│  6. Stop containers                     │
│                                         │
│  Runtime: ~8-10 minutes                 │
└─────────────────────────────────────────┘
```

**Limitations (now resolved):**
- 🐌 Everything rebuilds even if only one BC changes
- 🐌 Tests run serially (no parallelization)
- ❌ No deployment automation
- ❌ No security scanning
- ❌ No frontend build steps (needed for Cycle 16+)

---

## Future State (Phased Roadmap)

### Phase 1: Quick Wins (Immediate - 2 hours effort) ✅ IMPLEMENTED (2026-03-11)

**Goal:** Improve performance without architectural changes

**Changes:**
- ✅ Enable test parallelization (remove `-parallel none`)
- ✅ Add test result artifacts (upload `.trx` files)
- ✅ Add path-based triggering (skip docs-only changes)
- ✅ Add CodeQL security scanning (`.github/workflows/codeql.yml`)

**Expected Benefits:**
- ⚡ 30-50% faster test execution
- 🔍 Better observability (test artifacts)
- 🛡️ Early security vulnerability detection

**Timeline:** Can be implemented immediately

---

### Phase 2: Multi-Job Pipeline (After Frontend is Stable) ✅ IMPLEMENTED (2026-03-11)

**Goal:** Enable independent BC builds and frontend integration

**Trigger:** After Cycle 16 (Customer Experience BFF) is complete — ✅ Now complete

**Architecture implemented:**
```
PR / Push to main
        │
        ├──────────────────────────────────────────────┐
        │                                              │
        ▼                                              ▼
┌────────────────────┐                   ┌─────────────────────────┐
│   Unit Tests Job   │                   │ Integration Tests Job   │
│  (build + xUnit)   │                   │  (build + Testcontainers│
│  No Docker needed  │                   │   integration tests)    │
│  ~3-4 minutes      │                   │  ~8-10 minutes          │
└────────────────────┘                   └─────────────────────────┘
        │                                              │
        ▼                                              ▼
  Test results .trx                         Test results .trx
  (uploaded artifact)                       (uploaded artifact)
```

**Key Decisions:**
- ✅ All test fixtures use Testcontainers for Postgres (no docker-compose needed in CI)
- ✅ All test fixtures call `DisableAllExternalWolverineTransports()` (no RabbitMQ needed in CI)
- ✅ Two parallel jobs: `unit-tests` and `integration-tests` run in parallel
- ✅ `concurrency` group added to cancel stale runs on same branch/PR

**Benefits achieved:**
- ⚡ Parallel builds (total time ≈ max of both jobs instead of sum)
- 🎯 Separate status indicators in GitHub Actions UI
- 🔍 Distinct visibility: unit test failures vs integration test failures
- 🧹 Removed unnecessary `docker compose --profile ci` step (not needed — Testcontainers handles its own containers)

---

### Phase 3: Docker Images (When Deployment is Planned)

**Goal:** Build and publish container images for deployment

**Trigger:** When deployment to staging/production is planned

**Changes:**
- Create Dockerfile for each BC API project
- Push images to GitHub Container Registry (GHCR)
- Use SHA-based tags for immutability

**Benefits:**
- 📦 Deployable artifacts produced by CI
- 🔒 Immutable versioning (`sha-abc123`)
- ☸️ Ready for Kubernetes/container orchestration

**Timeline:** When hosting platform is chosen

---

### Phase 4: Deployment Automation (When Infrastructure is Ready)

**Goal:** Automated deployments to staging and production

**Trigger:** When hosting environment is defined

**Options:**
```
Option A: GitOps (Kubernetes)
  CI → Build Images → Update GitOps Repo → ArgoCD Syncs → Cluster

Option B: Direct Deploy
  CI → Build Images → kubectl apply → Verify Rollout

Option C: Managed Services
  CI → Build Images → Push to AWS App Runner / Azure Container Apps
```

**Timeline:** When infrastructure decisions are made

---

### Phase 5: Quality & Security Gates (Ongoing)

**Goal:** Enforce quality standards

**Changes:**
- ✅ CodeQL security scanning (Phase 1)
- 📊 Code coverage enforcement (Codecov)
- 🔍 Dependency scanning (Dependabot)
- 📈 SonarCloud analysis (optional)

**Timeline:** Incremental (CodeQL in Phase 1, others as needed)

---

### Phase 6: Performance Testing (When SLAs are Defined)

**Goal:** Automated performance regression detection

**Changes:**
- BenchmarkDotNet for micro-benchmarks
- k6 or Locust for load testing
- Performance comparison against baseline

**Timeline:** When performance SLAs are defined

---

## Implementation Status

| Phase | Priority | Effort | Status |
|-------|----------|--------|--------|
| Phase 1: Quick Wins | 🔴 High | 2 hours | ✅ **Implemented** (2026-03-11) |
| Phase 2: Multi-Job | 🟡 Medium | 1-2 days | ✅ **Implemented** (2026-03-11) |
| Phase 5: Security (CodeQL) | 🔴 High | 1 day | ✅ **Implemented** (2026-03-11) |
| Phase 3: Docker Images | 🟡 Medium | 2-3 days | 📋 Planned — needs hosting platform decision |
| Phase 4: Deployment | 🟢 Low | 1-2 weeks | 📋 Planned — needs infrastructure setup |
| Phase 6: Performance | 🟢 Low | 3-5 days | 📋 Planned — needs performance SLAs defined |

---

## Next Steps

### Completed (Phases 1, 2 & Security)

- ✅ Parallel `unit-tests` and `integration-tests` jobs in `dotnet.yml`
- ✅ Path-based CI triggering (only fires on code/build changes)
- ✅ Test result `.trx` artifact upload
- ✅ CodeQL security scanning (`codeql.yml`)
- ✅ Concurrency group cancels stale PR runs
- ✅ Removed unnecessary `docker compose` step (Testcontainers is self-contained)

### For Future Planning

1. **Choose hosting platform** - AWS vs Azure vs GCP vs self-hosted → unblocks Phase 3 & 4
2. **Define performance SLAs** - Informs Phase 6 requirements
3. **Revisit quarterly** - Adjust priorities based on project needs

---

## Decision Questions

Before implementing each phase, answer these questions:

### Phase 1 & 2 (Completed)
- [x] Is the team comfortable with test parallelization?
- [x] Do we want test result artifacts?
- [x] Should we enable CodeQL security scanning?
- [x] Is the Blazor frontend stable enough for dedicated CI jobs?
- [x] Do we want separate jobs per BC or just backend/frontend split? → unit vs integration split chosen

### Phase 3 (When Deployment is Planned)
- [ ] Which container registry? (GHCR, Docker Hub, ECR, ACR)
- [ ] Do we need multi-arch images? (amd64, arm64)
- [ ] What tagging strategy? (SHA, semver, latest)

### Phase 4 (When Infrastructure is Ready)
- [ ] What is the target hosting platform?
- [ ] Do we need staging and production environments?
- [ ] GitOps or direct deployment?
- [ ] What deployment strategy? (blue-green, rolling, canary)

### Phase 5 (Ongoing)
- [ ] What code coverage threshold? (70%? 80%?)
- [ ] Do we need SonarCloud or is CodeQL sufficient?
- [ ] Are there compliance requirements (SOC2, HIPAA)?

### Phase 6 (When SLAs are Defined)
- [ ] What are the performance SLAs?
- [ ] What load profile should we test? (100 users? 1000?)
- [ ] Which operations need performance benchmarks?

---

## Resources

- **Full ADR:** [ADR 0007: GitHub Workflow Improvements](../../../decisions/0007-github-workflow-improvements.md)
- **Implementation Guide:** [implementation-guide.md](./implementation-guide.md)
- **Visual Guide:** [visual-guide.md](./visual-guide.md)
- **Current Workflow:** [.github/workflows/dotnet.yml](../../../../.github/workflows/dotnet.yml)
- **Bounded Contexts:** [CONTEXTS.md](../../../../CONTEXTS.md)
- **Development Cycles:** [CYCLES.md](../CYCLES.md)

---

**Last Updated:** 2026-03-11
**Owner:** Erik Shafer
**Status:** Phases 1, 2 & Security implemented ✅
