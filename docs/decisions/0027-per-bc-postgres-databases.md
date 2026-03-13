# ADR 0027: Per-Bounded-Context Postgres Databases

**Status:** ✅ Accepted

**Date:** 2026-03-06

## Context

CritterSupply runs a single Postgres container (`crittersupply-postgres`) shared across all eight bounded contexts (BCs). Up until this change, every BC connected to the **same database** (`postgres`) and relied on **Marten schema isolation** (`opts.DatabaseSchemaName`) to separate their tables:

| Bounded Context   | ORM     | Connection Key         | Schema          |
|-------------------|---------|------------------------|-----------------|
| Orders            | Marten  | `marten`               | `orders`        |
| Payments          | Marten  | `marten`               | `payments`      |
| Inventory         | Marten  | `marten`               | `inventory`     |
| Fulfillment       | Marten  | `marten`               | `fulfillment`   |
| Customer Identity | EF Core | `postgres`             | `public` (EF)   |
| Shopping          | Marten  | `marten`               | `shopping`      |
| Product Catalog   | Marten  | `marten`               | `productcatalog`|
| Storefront (BFF)  | Marten  | `marten`               | `storefront`    |

While schema separation is correct practice, all data still lives inside a single Postgres logical database. This raises a question:

> **Should each bounded context connect to its own Postgres database (within the same server / container) rather than sharing one?**

## Decision

**Yes — give every bounded context its own Postgres database.**

A single Postgres _server_ (container) continues to host all data, but each BC now connects to its own named database:

| Bounded Context   | Database Name      |
|-------------------|--------------------|
| Orders            | `orders`           |
| Payments          | `payments`         |
| Inventory         | `inventory`        |
| Fulfillment       | `fulfillment`      |
| Customer Identity | `customeridentity` |
| Shopping          | `shopping`         |
| Product Catalog   | `productcatalog`   |
| Storefront (BFF)  | `storefront`       |

Within each database, the Marten schema name is preserved as-is (e.g. the `orders` schema inside the `orders` database). This keeps the Marten config identical — only the connection string `Database=` segment changes.

## Rationale

### 1. Stronger Bounded Context Isolation
Schemas share a single `pg_catalog`, meaning a super-user or misbehaving query can accidentally read across BC boundaries. Separate databases raise a hard boundary: a connection to `orders` literally cannot see tables in `payments` without an explicit `dblink` or FDW.

### 2. Independent Lifecycle Management
With separate databases it becomes trivial to:
- Drop and recreate a single BC's data without touching others.
- Back up or restore one BC independently.
- Point a BC at a different server/replica in future (e.g. read replica for Product Catalog).

### 3. Clearer Operational Intent
Connection strings now convey intent: `Database=orders` tells an operator immediately which BC is being connected to, without needing to decode schema names from logs.

### 4. Zero Application Code Change
Marten's `opts.DatabaseSchemaName` and EF Core migrations are unchanged. Only the `Database=` segment of the connection string changes. No handler, aggregate, query, or test logic is affected.

### 5. Negligible Startup / Runtime Overhead
Creating 8 databases takes milliseconds. Postgres allocates shared_buffers and other resources per-server, not per-database, so RAM usage does not multiply by 8. Each database does consume a small amount of additional disk (a few MB of system catalog pages) — completely acceptable for development.

## Implementation

### Step 1 — Postgres Init Script
A shell script at `docker/postgres/create-databases.sh` is mounted into `/docker-entrypoint-initdb.d/` inside the Postgres container. Docker's official Postgres image executes all scripts in that directory during the **first** container initialization (i.e. when the data directory is empty):

```bash
psql --username "$POSTGRES_USER" --dbname "postgres" <<-EOSQL
    CREATE DATABASE orders;
    CREATE DATABASE payments;
    ...
EOSQL
```

### Step 2 — docker-compose.yml
The `postgres` service gains a `volumes:` entry mounting the script:

```yaml
volumes:
  - ./docker/postgres/create-databases.sh:/docker-entrypoint-initdb.d/create-databases.sh
```

Every BC service's `ConnectionStrings__marten` (or `ConnectionStrings__postgres` for Customer Identity) is updated to reference the BC-specific database:

```yaml
# before
ConnectionStrings__marten: "Host=postgres;Port=5432;Database=postgres;..."

# after
ConnectionStrings__marten: "Host=postgres;Port=5432;Database=orders;..."
```

