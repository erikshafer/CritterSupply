# Backoffice operations health

> **Status:** Active
> **Type:** Cross-schema read aggregation (parallel surface to fan-in dashboards)
> **Initiating actor:** Operator (Operations Manager role)
> **BCs involved:** Backoffice (aggregator) reading the Wolverine dead-letter tables of every BC's Marten schema
> **Most recent material milestone:** M46.0 / D — Dead-letter aggregator

## Purpose

The operations-health workflow gives Operations Manager operators a single view of failed message processing across every BC's Wolverine dead-letter table. `GET /api/backoffice/operations/dead-letters` opens a fresh `NpgsqlConnection` (deliberately not reusing `IDocumentSession.Connection`) and queries each of an 18-schema default allow-list for `wolverine_dead_letters` rows since a cutoff. Returns aggregated counts by schema, `message_type`, and `exception_type` for the operator triage view.

This workflow is parallel to and independent of `backoffice-fan-in-dashboards.md`. The dashboards reflect business-domain state changes; this workflow reflects messaging-infrastructure health.

## Actors and triggers

- **Initiating actor:** Operator with Backoffice Identity role `OperationsManager`
- **Trigger:** `GET /api/backoffice/operations/dead-letters?since={cutoff}` on Backoffice
- **Prerequisite state:** Wolverine's `<schema>.wolverine_dead_letters` table auto-created in each BC's Marten schema (column set varies by Wolverine version — see Variants)

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Backoffice | Operator request reaches `GetDeadLetterSummary.Handle` (`OperationsHealth/GetDeadLetterSummary.cs#L89`); auth policy: `OperationsManager` | Auth check (this policy string is **not** in the mis-spelled set per `bcs/backoffice.md#identity--auth-posture`) | `bcs/backoffice.md#http--api-surface` |
| 2 | Backoffice | Handler opens a fresh `NpgsqlConnection` from `ConnectionStrings:postgres` (`L108-L113`) rather than reusing `IDocumentSession.Connection` | Connection isolated from Backoffice's per-request transaction (Marten's session connection is enlisted in the transaction and would pin Backoffice's own schema for the lifetime of the read) | `bcs/backoffice.md#operationshealth-` |
| 3 | Backoffice | Handler iterates an 18-schema default allow-list (`L50-L70`) — every BC's Marten schema name (`backoffice`, `customeridentity`, `orders`, `payments`, `inventory`, `fulfillment`, `returns`, `correspondence`, `productcatalog`, `pricing`, `promotions`, `marketplaces`, `listings`, `shopping`, `vendoridentity`, `vendorportal`, `backofficeidentity`, `customerexperience`) | Per-schema allow-list defined | `bcs/backoffice.md#operationshealth-` |
| 4 | Backoffice | For each schema: handler queries the `<schema>.wolverine_dead_letters` table using string-interpolated schema names (allow-listed against `IsValidSchemaIdentifier`) and a parameterised `@cutoff` | Per-schema DLQ counts gathered | `bcs/backoffice.md#operationshealth-` |
| 5 | Backoffice | Handler aggregates counts grouped by schema, `message_type`, `exception_type` | `DeadLetterSummary` DTO assembled | `bcs/backoffice.md#operationshealth-` |
| 6 | Backoffice | Returns to operator | Operator triage view rendered | (HTTP response) |

## Projections and views

- **No persisted projection.** The dead-letter summary is computed on-demand per request. Wolverine's `<schema>.wolverine_dead_letters` tables are the authoritative read source.

## Compensation paths

### Failure: A schema in the allow-list does not exist (BC not yet deployed in this environment)
- **Compensating action:** The handler treats `42P01` (relation does not exist) as a no-op for that schema; aggregation continues across the remaining schemas.
- **Resulting state:** Partial results returned — operator sees only the schemas that exist.

### Failure: Marten session connection is in error state (per memory note)
- **Compensating action:** The fresh-connection pattern (step 2) avoids the issue entirely. Once a `PostgresException` fires inside the session's transaction context, the connection enters error state and subsequent statements fail until ROLLBACK — that is the documented reason the dead-letter endpoint opens its own connection.
- **Resulting state:** Operations endpoint remains usable even when Backoffice's own per-request transaction has hit an error.

