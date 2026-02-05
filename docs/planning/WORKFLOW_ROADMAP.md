# GitHub Workflow Improvement Roadmap

> **Quick Reference:** This document summarizes the phased approach to improving CritterSupply's CI/CD pipeline as detailed in [ADR 0007](../decisions/0007-github-workflow-improvements.md).

---

## Current State (as of 2026-02-05)

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

**Limitations:**
- 🐌 Everything rebuilds even if only one BC changes
- 🐌 Tests run serially (no parallelization)
- ❌ No deployment automation
- ❌ No security scanning
- ❌ No frontend build steps (needed for Cycle 16+)

---

## Future State (Phased Roadmap)

### Phase 1: Quick Wins (Immediate - 2 hours effort) ✅ RECOMMENDED NOW

**Goal:** Improve performance without architectural changes

**Changes:**
- ✅ Enable test parallelization (remove `-parallel none`)
- ✅ Add test result artifacts (upload `.trx` files)
- ✅ Add path-based triggering (skip docs-only changes)
- ✅ Add CodeQL security scanning

**Expected Benefits:**
- ⚡ 30-50% faster test execution
- 🔍 Better observability (test artifacts)
- 🛡️ Early security vulnerability detection

**Timeline:** Can be implemented immediately

---

### Phase 2: Multi-Job Pipeline (After Frontend is Stable)

**Goal:** Enable independent BC builds and frontend integration

**Trigger:** After Cycle 16 (Customer Experience BFF) is complete

**Architecture:**
```
┌─────────────────────────────────────────────────────────────┐
│                     Changes Detection                       │
│  (Which BCs changed? Which tests to run?)                   │
└─────────────────────────────────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│ Build Orders  │   │Build Payments │   │ Build Shopping│
│   BC (2 min)  │   │   BC (2 min)  │   │   BC (2 min)  │
└───────────────┘   └───────────────┘   └───────────────┘
        │                   │                   │
        └───────────────────┼───────────────────┘
                            │
                            ▼
                ┌─────────────────────┐
                │ Build Frontend (BFF) │
                │      (3 min)         │
                └─────────────────────┘
                            │
                            ▼
                ┌─────────────────────┐
                │ Integration Smoke   │
                │      Tests          │
                └─────────────────────┘
```

**Benefits:**
- ⚡ Parallel builds (10 min → 3-4 min typical case)
- 🎯 Only build what changed
- 🎨 Frontend-specific tooling (Bunit, Playwright)

**Timeline:** After Blazor frontend is stable

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

## Recommended Implementation Order

| Phase | Priority | Effort | Timeline | Blocker |
|-------|----------|--------|----------|---------|
| Phase 1: Quick Wins | 🔴 High | 2 hours | **Immediate** | None - Can start now |
| Phase 5: Security | 🔴 High | 1 day | After Phase 1 | None - Part of Phase 1 |
| Phase 2: Multi-Job | 🟡 Medium | 1-2 days | After frontend | Needs Blazor frontend (Cycle 16+) |
| Phase 3: Docker Images | 🟡 Medium | 2-3 days | When deploying | Needs hosting platform decision |
| Phase 4: Deployment | 🟢 Low | 1-2 weeks | When ready | Needs infrastructure setup |
| Phase 6: Performance | 🟢 Low | 3-5 days | When needed | Needs performance SLAs defined |

---

## Next Steps

### For Immediate Implementation (Phase 1)

1. **Review ADR 0007** - [Read full proposal](../decisions/0007-github-workflow-improvements.md)
2. **Approve Phase 1** - Low risk, high reward improvements
3. **Implement changes** - See implementation steps in ADR
4. **Measure impact** - Compare CI times before/after

### For Future Planning

1. **Complete Cycle 16** - Customer Experience BFF provides frontend requirements
2. **Choose hosting platform** - AWS vs Azure vs GCP vs self-hosted
3. **Define performance SLAs** - Informs Phase 6 requirements
4. **Revisit quarterly** - Adjust priorities based on project needs

---

## Decision Questions

Before implementing each phase, answer these questions:

### Phase 1 (Immediate)
- [x] Is the team comfortable with test parallelization?
- [x] Do we want test result artifacts?
- [x] Should we enable CodeQL security scanning?

### Phase 2 (After Frontend is Stable)
- [ ] Is the Blazor frontend stable enough for dedicated CI jobs?
- [ ] Do we want separate jobs per BC or just backend/frontend split?
- [ ] What frontend testing tools do we need? (Bunit, Playwright, Cypress)

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

- **Full ADR:** [ADR 0007: GitHub Workflow Improvements](../decisions/0007-github-workflow-improvements.md)
- **Current Workflow:** [.github/workflows/dotnet.yml](../../.github/workflows/dotnet.yml)
- **Bounded Contexts:** [CONTEXTS.md](../../CONTEXTS.md)
- **Development Cycles:** [CYCLES.md](./CYCLES.md)

---

**Last Updated:** 2026-02-05
**Owner:** Erik Shafer
**Status:** Proposal / Discussion Phase
