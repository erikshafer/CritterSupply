# ADR 0026: Polecat (SQL Server) Migration for Selected Bounded Contexts

**Status:** ⚠️ Proposed

**Date:** 2026-03-12

---

## Context

[Polecat](https://github.com/JasperFx/polecat) is a new library from JasperFx that ports Marten's document database and event store capabilities from PostgreSQL to SQL Server 2025. It is part of the Critter Stack ecosystem and mirrors Marten's API surface — same interface names, same session patterns, same projection model.

Polecat is at version **0.9.0** (released 2026-03-09) and is approaching production readiness. CritterSupply, as a reference architecture for the Critter Stack, is uniquely positioned to help validate Polecat's real-world readiness by migrating selected bounded contexts from PostgreSQL/Marten to SQL Server/Polecat.

### Why These 4 BCs?

The proposal targets **Vendor Identity**, **Vendor Portal**, **Customer Identity**, and **Customer Experience (Storefront)** because:

1. **They represent both persistence models**: EF Core (Vendor Identity, Customer Identity) and Marten document store (Vendor Portal, Customer Experience) — exercising both migration paths.
2. **They are self-contained**: None of these BCs participate in the Order Saga or core e-commerce event-sourcing flows. Their data doesn't need to cross the Postgres/SQL Server boundary within a single transaction.
3. **They exercise real-time features**: Both Vendor Portal and Storefront use SignalR hubs, testing Polecat under real-time messaging workloads.
4. **They include EF Core integration**: Polecat ships with `Polecat.EntityFrameworkCore` (v0.9.0), which can be validated via the EF Core BCs.
5. **The remaining 8 BCs stay on PostgreSQL/Marten**: Orders, Payments, Shopping, Inventory, Fulfillment, Product Catalog, Pricing, and Returns are unaffected.

---

## Decision

Migrate 4 bounded contexts from PostgreSQL to SQL Server 2025 in a phased approach, using Polecat in place of Marten for document store BCs, and `Microsoft.EntityFrameworkCore.SqlServer` in place of `Npgsql.EntityFrameworkCore.PostgreSQL` for EF Core BCs.

---

## Risk Assessment

### Tier 1 — Critical Risks (Must Resolve Before Migration)

#### R1: No Wolverine+Polecat Integration Package Exists

**Impact: HIGH** | **Likelihood: CERTAIN**

The Vendor Portal uses `.IntegrateWithWolverine()` on Marten, which provides:
- Automatic transaction management for message handlers
- Durable outbox for guaranteed message delivery
- Saga state persistence

**There is no `WolverineFx.Polecat` or `WolverineFx.Http.Polecat` NuGet package** as of 2026-03-12. However:
- `WolverineFx.SqlServer` (v5.18.2) exists — this provides SQL Server-backed persistence for Wolverine's durable messaging (inbox/outbox/saga storage), independent of Polecat.
- Polecat shares the same JasperFx.Events abstractions as Marten, suggesting Wolverine integration may be possible through the common `JasperFx` layer.

**Mitigation:**
1. Use `WolverineFx.SqlServer` for Wolverine's durable messaging on SQL Server (this replaces `WolverineFx.Http.Marten` for the outbox/inbox piece).
2. Investigate whether Polecat's `IDocumentSession` can be wired into Wolverine's handler pipeline manually (or via `IConfigurePolecat`).
3. Engage JasperFx maintainers to understand the Wolverine+Polecat integration timeline.
4. **Block Phase 2 migration until Wolverine+Polecat integration path is confirmed and tested.**

#### R2: Polecat Is Pre-1.0 Software (v0.9.0)

**Impact: HIGH** | **Likelihood: MEDIUM**

- Only 2 published versions (0.1.0 → 0.9.0).
- 72 NuGet downloads total (v0.9.0).
- 1 open issue, 11 stars, 1 fork on GitHub.
- API may have breaking changes before 1.0.
- Edge cases in document storage, LINQ queries, and projections are likely.

**Mitigation:**
1. Pin Polecat version in `Directory.Packages.props`.
2. Write comprehensive integration tests exercising all document patterns used (Store, Load, Query with LINQ, batch operations).
3. Report bugs found directly to JasperFx — this is the stated goal of the initiative.
4. Maintain rollback capability (keep Marten configuration in a branch/flag).

#### R3: SQL Server 2025 Requirement

**Impact: MEDIUM** | **Likelihood: MEDIUM**

Polecat requires SQL Server 2025 (v17) specifically, for the native `JSON` type. SQL Server 2025 is still relatively new:
- Docker image: `mcr.microsoft.com/mssql/server:2025-latest`
- Minimum 2 GB RAM for the container
- Not all developers may have experience with SQL Server 2025 tooling.

**Mitigation:**
1. Use the official Microsoft Docker image for local dev and CI.
2. Document minimum system requirements (2GB additional RAM for SQL Server).
3. Verify SQL Server 2025 Docker image availability on GitHub Actions runners.

---

### Tier 2 — Significant Risks (Must Plan For)

#### R4: Dual-Database Infrastructure Complexity

**Impact: HIGH** | **Likelihood: CERTAIN**

After migration, developers run **two** database engines simultaneously:

| Engine | Databases | Estimated RAM |
|--------|-----------|---------------|
| PostgreSQL | 8 BCs (Orders, Payments, Shopping, Inventory, Fulfillment, ProductCatalog, Pricing, Returns) | ~100–150 MB |
| SQL Server 2025 | 4 BCs (CustomerIdentity, Storefront, VendorIdentity, VendorPortal) | **~1.5–2 GB minimum** |

**Changes Required:**
- New `sqlserver` service in `docker-compose.yml` with SQL Server 2025 image
- New database initialization script (T-SQL) for the 4 migrated databases
- Updated `docker/postgres/create-databases.sh` to remove the 4 migrated databases
- New healthcheck pattern (`/opt/mssql-tools/bin/sqlcmd -Q "SELECT 1"` vs `pg_isready`)
- New Docker Compose profile (e.g., `sqlserver`) for selective startup
- `infrastructure` profile must start both engines
- Port mapping (host port 1434 → container 1433 to avoid conflicts)

**Mitigation:**
1. Add Docker Compose profiles for granular control (`profile: sqlserver`, `profile: postgres`).
2. Update `start-all.sh` to report container health status clearly.
3. Document minimum system requirements in README (16 GB+ RAM recommended).

#### R5: Test Infrastructure Rewrite

**Impact: HIGH** | **Likelihood: CERTAIN**

All 4 BCs use `Testcontainers.PostgreSql` in integration/E2E tests. Each must migrate to `Testcontainers.MsSql`:

| Test Project | Current | Target | Complexity |
|---|---|---|---|
| `CustomerIdentity.Api.IntegrationTests` | Testcontainers.PostgreSql + EF Core Npgsql | Testcontainers.MsSql + EF Core SqlServer | **Medium** — EF Core provider swap + migration regen |
| `VendorIdentity.Api.IntegrationTests` | Testcontainers.PostgreSql + EF Core Npgsql | Testcontainers.MsSql + EF Core SqlServer | **Medium** — same as above |
| `VendorPortal.Api.IntegrationTests` | Testcontainers.PostgreSql + Marten | Testcontainers.MsSql + Polecat | **High** — Marten→Polecat API mapping needed |
| `Storefront.Api.IntegrationTests` | Testcontainers.PostgreSql + Marten | Testcontainers.MsSql + Polecat | **High** — Marten→Polecat API mapping needed |
| `VendorPortal.E2ETests` | Testcontainers.PostgreSql + Playwright | Testcontainers.MsSql + Playwright | **High** — full stack, most moving parts |
| `Storefront.E2ETests` | Testcontainers.PostgreSql + Playwright | Testcontainers.MsSql + Playwright | **High** — full stack, most moving parts |

**Key concern:** SQL Server Docker images are ~1.5 GB (vs Postgres Alpine ~80 MB). CI pipeline time increases. Test startup latency increases.

**Mitigation:**
1. Cache SQL Server Docker image in CI.
2. Run migrated BC tests as a separate CI job to parallelize.
3. Benchmark test startup time difference before committing.

#### R6: EF Core Migration Regeneration

**Impact: MEDIUM** | **Likelihood: CERTAIN**

Customer Identity and Vendor Identity use EF Core code-first migrations targeting Npgsql. SQL Server has different:
- Identity column syntax (`IDENTITY(1,1)` vs `serial`)
- DateTime type (`datetimeoffset` vs `timestamptz`)
- String collation defaults
- Index creation syntax
- Unique constraint behavior with NULL values

**Mitigation:**
1. Delete all existing Npgsql migrations.
2. Regenerate migrations using `Microsoft.EntityFrameworkCore.SqlServer`.
3. Test all CRUD operations with the new provider.
4. Verify FluentValidation rules still work (e.g., unique email checks via EF Core queries).

#### R7: Connection String Naming Convention

**Impact: MEDIUM** | **Likelihood: CERTAIN**

Current convention: all BCs use `ConnectionStrings:postgres` (or `ConnectionStrings:marten`). After migration, 4 BCs use SQL Server but the config key says "postgres."

**Options:**

| Option | Pros | Cons |
|--------|------|------|
| Rename to `ConnectionStrings:sqlserver` | Explicit, no confusion | Breaking change across appsettings, docker-compose, test fixtures |
| Rename to `ConnectionStrings:Default` | Engine-agnostic | Lose signal of which engine in use |
| Keep `ConnectionStrings:postgres` | Zero changes | Misleading — key says "postgres" for SQL Server |

**Recommendation:** Rename to `ConnectionStrings:sqlserver` for migrated BCs. The breaking change is contained to the 4 BCs and is a one-time cost.

---

#### R8: SQL Server String Collation and Composite Document IDs

**Impact: MEDIUM** | **Likelihood: HIGH**

Vendor Portal uses composite string IDs for documents (e.g., `VendorProductCatalogEntry` uses SKU as its ID). SQL Server's default collation (`SQL_Latin1_General_CP1_CI_AS`) is **case-insensitive**, while PostgreSQL is case-sensitive by default.

This means `SKU-1001` and `sku-1001` would resolve to the **same document** on SQL Server but **different documents** on PostgreSQL. This is a silent behavioral change that could affect:
- `VendorProductCatalogEntry` lookups by SKU
- Any composite ID patterns using `{VendorTenantId}:{Sku}` or `{VendorTenantId}:{Sku}:{WarehouseId}`
- LINQ `.Where()` string comparisons

**Mitigation:**
1. Test composite ID lookups with mixed-case SKUs on Polecat (Phase 0 task).
2. Determine if Polecat allows explicit collation configuration per database or table.
3. Consider creating SQL Server databases with `COLLATE Latin1_General_CS_AS` (case-sensitive) if Polecat doesn't manage this.

#### R9: CONTEXTS.md / Code Mismatch on Vendor Portal Persistence

**Impact: MEDIUM** | **Likelihood: CERTAIN**

CONTEXTS.md describes Vendor Portal aggregates as "event-sourced in Marten," but the actual implementation uses **document store patterns** exclusively (`session.Store()`, `session.LoadAsync<T>()`, `session.SaveChangesAsync()`). There are zero calls to `session.Events.*` anywhere in the Vendor Portal.

This mismatch must be resolved before migration to avoid scope ambiguity: migrating a document store is different from migrating an event store.

**Mitigation:**
1. Update CONTEXTS.md to accurately describe Vendor Portal as using Marten's document store (not event sourcing) — this should happen regardless of whether the migration proceeds.
2. If the intent is to add event sourcing later, document that as a post-migration future enhancement.

---

### Tier 3 — Moderate Risks (Monitor and Mitigate)

#### R10: Polecat API Surface Differences from Marten

While Polecat mirrors Marten's API, there are known differences:

| Feature | Marten (PostgreSQL) | Polecat (SQL Server) |
|---------|---------------------|----------------------|
| JSON storage | `jsonb` type | `json` type (SQL Server 2025) |
| Sequences | `bigserial` / sequences | `bigint IDENTITY(1,1)` |
| Upsert | `INSERT ... ON CONFLICT` | `MERGE` statement |
| Change notification | `LISTEN/NOTIFY` | Polling (configurable interval) |
| Advisory locks | `pg_advisory_lock` | `sp_getapplock` / `sp_releaseapplock` |
| Timestamps | `timestamptz` | `datetimeoffset` |
| Serialization | STJ or Newtonsoft | System.Text.Json only (no Newtonsoft) |
| Default schema | `public` | `dbo` |

**CritterSupply already uses STJ only** — no Newtonsoft risk. But the polling-based change notification (vs Postgres `LISTEN/NOTIFY`) may affect async daemon behavior in the Vendor Portal.

#### R11: SignalR Reconnection During Migration

Both Storefront and Vendor Portal use SignalR with limited reconnect policies:
- Storefront: JS client with `0ms → 2s → 10s → 30s` retry, terminal after ~42s
- Vendor Portal: .NET client with `0s → 2s → 10s` retry, terminal after ~12s

During BC migration (service restart), SignalR connections drop and may not recover if the restart takes longer than the retry window.

**Mitigation (UX Engineer recommendation — P1 pre-migration work):**
1. Extend reconnect policies to retry for 5+ minutes.
2. Add a visible connection health indicator to both frontends.
3. Add vendor session expiry warning toast (instead of silent JWT expiry).

#### R12: Wolverine Outbox Message Loss During Cutover

If Marten's outbox has undelivered messages when the service stops for migration, those messages won't exist in Polecat's tables after migration. They are permanently lost from the messaging perspective.

**Mitigation:**
1. Drain the Wolverine outbox before stopping services (wait for all pending messages to publish to RabbitMQ).
2. Monitor the outbox table count (`mt_doc_wolverine_outgoing_envelope`) before shutdown.
3. Post-migration, verify no orphaned messages remain.

#### R13: CI/CD Pipeline Updates

GitHub Actions workflows need:
- SQL Server 2025 service container (or Testcontainers in CI)
- Updated test matrix for mixed-database BCs
- Docker image caching for SQL Server
- Potentially separate test jobs for Postgres-backed vs SQL Server-backed BCs

#### R14: Aspire AppHost Updates

The `CritterSupply.AppHost` project currently references `Aspire.Hosting.PostgreSQL`. After migration:
- Vendor Identity and Vendor Portal remain excluded from Aspire (RabbitMQ transport incompatibility).
- Customer Identity and Storefront are in Aspire — they need `Aspire.Hosting.SqlServer` added.
- The AppHost project needs a SQL Server resource alongside the PostgreSQL resource.

---

## High-Level Migration Plan

### Phase 0: Pre-Migration Prerequisites (1 cycle)

**Goal:** Resolve P1 blockers, validate Polecat compatibility, prepare infrastructure.

- [ ] **P0-1:** Confirm Wolverine+Polecat integration path with JasperFx maintainers
  - Can `WolverineFx.SqlServer` be used alongside Polecat for durable messaging?
  - Is a `WolverineFx.Polecat` integration package planned? Timeline?
- [ ] **P0-2:** Create proof-of-concept: Polecat document store with `WolverineFx.SqlServer` outbox in a minimal test project
  - Verify: document Store/Load/Query operations
  - Verify: Wolverine message handler with Polecat session injection
  - Verify: Durable outbox commit in same transaction as document changes
- [ ] **P0-3:** Extend SignalR reconnect policies in Storefront.Web and VendorPortal.Web (UX P1)
- [ ] **P0-4:** Add SignalR connection health indicator to both frontends (UX P1)
- [ ] **P0-5:** Add SQL Server 2025 container to `docker-compose.yml` (`infrastructure` profile)
  - Image: `mcr.microsoft.com/mssql/server:2025-latest`
  - Port: host `1434` → container `1433`
  - Health check: `sqlcmd -Q "SELECT 1"`
  - Database init script: `docker/sqlserver/create-databases.sql`
- [ ] **P0-6:** Add `Testcontainers.MsSql` to `Directory.Packages.props`
- [ ] **P0-7:** Verify SQL Server 2025 Docker image availability on GitHub Actions runners
- [ ] **P0-8:** Reconcile CONTEXTS.md Vendor Portal persistence description — update "event-sourced in Marten" to "document store in Marten" to match actual implementation (blocking — clarifies migration scope)
- [ ] **P0-9:** Test composite string ID lookups with mixed-case SKUs on Polecat — verify collation behavior for `VendorProductCatalogEntry` and similar documents
- [ ] **P0-10:** Align `WolverineFx.*` package versions — current `Directory.Packages.props` pins at 5.17.0, but `WolverineFx.SqlServer` is at 5.18.2. Upgrade all Wolverine packages to 5.18.x for consistency.
- [ ] **P0-11:** Add open questions Q9–Q11 for JasperFx: composite ID collation behavior, `IDocumentSession` lifecycle in Wolverine handlers without `.IntegrateWithWolverine()`, and `LoadManyAsync<T>()` support in Polecat

### Phase 1: Identity BCs — EF Core Provider Swap (1 cycle)

**Goal:** Migrate Vendor Identity and Customer Identity from Npgsql to SQL Server.

**Why first:** These use EF Core (not Marten), so Polecat is not involved. EF Core's SQL Server provider is mature and battle-tested. Lowest risk path to validate infrastructure.

- [ ] **P1-1:** Customer Identity
  - Replace `Npgsql.EntityFrameworkCore.PostgreSQL` with `Microsoft.EntityFrameworkCore.SqlServer`
  - Update `Program.cs`: `UseNpgsql()` → `UseSqlServer()`
  - Update connection string key: `ConnectionStrings:postgres` → `ConnectionStrings:sqlserver`
  - Update `appsettings.json` with SQL Server connection string format
  - Delete existing Npgsql migrations, regenerate for SQL Server
  - Update `docker-compose.yml` environment variables
  - Update test fixture: `Testcontainers.PostgreSql` → `Testcontainers.MsSql`
  - Run full integration test suite
- [ ] **P1-2:** Vendor Identity
  - Same steps as P1-1
  - Additional: verify JWT token issuance still works after migration
  - Additional: verify `WolverineFx.EntityFrameworkCore` + SQL Server works for outbox
- [ ] **P1-3:** Update `docker/postgres/create-databases.sh` (remove `customeridentity` and `vendoridentity`)
- [ ] **P1-4:** Create `docker/sqlserver/create-databases.sql` (create `customeridentity` and `vendoridentity`)
- [ ] **P1-5:** Update health check configurations (`AspNetCore.HealthChecks.Npgsql` → `AspNetCore.HealthChecks.SqlServer`)
- [ ] **P1-6:** Run cross-BC integration smoke test (Storefront → Customer Identity API, Vendor Portal → Vendor Identity API)
- [ ] **P1-7:** Update Aspire AppHost for Customer Identity (add `Aspire.Hosting.SqlServer`)
- [ ] **P1-8:** Update CONTEXTS.md, CLAUDE.md, and README with new infrastructure requirements
- [ ] **P1-9:** Stabilize for ≥1 week before proceeding to Phase 2

### Phase 2: Portal/Experience BCs — Marten→Polecat Swap (1–2 cycles)

**Goal:** Migrate Customer Experience and Vendor Portal from Marten/Postgres to Polecat/SQL Server.

**Why second:** This involves replacing Marten with Polecat — a library-level change, not just a provider swap. Higher risk, but Phase 1 validates the infrastructure.

**Ordering rationale (Storefront first, Vendor Portal second):** Storefront has minimal Marten usage (BFF pattern, no domain documents, no `.IntegrateWithWolverine()`) — the migration is essentially a config swap. Vendor Portal has 5+ document types, complex LINQ queries, composite string IDs, Wolverine outbox integration, and SignalR notifications. Proving Polecat works on the simpler BC first de-risks the harder one.

- [ ] **P2-1:** Customer Experience / Storefront (Marten → Polecat) — **migrate first (simpler)**
  - Replace `Marten` NuGet with `Polecat` in project references
  - Update `Program.cs`: `AddMarten()` → `AddPolecat()`
  - Note: Storefront does NOT use `.IntegrateWithWolverine()` — this is a clean library swap
  - Minimal document store usage (BFF pattern — Storefront doesn't heavily use document storage)
  - Update Wolverine configuration for SQL Server durable messaging (if needed; Storefront uses `ProcessInline()` queues)
  - Verify: SignalR real-time notifications for cart updates, order status, payment events
  - Verify: BFF composition queries still work
  - Update connection string and docker-compose environment
  - Update test fixture: Marten → Polecat configuration, Testcontainers.MsSql
  - Run full integration and E2E test suites
- [ ] **P2-2:** Vendor Portal (Marten → Polecat) — **migrate second (complex)**
  - Replace `Marten` NuGet with `Polecat` in project references
  - Update `Program.cs`: `AddMarten()` → `AddPolecat()`
  - Update schema config: `opts.DatabaseSchemaName = "vendorportal"` (Polecat defaults to `dbo`, must be explicit)
  - Replace `.IntegrateWithWolverine()` with `WolverineFx.SqlServer` durable messaging
  - Update all document operations (`session.Store()`, `session.Load()`, `session.Query()`)
  - Verify: `VendorProductCatalogEntry` document CRUD (including composite string ID behavior with case sensitivity)
  - Verify: `ChangeRequest` document CRUD and query patterns
  - Verify: `VendorAccount` document operations
  - Verify: `InventorySnapshot` document operations
  - Verify: `LoadManyAsync<T>()` batch loading (used in analytics handlers)
  - Verify: Wolverine message handler integration with Polecat sessions
  - Verify: SignalR real-time notifications still fire correctly
  - Update connection string and docker-compose environment
  - Update test fixture: Marten → Polecat configuration, Testcontainers.MsSql
  - Run full integration test suite
  - Run E2E test suite (Playwright)
- [ ] **P2-3:** Update `docker/postgres/create-databases.sh` (remove `storefront` and `vendorportal`)
- [ ] **P2-4:** Update `docker/sqlserver/create-databases.sql` (add `storefront` and `vendorportal`)
- [ ] **P2-5:** Final cross-BC smoke test (all 12 BCs running together, mixed databases)
- [ ] **P2-6:** Update all documentation (CONTEXTS.md, CLAUDE.md, README, Aspire guide, Docker docs)

### Phase 3: Validation and Bug Reporting (Ongoing)

**Goal:** Exercise Polecat in realistic scenarios and report issues to JasperFx.

- [ ] **P3-1:** Run full test suite (all ~895 tests) repeatedly under load
- [ ] **P3-2:** Manual end-to-end testing of Storefront shopping flow (with mixed DB backends)
- [ ] **P3-3:** Manual end-to-end testing of Vendor Portal flow (change requests, analytics, SignalR)
- [ ] **P3-4:** Document any Polecat bugs, quirks, or API gaps found
- [ ] **P3-5:** Submit issues/PRs to JasperFx/polecat
- [ ] **P3-6:** Write a retrospective documenting the migration experience

---

## Package Changes Summary

### New Packages (Directory.Packages.props)

| Package | Version | Purpose |
|---------|---------|---------|
| `Polecat` | 0.9.0 | SQL Server document store (replaces Marten for 2 BCs) |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.x | EF Core SQL Server provider (replaces Npgsql for 2 BCs) |
| `WolverineFx.SqlServer` | 5.18.2 | Wolverine durable messaging on SQL Server |
| `Testcontainers.MsSql` | 4.11.0 | SQL Server test containers |
| `AspNetCore.HealthChecks.SqlServer` | (latest) | SQL Server health checks |
| `Aspire.Hosting.SqlServer` | (latest) | Aspire SQL Server resource |

> **Note:** `Polecat.EntityFrameworkCore` (v0.9.0) exists for EF Core integration within Polecat projections, but is **deferred** — none of the 4 target BCs use EF Core projections on Polecat. Add when there's a concrete use case.

### Packages Retained (Still Used by Other BCs)

| Package | Reason |
|---------|--------|
| `Marten` | Still used by 6+ BCs on Postgres |
| `WolverineFx.Http.Marten` | Still used by Postgres-backed BCs |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Still used if any remaining EF Core BC is on Postgres |
| `Testcontainers.PostgreSql` | Still used by 8+ BCs |
| `AspNetCore.HealthChecks.Npgsql` | Still used by Postgres-backed BCs |

---

## Infrastructure Changes Summary

### Docker Compose

```yaml
# New SQL Server 2025 service
sqlserver:
  container_name: crittersupply-sqlserver
  image: mcr.microsoft.com/mssql/server:2025-latest
  ports:
    - "1434:1433"
  environment:
    ACCEPT_EULA: "Y"
    MSSQL_SA_PASSWORD: "CritterSupply2025!"  # Meets SQL Server password complexity
    MSSQL_PID: "Developer"
  volumes:
    - ./docker/sqlserver/create-databases.sql:/docker-entrypoint-initdb.d/create-databases.sql
  healthcheck:
    test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "CritterSupply2025!" -Q "SELECT 1" -C -b
    interval: 10s
    timeout: 5s
    retries: 10
    start_period: 30s
  networks:
    - backend
  profiles: [infrastructure, all, ci]
```

### Connection String Format

```
# PostgreSQL (existing, unchanged for 8 BCs)
Host=localhost;Port=5433;Database=orders;Username=postgres;Password=postgres

# SQL Server (new, for 4 migrated BCs)
Server=localhost,1434;Database=customeridentity;User Id=sa;Password=CritterSupply2025!;TrustServerCertificate=true
```

### Database Init Script (docker/sqlserver/create-databases.sql)

```sql
-- Creates one database per bounded context in the SQL Server instance.
-- Run by the container entrypoint on first startup.

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'customeridentity')
    CREATE DATABASE customeridentity;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'vendoridentity')
    CREATE DATABASE vendoridentity;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'vendorportal')
    CREATE DATABASE vendorportal;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'storefront')
    CREATE DATABASE storefront;
GO
```

---

## Complexity Estimate

| Phase | Effort | Risk | Cycle Estimate | Confidence |
|-------|--------|------|----------------|------------|
| Phase 0 (Prerequisites + PoC) | Medium | Medium | 1–2 cycles | Medium (JasperFx external dependency) |
| Phase 1 (EF Core BCs) | Medium | Low-Medium | 1 cycle | High |
| Phase 2 (Marten→Polecat BCs) | High | Medium-High | 1.5–2 cycles | Medium (first real Polecat usage) |
| Phase 3 (Validation) | Low | Low | Ongoing | — |
| **Total** | **High** | **Medium** | **4–5 cycles** | **Medium** |

> **Note:** The 1-cycle variance comes from Phase 0 external dependency risk. If JasperFx is responsive and the PoC works first try, 4 cycles is achievable. If the Wolverine+Polecat transaction question (R1) requires escalation or custom middleware, add a cycle.

---

## UX Engineer Sign-Off Summary

The UX Engineer assessment (included in the research phase) identified these key findings:

### ✅ No Direct Frontend Code Changes Required
Both Blazor frontends (Storefront.Web and VendorPortal.Web) are fully decoupled from the database layer.

### ⚠️ P1 Pre-Migration Work (UX-Driven)
1. **Add SignalR connection health indicator** to Storefront.Web — customers have zero visibility into connection state
2. **Extend SignalR reconnect policies** — current ~42s/~12s windows are too short for a migration restart
3. **Add vendor session expiry notification** — silent JWT expiry during migration causes confusing logout

### ⚠️ Developer Experience Impact
- SQL Server container requires ~2 GB additional RAM
- Test execution time increases (larger Docker image, slower startup)
- Dual-database management adds cognitive overhead

### Phasing Recommendation (UX-Endorsed)
- Phase 1 first (Identity BCs) — lowest user-facing risk, EF Core provider swap is mature
- Phase 2 second (Portal/Experience BCs) — higher risk, validates after Phase 1 infrastructure is stable
- One-week stabilization gap between phases

---

## Alternatives Considered

### Alternative A: Migrate All BCs to SQL Server
**Rejected.** Too risky. The Order Saga, event-sourced aggregates, and projections in Orders, Payments, Inventory, and Fulfillment are deeply integrated with Marten. Migrating them adds enormous risk with little additional validation value.

### Alternative B: Keep Everything on PostgreSQL
**Rejected.** Polecat needs real-world validation, and CritterSupply's role as a reference architecture makes it the ideal proving ground. The whole point is to help Polecat reach production readiness.

### Alternative C: Create a Separate Demo Project for Polecat
**Rejected.** A separate demo project would only test Polecat in isolation. CritterSupply's value is testing Polecat in a complex, multi-BC, event-driven system with real integration patterns.

### Alternative D: Wait for Polecat 1.0
**Considered but deferred.** Waiting for 1.0 reduces risk but doesn't help Polecat *reach* 1.0. The proposal includes sufficient guardrails (phased rollout, integration tests, rollback capability) to manage pre-1.0 risks.

---

## Consequences

### Positive
- CritterSupply becomes a dual-database reference architecture (Postgres + SQL Server)
- Validates Polecat in a production-realistic scenario
- Validates `WolverineFx.SqlServer` integration patterns
- Demonstrates cross-database-engine bounded context isolation
- Helps Polecat reach 1.0 through real-world bug reports

### Negative
- Increased infrastructure complexity (two database engines)
- Higher local development resource requirements (~2 GB additional RAM)
- Slower CI pipeline (SQL Server Docker image is larger)
- Temporary instability risk during migration phases
- Ongoing maintenance of two database stacks

### Neutral
- No impact on the 8 BCs remaining on PostgreSQL
- No impact on RabbitMQ, Jaeger, or Aspire infrastructure
- Message contracts (`Messages.Contracts`) are database-agnostic and unchanged

### Security Notes
- Docker Compose SQL Server password (`CritterSupply2025!`) is for local development only. CI/CD should use environment-variable injection.
- `TrustServerCertificate=true` in connection strings is a dev-only setting — document this explicitly.
- `sa` account usage is acceptable for a reference architecture but production would use least-privilege accounts.

---

## References

- [Polecat GitHub Repository](https://github.com/JasperFx/polecat) — Source code and documentation
- [Polecat NuGet](https://www.nuget.org/packages/Polecat/) — v0.9.0 (2026-03-09)
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/) — v0.9.0
- [WolverineFx.SqlServer NuGet](https://www.nuget.org/packages/WolverineFx.SqlServer/) — v5.18.2 (2026-03-12)
- [Testcontainers.MsSql NuGet](https://www.nuget.org/packages/Testcontainers.MsSql) — SQL Server test containers
- [CONTEXTS.md](../../CONTEXTS.md) — Bounded context definitions for the 4 affected BCs
- [ADR 0002: EF Core for Customer Identity](0002-ef-core-for-customer-identity.md) — Original EF Core decision
- [ADR 0015: Per-BC Postgres Databases](0015-per-bc-postgres-databases.md) — Database isolation pattern
- [ADR 0015: JWT for Vendor Identity](0015-jwt-for-vendor-identity.md) — JWT architecture