### Failure: Wolverine version bump adds NOT NULL columns to `wolverine_dead_letters`
- **Compensating action:** The `GetDeadLetterSummary` endpoint reads only `id`, `message_type`, `exception_type`, `sent_at` (fixed projection — tolerant of additional columns). Test setup that seeds rows must introspect `information_schema.columns` and conditionally include NOT NULL columns — `tests/Backoffice/Backoffice.Api.IntegrationTests/OperationsHealth/GetDeadLetterSummaryTests.cs` uses the `InsertDlqRowAsync` helper for this purpose.
- **Resulting state:** Read tolerant; test seeding portable across Wolverine versions.

## Variants and edge cases

### `wolverine_dead_letters` column variance across Wolverine versions
Wolverine's auto-created `<schema>.wolverine_dead_letters` has more columns than basic docs suggest (e.g. `execution_time`, `body`, `replayable` may be NOT NULL on certain versions). Hardcoded positional `INSERT … VALUES (...)` breaks on version bumps. The test fixture introspects `information_schema.columns` and conditionally includes NOT NULL columns. Memory note: `tests/Backoffice/Backoffice.Api.IntegrationTests/OperationsHealth/GetDeadLetterSummaryTests.cs` (M46.0/D).

### Schema-name allow-listing
String-interpolated schema names are allow-listed against `IsValidSchemaIdentifier` and the 18-schema default list — defence against SQL injection without losing the convenience of interpolated identifiers (Npgsql does not support parameterised identifiers).

### Connection-string overlay in tests
`tests/Backoffice/Backoffice.Api.IntegrationTests/BackofficeTestFixture.cs` overlays `ConnectionStrings:postgres` in `IConfiguration` so the endpoint targets the test container. Memory note: integration tests use this overlay so the fresh connection points at the test Postgres.

### Wolverine concurrency-exhaustion DLQ proof
The Inventory BC has a deterministic concurrency-exhaustion test (`tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs`) that uses a test-only `ConcurrencyExhaustionProbe` + `IWolverineExtension` for handler discovery. After M43.1, the Inventory.Api policy ends in `MoveToErrorQueue` (not `Discard`), so exhausted retries land in `<inventory>.wolverine_dead_letters` and are visible to this aggregator. Memory note: M43.1 closed Gap #13.

## BCs and roles

- **Backoffice** — Aggregator. Owns the endpoint, the connection-string overlay, the schema allow-list, and the cross-schema read pattern. Dossier: `bcs/backoffice.md`.
- **Every BC with a Marten schema** — Owns its own `<schema>.wolverine_dead_letters` table; Wolverine auto-creates and writes to it on terminal retry exhaustion. The aggregator reads but never writes.

## Tests as behavioral evidence

- **Integration test:** `tests/Backoffice/Backoffice.Api.IntegrationTests/OperationsHealth/GetDeadLetterSummaryTests.cs` (M46.0/D) — seeds DLQ rows via the schema-introspecting `InsertDlqRowAsync` helper; asserts the aggregated counts.
- **Test fixture overlay:** `BackofficeTestFixture.cs` overlays `ConnectionStrings:postgres`.
- **Cross-BC proof:** `tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs` shows the producer side — exhausted retries land in DLQ visible to this aggregator.

## ADRs

No dedicated operations-health ADR. The pattern (fresh connection, schema allow-list, on-demand aggregation) is the M46.0/D implementation choice; rationale is in the M46.0 retrospectives.

## Declared vs. implemented

- **Declared shape:** The `OperationsManager` policy is **correctly** registered (not in the mis-spelled set affecting customer-service endpoints).
- **Implemented shape:** Endpoint accessible to `OperationsManager` operators.
- **Gap:** None — the customer-service workflow's auth blocker does not affect this workflow.

## Source citations

- Dossier sections referenced: `bcs/backoffice.md#operationshealth-`, `bcs/backoffice.md#http--api-surface`, `bcs/backoffice.md#identity--auth-posture`.
- Memory notes: dead-letter table introspection (M46.0/D), Marten connection sharing (`src/Backoffice/Backoffice.Api/OperationsHealth/GetDeadLetterSummary.cs`), Inventory M43.1 Gap #13 resolution.
- Tests: `tests/Backoffice/Backoffice.Api.IntegrationTests/OperationsHealth/GetDeadLetterSummaryTests.cs`, `tests/Backoffice/Backoffice.Api.IntegrationTests/BackofficeTestFixture.cs`, `tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs`.
