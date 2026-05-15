# Vendor Portal

> **Source folder:** `src/Vendor Portal/`
> **Status:** Implemented
> **Most recent material milestone:** M44.0 — Vendor Portal test-fixture hardening + projection-lifecycle audit
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Vendor Portal is the vendor-facing surface of CritterSupply. It contains the Blazor WASM application (`VendorPortal.Web`) and the BFF (`VendorPortal.Api`) that vendors use to manage their products, view sales analytics, submit change requests against the catalog, manage their team (invitations, roles), and read pricing history. Vendor data is partitioned by `VendorTenantId`; SignalR groups follow the same partitioning so each tenant only sees its own real-time updates.

## Top-level structure

### Aggregates

Vendor Portal stores tenant-scoped data as Marten documents rather than event-sourced aggregates. Document types: `ChangeRequest`, `VendorAccount`, `TeamMember`, `TeamInvitation`, `VendorProductCatalogEntry`, `InventorySnapshot`, `LowStockAlert`, `SavedDashboardView`, `NotificationPreferences`.

### Commands

- Change requests: `DraftChangeRequest`, `SubmitChangeRequest`, `WithdrawChangeRequest`, `ProvideAdditionalInfo`
- Account: `SaveDashboardView`, `DeleteDashboardView`, `UpdateNotificationPreferences`

### Domain events

Not applicable — Vendor Portal does not emit domain events; tenant-scoped state mutations are written to Marten documents and surfaced over SignalR.

### Projections

- Document models materialized by the integration-event handler set (`VendorPortal/Analytics/`, `VendorPortal/TeamManagement/`, `VendorPortal/ChangeRequests/`, `VendorPortal/VendorProductCatalog/`).

### Integration events

- `VendorPortal.DataCorrectionRequested` — publishes
- `VendorPortal.DescriptionChangeRequested` — publishes
- `VendorPortal.ImageUploadRequested` — publishes
- Subscribes to Vendor Identity user / tenant lifecycle events (Team Management handlers)
- Subscribes to Inventory `InventoryAdjusted` / `LowStockDetected` / `StockReplenished`
- Subscribes to Orders `OrderPlaced` (analytics)
- Subscribes to Product Catalog change-decision events (`DataCorrectionApproved` / `DescriptionChangeApproved` / `ImageChangeApproved` and rejection counterparts)

### HTTP / API surface (one line)

`VendorPortal.Api` exposes the vendor dashboard, product catalog read, change-request workflow, team-management and pricing endpoints, plus the SignalR hub at `/hub/vendor-portal`.

### Frontend surface (if applicable)

`VendorPortal.Web` — Blazor WebAssembly single-page application; in-memory JWT storage with background refresh.

### Identity / auth posture (if applicable)

JWT Bearer (validates tokens issued by `VendorIdentity.Api`); JWT also accepted via `access_token` query string for SignalR WebSocket connections.

## Prior event modeling

- `docs/planning/vendor-portal-event-modeling.md`

## ADRs

- ADR 0021 — Blazor WebAssembly for VendorPortal.Web
- ADR 0025 — Blazor WASM POC Learnings

## Source citations (S1 stub)

- `src/Vendor Portal/`
- `src/Shared/Messages.Contracts/VendorPortal/`
- `src/Vendor Portal/VendorPortal.Api/Program.cs` (auth wiring, SignalR registration, Marten config)
- `CONTEXTS.md` (section: `Vendor Portal`)
- `docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md`
- `docs/decisions/0025-blazor-wasm-poc-learnings.md`
- `docs/planning/vendor-portal-event-modeling.md`
