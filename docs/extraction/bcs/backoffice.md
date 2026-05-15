# Backoffice

> **Source folder:** `src/Backoffice/`
> **Status:** Implemented
> **Most recent material milestone:** M46.0 — Operations DLQ summary endpoint (Reliability follow-through)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Backoffice is the internal operations portal — a Blazor WebAssembly admin console (`Backoffice.Web`) backed by a BFF (`Backoffice.Api`) that composes data from across the system: orders, returns, products, prices, inventory, fulfillment, correspondence history, customers, and operations health. It applies role-based access via Backoffice Identity, surfaces real-time alerts through SignalR, and supports write operations against multiple downstream BCs. Backoffice owns one event-sourced aggregate of its own: `OrderNote` (per ADR 0037).

## Top-level structure

### Aggregates

- `OrderNote` — event-sourced aggregate; stream ID: UUID v7. Lives in Backoffice rather than Orders per ADR 0037.

### Commands

- OrderNote: `AddOrderNote`, `EditOrderNote`, `DeleteOrderNote`
- AlertManagement: `AcknowledgeAlert`
- Plus a read-side surface composed across feature folders: `AlertManagement`, `CustomerService`, `DashboardReporting`, `OrderManagement`, `OrderNotes`, `ProductCatalog`, `ReturnManagement`, `WarehouseOperations`

### Domain events

- `OrderNoteAdded`
- `OrderNoteEdited`
- `OrderNoteDeleted`

### Projections

- `OrderNote` snapshot — inline, keyed by stream id.
- `AdminDailyMetricsProjection` — inline.
- `AlertFeedViewProjection` — inline.
- `ReturnMetricsViewProjection` — inline.
- `CorrespondenceMetricsViewProjection` — inline.
- `FulfillmentPipelineViewProjection` — inline.

### Integration events

- Subscribes to integration events from Orders, Fulfillment, Returns, Payments, Inventory, Correspondence (handler set in `Backoffice/Notifications/`)
- Reads operational data from `wolverine_dead_letters` across all schemas via `GET /api/backoffice/operations/dead-letters/summary` (M46.0)

### HTTP / API surface (one line)

`Backoffice.Api` exposes admin views (composition under `Backoffice/Composition/`), operational health endpoints, and the SignalR hub at `/hub/backoffice`.

### Frontend surface (if applicable)

`Backoffice.Web` — Blazor WebAssembly single-page application; in-memory JWT storage with background refresh and session-expired recovery UX.

### Identity / auth posture (if applicable)

JWT Bearer (validates tokens issued by `BackofficeIdentity.Api`); policy-based RBAC.

## Prior event modeling

- `docs/planning/backoffice-event-modeling.md`
- `docs/planning/backoffice-event-model-critique.md`
- `docs/planning/backoffice-event-modeling-revised.md`

## ADRs

- ADR 0034 — Backoffice BFF Architecture
- ADR 0035 — Backoffice SignalR Hub Design
- ADR 0036 — BFF Projections Strategy
- ADR 0037 — OrderNote Aggregate Ownership
- ADR 0033 — Admin Portal to Backoffice Rename

## Source citations (S1 stub)

- `src/Backoffice/`
- `src/Backoffice/Backoffice.Api/Program.cs` (projection registrations, SignalR hub, JWT wiring)
- `CONTEXTS.md` (section: `Backoffice`)
- `docs/decisions/0034-backoffice-bff-architecture.md`
- `docs/decisions/0035-backoffice-signalr-hub-design.md`
- `docs/decisions/0036-bff-projections-strategy.md`
- `docs/decisions/0037-ordernote-aggregate-ownership.md`
- `docs/decisions/0033-admin-portal-to-backoffice-rename.md`
- `docs/planning/backoffice-event-modeling-revised.md`
