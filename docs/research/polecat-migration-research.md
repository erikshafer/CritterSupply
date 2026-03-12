# Polecat Migration Research: API Compatibility & Infrastructure Analysis

**Date:** 2026-03-12
**Status:** Research / Pre-Implementation
**Related ADR:** [0026 — Polecat SQL Server Migration](../decisions/0026-polecat-sql-server-migration.md)

---

## 1. Polecat Library Overview

**Repository:** [github.com/JasperFx/polecat](https://github.com/JasperFx/polecat)
**Current Version:** 0.9.0 (released 2026-03-09)
**License:** MIT
**Target:** .NET 10 only, SQL Server 2025 only

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Polecat` | 0.9.0 | Core document store + event store |
| `Polecat.EntityFrameworkCore` | 0.9.0 | EF Core integration for projections |
| `Polecat.AspNetCore` | (in source) | ASP.NET Core hosting integration |
| `Polecat.AspNetCore.Testing` | (in source) | Test helpers for ASP.NET Core |

### Dependencies (Polecat 0.9.0)

| Dependency | Version | Notes |
|------------|---------|-------|
| JasperFx | 1.21.0 | Core Critter Stack framework |
| JasperFx.Events | 1.23.1 | Shared event sourcing abstractions |
| Microsoft.Data.SqlClient | 6.1.4 | SQL Server ADO.NET provider |
| Polly.Core | 8.6.5 | Resilience/retry policies |
| Weasel.SqlServer | 8.9.0 | Schema migration for SQL Server |

---

## 2. Marten → Polecat API Mapping

### Service Registration

| Marten | Polecat | Notes |
|--------|---------|-------|
| `services.AddMarten(opts => { ... })` | `services.AddPolecat(opts => { ... })` | Nearly identical API |
| `opts.Connection(connectionString)` | `opts.ConnectionString = connectionString` | Property instead of method |
| `opts.DatabaseSchemaName = "myschema"` | `opts.DatabaseSchemaName = "myschema"` | Identical; Polecat defaults to `dbo` instead of `public` |
| `.UseLightweightSessions()` | Default behavior in Polecat | Polecat sessions are lightweight by default |
| `.IntegrateWithWolverine()` | **No equivalent yet** | See Risk R1 in ADR |

### Session Operations

| Marten | Polecat | Notes |
|--------|---------|-------|
| `IDocumentSession` | `IDocumentSession` | Same interface name |
| `IQuerySession` | `IQuerySession` | Same interface name |
| `session.Store(document)` | `session.Store(document)` | Same API |
| `session.Load<T>(id)` | `session.Load<T>(id)` | Same API |
| `session.Query<T>()` | `session.Query<T>()` | Same API, LINQ support |
| `session.SaveChangesAsync()` | `session.SaveChangesAsync()` | Same API |
| `session.Delete<T>(document)` | Expected same | Verify |
| `session.DeleteWhere<T>(predicate)` | Verify | May differ |

### Event Store Operations

| Marten | Polecat | Notes |
|--------|---------|-------|
| `session.Events.StartStream<T>(id, events)` | `session.Events.StartStream<T>(id, events)` | Expected same (JasperFx.Events) |
| `session.Events.Append(streamId, events)` | `session.Events.Append(streamId, events)` | Expected same |
| `session.Events.FetchStream(streamId)` | `session.Events.FetchStream(streamId)` | Expected same |
| `session.Events.AggregateStream<T>(streamId)` | `session.Events.AggregateStream<T>(streamId)` | Expected same |

### Projections

| Marten | Polecat | Notes |
|--------|---------|-------|
| `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` | `opts.Projections` configuration | Same JasperFx.Events model |
| `SingleStreamProjection<T>` | `SingleStreamProjection<T>` | Same base type |
| `MultiStreamProjection<T>` | `MultiStreamProjection<T>` | Same base type |
| `EventProjection` | `EventProjection` | Same base type |
| Async Daemon | Async Daemon | Polecat supports this |

### Key Differences

| Feature | Marten | Polecat | Impact |
|---------|--------|---------|--------|
| JSON type | PostgreSQL `jsonb` (binary) | SQL Server `json` (text) | Potential query performance diff |
| Upsert | `INSERT ... ON CONFLICT` | `MERGE` statement | Transparent — handled by library |
| Change notification | `LISTEN/NOTIFY` (push) | Polling (configurable) | Async daemon may have slightly higher latency |
| Advisory locks | `pg_advisory_lock` | `sp_getapplock` | Transparent — handled by library |
| Serialization | STJ or Newtonsoft | STJ only | ✅ CritterSupply already uses STJ |
| Default schema | `public` | `dbo` | Must explicitly set `DatabaseSchemaName` |
| Multi-tenancy | Conjoined or separate DB | Conjoined or separate DB | Same model |
| String comparison | Postgres default (case-sensitive) | SQL Server default (case-insensitive) | ⚠️ May affect LINQ string queries |

---

## 3. Affected BC Inventory

### Customer Identity (EF Core → EF Core SQL Server)

**Persistence Change:** `Npgsql.EntityFrameworkCore.PostgreSQL` → `Microsoft.EntityFrameworkCore.SqlServer`

**Entities:**
- `Customer` — Guid PK, unique email, navigation to Addresses
- `CustomerAddress` — Guid PK, FK to Customer, enum types (AddressType)
- `AddressSnapshot` — record DTO (no persistence)

**EF Core Features Used:**
- Code-first migrations
- Unique indexes (email)
- One-to-many relationships (Customer → Addresses)
- Enum persistence
- `DateTimeOffset` columns

**SQL Server Compatibility:** ✅ All features fully supported by EF Core SQL Server provider.

**Wolverine Integration:**
- Uses `WolverineFx.EntityFrameworkCore` for transaction management
- No RabbitMQ integration (isolated BC)
- Uses `AutoApplyTransactions()` and `UseDurableLocalQueues()`

### Vendor Identity (EF Core → EF Core SQL Server)

**Persistence Change:** Same as Customer Identity.

**Entities:**
- `VendorTenant` — Guid PK, unique org name, status enum, navigation to Users
- `VendorUser` — Guid PK, FK to Tenant, unique email system-wide, role enum
- `VendorUserInvitation` — Guid PK, FK to User and Tenant, hashed token

**EF Core Features Used:**
- Code-first migrations
- Multiple unique indexes (org name, email)
- One-to-many relationships (Tenant → Users, User → Invitations)
- Enum persistence (VendorTenantStatus, VendorRole, VendorUserStatus, InvitationStatus)
- `DateTimeOffset` columns
- SHA-256 token hashing (application-level, DB-agnostic)

**SQL Server Compatibility:** ✅ All features fully supported.

**Wolverine Integration:**
- Uses `WolverineFx.EntityFrameworkCore` + `WolverineFx.RabbitMQ`
- Publishes integration messages to RabbitMQ
- Uses `AutoApplyTransactions()`, `UseDurableOutboxOnAllSendingEndpoints()`
- **Risk:** Wolverine's EF Core outbox currently uses Npgsql. Must verify `WolverineFx.EntityFrameworkCore` works with SQL Server provider. `WolverineFx.SqlServer` may be needed as the outbox backend instead.

### Vendor Portal (Marten → Polecat)

**Persistence Change:** `Marten` → `Polecat`

**Documents:**
- `VendorProductCatalogEntry` — string ID (SKU), simple CRUD
- `ChangeRequest` — Guid ID, complex state machine, query by status/tenant/type
- `VendorAccount` — Tenant profile, notification preferences
- `InventorySnapshot` — Vendor inventory levels, analytics queries
- `LowStockAlert` — Active alert tracking

**Marten Features Used:**
- `.Store()` / `.Load<T>()` / `.Query<T>()` — Standard document operations
- `.UseLightweightSessions()` — Session configuration
- `.IntegrateWithWolverine()` — **Critical integration point**
- `DatabaseSchemaName = "vendorportal"` — Schema isolation
- LINQ queries with `.Where()`, `.OrderBy()`, `.FirstOrDefaultAsync()`
- Batch document operations in single `SaveChangesAsync()`

**Polecat Compatibility Assessment:**
- ✅ Document CRUD (Store/Load/Query) — API matches
- ✅ Schema name configuration — Supported
- ✅ LINQ queries — Supported (verify complex predicates)
- ⚠️ `.IntegrateWithWolverine()` — **No equivalent; use `WolverineFx.SqlServer` for outbox**
- ⚠️ String comparison in LINQ — SQL Server default collation is case-insensitive (vs Postgres case-sensitive). SKU lookups may behave differently.

### Customer Experience / Storefront (Marten → Polecat)

**Persistence Change:** `Marten` → `Polecat`

**Documents:**
- Minimal document usage — Storefront is a BFF that primarily queries other BCs via HTTP
- May use Marten for session caching or read model state

**Marten Features Used:**
- `AddMarten()` — Basic configuration
- `DatabaseSchemaName = "storefront"` — Schema isolation
- Minimal document operations (BFF pattern)

**Polecat Compatibility Assessment:**
- ✅ Basic document store — Low risk, minimal usage
- ✅ Schema configuration — Supported
- ⚠️ `.IntegrateWithWolverine()` — Same gap as Vendor Portal, but lower impact since Storefront doesn't heavily use Marten for persistence

---

## 4. Wolverine Integration Analysis

### Current Wolverine+Marten Integration

The `WolverineFx.Http.Marten` package provides:
1. **Transaction management:** Marten `IDocumentSession` enlisted in Wolverine's unit of work
2. **Outbox pattern:** Outgoing messages stored in Marten's PostgreSQL tables alongside document changes (atomic commit)
3. **Saga persistence:** Wolverine sagas stored as Marten documents
4. **Handler middleware:** Automatic session injection, `[WriteAggregate]`/`[ReadAggregate]` attributes

### Post-Migration Wolverine Stack

For the 4 migrated BCs:

| Layer | Current | Target |
|-------|---------|--------|
| **Document Store** | Marten (Postgres) | Polecat (SQL Server) |
| **Wolverine Outbox** | WolverineFx.Http.Marten | WolverineFx.SqlServer |
| **Wolverine Inbox** | WolverineFx.Http.Marten | WolverineFx.SqlServer |
| **Saga Storage** | Marten documents | WolverineFx.SqlServer (if no Polecat integration) |
| **Message Transport** | WolverineFx.RabbitMQ | WolverineFx.RabbitMQ (unchanged) |
| **SignalR Transport** | WolverineFx.SignalR | WolverineFx.SignalR (unchanged) |

### Key Integration Question

**Can Polecat's `IDocumentSession.SaveChangesAsync()` and Wolverine's `WolverineFx.SqlServer` outbox participate in the same SQL Server transaction?**

If yes → atomic document + message commit (same guarantee as Marten+Wolverine today).
If no → potential for message loss or double-delivery in edge cases.

This is the single most important technical question to resolve in Phase 0.

---

## 5. Infrastructure Delta

### Docker Compose Changes

| Change | Details |
|--------|---------|
| **Add** `sqlserver` service | SQL Server 2025 container, port 1434, ~2 GB RAM |
| **Modify** `postgres` `create-databases.sh` | Remove 4 database entries (Phase 1: 2, Phase 2: 2) |
| **Add** `docker/sqlserver/create-databases.sql` | T-SQL script for 4 databases |
| **Modify** 4 BC service entries | Update `ConnectionStrings__*` env vars |
| **Modify** `infrastructure` profile | Add `sqlserver` service |
| **Add** `sqlserver` profile | Optional standalone SQL Server startup |

### CI/CD Changes

| Change | Details |
|--------|---------|
| **GitHub Actions** | Add SQL Server 2025 service container or rely on Testcontainers |
| **Docker image cache** | SQL Server image is ~1.5 GB — enable caching |
| **Test parallelism** | Consider splitting Postgres and SQL Server test jobs |
| **Build matrix** | No change — same .NET 10 SDK for all |

### Aspire AppHost Changes

| Change | Details |
|--------|---------|
| **Add** `Aspire.Hosting.SqlServer` | New package reference |
| **Add** SQL Server resource | Alongside existing Postgres resource |
| **Modify** Customer Identity reference | Point to SQL Server resource |
| **No change** for Vendor BCs | Still excluded from Aspire (RabbitMQ incompatibility) |

---

## 6. UX Engineer Assessment Summary

*(Full assessment provided during research phase. Key points below.)*

### Frontend Impact: None
Both Blazor frontends are fully decoupled from the database layer. No `.razor`, `.js`, or `.css` changes needed.

### P1 Pre-Migration UX Work
1. **SignalR connection health indicator** — Customers/vendors have no visibility when connections drop
2. **Extended reconnect policies** — Current windows (42s/12s) too short for migration restarts
3. **Vendor session expiry warning** — Silent JWT logout is poor UX

### Developer Experience Impact
- +2 GB RAM for SQL Server container
- Slower test execution (larger Docker image)
- Dual-database cognitive overhead

### Phasing Endorsed
- Phase 1 (Identity BCs) first — lowest user-facing risk
- Phase 2 (Portal/Experience BCs) second — higher risk
- One-week stabilization gap between phases

### Risk Matrix

| Risk Area | Severity | Likelihood |
|-----------|----------|------------|
| Silent SignalR disconnection (Storefront) | **High** | Medium |
| Test infrastructure rewrite | **High** | Certain |
| Developer local resource pressure | **High** | Certain |
| Vendor session silent expiry | **Medium** | Medium |
| Checkout address step failure during migration | **Medium** | High (during downtime) |
| Wolverine outbox message loss | **Medium** | Low |
| SQL Server query latency differences | **Low** | Low |
| Real-time notification gaps during restart | **Low** | Medium |

---

## 7. Open Questions for JasperFx

1. **Wolverine+Polecat integration timeline:** Is a `WolverineFx.Polecat` package planned? What would it provide beyond `WolverineFx.SqlServer`?
2. **Shared transaction support:** Can Polecat's `IDocumentSession` and `WolverineFx.SqlServer`'s outbox share a SQL Server transaction?
3. **LINQ compatibility:** Are there known LINQ query patterns that work in Marten but not in Polecat?
4. **Document type registration:** Does Polecat require explicit document type registration (like `opts.Schema.For<T>()` in Marten)?
5. **Async daemon polling interval:** What is the default polling interval? Is it configurable?
6. **SQL Server 2025 feature gates:** Which SQL Server 2025-specific features does Polecat require? (JSON type is mandatory — anything else?)
7. **Connection pooling:** Does Polecat manage its own connection pool, or does it use `Microsoft.Data.SqlClient`'s built-in pooling?
8. **Bulk operations:** Does Polecat support batch inserts comparable to Marten's `BulkInsert`?

---

## 8. Next Steps

1. **Create Phase 0 GitHub Issues** for each prerequisite task
2. **Reach out to JasperFx maintainers** with the open questions above
3. **Build PoC project** testing Polecat + WolverineFx.SqlServer integration
4. **Get principal-architect sign-off** on the ADR
5. **Schedule Phase 0 work** into the next available cycle