### Step 3 — appsettings.json (local development)
Each BC's `appsettings.json` is updated with the same database name change so `dotnet run` (without Docker) also connects to the correct database:

```json
// before
"marten": "Host=localhost;Port=5433;Database=postgres;..."

// after
"marten": "Host=localhost;Port=5433;Database=orders;..."
```

### Step 4 — Schema Names Remain Unchanged
`opts.DatabaseSchemaName = "orders"` (and equivalents) are left as-is. Having a dedicated schema inside a dedicated database is slightly redundant, but adds zero cost and keeps the code consistent — useful if two aggregates within the same BC ever need sub-schema separation.

## Migration Notes for Existing Data

> **Local development only.** CritterSupply targets local dev; there is no persistent production database.

If you have an existing Postgres volume with data in the `postgres` database:

1. Stop all services: `docker-compose down`
2. Remove the old volume: `docker volume rm crittersupply_postgres` (or the volume name shown by `docker volume ls`)
3. Restart: `docker-compose --profile infrastructure up -d`

The init script runs on a fresh data directory and creates all 8 databases automatically. All BCs auto-create their Marten schemas on first connection (`AutoCreate.All`). EF Core migrations run at startup, so Customer Identity tables are created automatically too.

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Existing local data is lost** when the volume is cleared | Low | Data is ephemeral dev data. Re-seed as needed. |
| **Init script runs only once** — adding a new BC later requires manual `CREATE DATABASE` or volume recreation | Low | Document the `create-databases.sh` script as the authoritative list; add new databases there and recreate the volume. |
| **Persistent volume pre-existed** — `docker-entrypoint-initdb.d` scripts do NOT run if the data directory is non-empty | Medium | Addressed in Migration Notes above. One-time `docker volume rm` is sufficient. |
| **Cross-BC queries** (e.g. a rogue direct SQL join across BCs) would silently break | Beneficial | CritterSupply uses messaging for cross-BC communication; this risk turns into a desirable hard boundary. |
| **Increased complexity for DBA tooling** | Low | For local dev this is irrelevant; future production topology would likely have separate servers anyway. |
| **Aspire integration** — AppHost reads from `appsettings.json` which is updated; no code change required | None | ✅ Resolved by appsettings update. |
| **Test isolation** — integration tests use Testcontainers with dedicated ephemeral databases; unaffected | None | ✅ Tests create their own containers and databases. |

## Alternatives Considered

### Keep Schemas in Shared Database (Status Quo)
Schema separation is well-supported by Marten and sufficient for avoiding accidental data mixing at the application layer. However, it provides no hard guarantee at the database level and makes independent BC lifecycle management harder.

### Separate Postgres Containers per BC
True physical isolation with one container per BC. Rejected because:
- 8 Postgres containers multiplies RAM and CPU for a local dev machine.
- Startup time increases significantly (8 independent health checks).
- Docker Compose complexity grows substantially.
- Provides minimal additional benefit over per-database isolation on the same server for local dev.

### Use Postgres Schemas Only (Status Quo)
Kept as the Marten schema name (`opts.DatabaseSchemaName`) is unchanged. The database-level separation adds isolation without requiring any schema naming changes.

## Consequences

### Positive
- ✅ Hard database-level boundary between bounded contexts
- ✅ Independent per-BC data lifecycle (drop / restore without affecting siblings)
- ✅ Self-documenting connection strings (`Database=orders`, not `Database=postgres`)
- ✅ Zero application code changes — only configuration
- ✅ No measurable startup time increase
- ✅ No change to integration tests (Testcontainers manages its own containers)

### Negative / Trade-offs
- ⚠️ Developers with existing local data must recreate their Postgres volume once
- ⚠️ Adding a new BC requires updating `create-databases.sh` and recreating the volume (or running `CREATE DATABASE` manually)
- ⚠️ Slightly more Docker configuration to maintain (one connection string per BC vs one shared string)

## References
- [Postgres docs: `CREATE DATABASE`](https://www.postgresql.org/docs/current/sql-createdatabase.html)
- [Docker Postgres image: init scripts](https://hub.docker.com/_/postgres) — _"Initialization scripts"_ section
- [Marten docs: `DatabaseSchemaName`](https://martendb.io/configuration/storeoptions.html)
- `CLAUDE.md` — API project configuration, connection string format
- ADR 0008 — RabbitMQ configuration consistency (related infrastructure pattern)
