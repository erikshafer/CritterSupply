# Bounded Context — Backoffice (BFF + 1 ES aggregate)

**S2 — full depth (Variant C-hybrid).** Backoffice is the internal admin BFF for the seven `BackofficeRole` operators issued by the Backoffice Identity BC. It composes read views from upstream BCs over typed `HttpClient`s, materialises five Marten document-shape projections from RabbitMQ subscriptions, fans out real-time updates over a single SignalR hub, and owns one event-sourced aggregate (`OrderNote`) plus one document-mutating command (`AcknowledgeAlert`). The "C-hybrid" label flags the single ES aggregate against an otherwise-BFF profile — see §Aggregates.

- Composition root: `src/Backoffice/Backoffice.Api/Program.cs`
- Domain library: `src/Backoffice/Backoffice/`
- WASM admin shell: `src/Backoffice/Backoffice.Web/`
- API port (Dev): `5243`

## Purpose

Backoffice is the operator BFF for CritterSupply staff. It does not own customer-, order-, return-, inventory-, or correspondence-state — those live in their respective BCs. Backoffice's authoritative artifacts are the seven Marten documents it maintains in its `backoffice` schema (`OrderNote` event-sourced stream + 5 dashboard / alert documents + `OrderNoteSnapshot`), the SignalR delivery surface that pushes operator-relevant changes into the WASM shell, and the role-keyed HTTP surface that aggregates upstream BC data behind operator-friendly endpoints. Reads, where possible, are served from local projections; writes against upstream state are HTTP-proxied through typed clients (`IOrdersClient.CancelOrderAsync`, `IInventoryClient.AdjustAsync`, `IReturnsClient.ApproveAsync`, etc.) or appended to the `OrderNote` stream / `AlertFeedView` document directly.

## Aggregates

### `OrderNote` (event-sourced)

- **Stream ID:** `Guid` minted client-side in `AddOrderNoteEndpoint.Handle` via `Guid.CreateVersion7()` and used as the Marten stream id with `MartenOps.StartStream<OrderNote>(noteId, addedEvent)`. The order's own id (`orderId`) is carried as a field on every event but is **not** part of the stream key — multiple notes per order are independent streams (`src/Backoffice/Backoffice.Api/OrderNotes/AddOrderNoteEndpoint.cs#L46-L75`).
- **Key state:** `Id` (note id), `OrderId`, `AuthorUserId`, `AuthorDisplayName`, `Body`, `CreatedAt`, `LastEditedAt?`, `IsDeleted` (soft-delete flag) (`src/Backoffice/Backoffice/OrderNote/OrderNote.cs#L9-L19`).
- **Lifecycle stages:**
  - `Created` → on `OrderNoteAdded` from `Create(IEvent<OrderNoteAdded>)` factory; `IsDeleted=false`, `LastEditedAt=null` (`src/Backoffice/Backoffice/OrderNote/OrderNote.cs#L21-L32`).
  - `Edited*` → on `OrderNoteEdited` from `Apply(OrderNoteEdited)`; rewrites `Body` and stamps `LastEditedAt`. May fire any number of times (`src/Backoffice/Backoffice/OrderNote/OrderNote.cs#L34-L42`).
  - `Deleted` → on `OrderNoteDeleted` from `Apply(OrderNoteDeleted)`; sets `IsDeleted = true`. Stream is **not** archived; the document is left in place so audit reads can still load the soft-deleted note (`src/Backoffice/Backoffice/OrderNote/OrderNote.cs#L44-L48`).
- **File:** `src/Backoffice/Backoffice/OrderNote/OrderNote.cs`.

A `OrderNoteSnapshot` is also registered as an inline single-stream projection in `Program.cs#L90` so the snapshot document is queryable without a `LiveStreamAggregation`.

## Domain events

| Event | Field shape | File |
|-------|-------------|------|
| `OrderNoteAdded` | `NoteId, OrderId, AuthorUserId, AuthorDisplayName, Body, CreatedAt` | `src/Backoffice/Backoffice/OrderNote/OrderNoteEvents.cs#L8-L15` |
| `OrderNoteEdited` | `NoteId, NewBody, EditedAt` | `src/Backoffice/Backoffice/OrderNote/OrderNoteEvents.cs#L17-L23` |
| `OrderNoteDeleted` | `NoteId, DeletedAt, DeletedByUserId` | `src/Backoffice/Backoffice/OrderNote/OrderNoteEvents.cs#L25-L30` |

Counts match the S1 baseline.

## Commands

Four in-process commands are dispatched through Wolverine inside Backoffice. All four mutate **Backoffice-owned** state (`OrderNote` stream or `AlertFeedView` document); no Backoffice command emits cross-BC integration events.

| Command | Handler | Persisted via | Authorize policy on calling endpoint |
|---------|---------|---------------|--------------------------------------|
| `AddOrderNote` (record) | `AddOrderNoteEndpoint.Handle` | `MartenOps.StartStream<OrderNote>(...)` returning a `IMartenOp` from a Wolverine `[WolverinePost]` HTTP endpoint | `CustomerService` (broken — see §Identity / auth posture) |
| `EditOrderNote` (record) | `EditOrderNoteEndpoint.Handle` | `MartenOps.AppendOne(...)` after `BeforeAsync` author-only guard | `CustomerService` (broken) |
| `DeleteOrderNote` (record) | `DeleteOrderNoteEndpoint.Handle` | `MartenOps.AppendOne(OrderNoteDeleted)`. `BeforeAsync` allows deletion only by the original author **or** any caller carrying the `system-admin` role claim | `CustomerService` (broken) |
| `AcknowledgeAlert` (record) | `AcknowledgeAlertHandler.Handle` (in-process; dispatched via `IMessageBus.InvokeAsync`) | `IDocumentSession` load → mutate `AlertFeedView` (set `IsAcknowledged=true`, `AcknowledgedBy*`, `AcknowledgedAt`) → `Store` | `WarehouseClerk` |

Files:
- `src/Backoffice/Backoffice/OrderNote/AddOrderNote.cs`
- `src/Backoffice/Backoffice/OrderNote/EditOrderNote.cs`
- `src/Backoffice/Backoffice/OrderNote/DeleteOrderNote.cs`
- `src/Backoffice/Backoffice/AlertManagement/AcknowledgeAlert.cs`

A fifth HTTP-only DTO, `CancelOrderCommand` (`src/Backoffice/Backoffice.Api/OrderManagement/CancelOrder.cs#L13-L74`), is named "Command" but is not dispatched through Wolverine. The endpoint validates the request, extracts `adminUserId` from the JWT, then delegates to `IOrdersClient.CancelOrderAsync(orderId, ct)` which `POST`s to Orders' `/api/orders/{orderId}/cancel`. It is documented under §HTTP / API surface — the Orders saga is the authoritative cancellation handler and Backoffice does not participate in the resulting saga state machine.

## Composition map

Backoffice composes read views from nine upstream BCs over typed `HttpClient`s and subscribes to integration messages from a subset of those BCs over RabbitMQ. The matrix below cross-references CONTEXTS.md lines 273-291 against the actual `Program.cs` / `IxxxClient` registrations.

| Upstream BC | Subscribes to (RabbitMQ) | Queries via HTTP client | Materialised local read model | CONTEXTS.md drift |
|-------------|--------------------------|--------------------------|--------------------------------|-------------------|
| Backoffice Identity | — | `IBackofficeIdentityClient` (registered, **no consumer in `Backoffice.Api/`**) | — | none — Web shell calls Identity API directly via `BackofficeIdentityApi` named `HttpClient` |
| Customer Identity | — | `ICustomerIdentityClient` (4 endpoints) | feeds `CustomerDetailView`, `CustomerServiceView`, `CorrespondenceHistoryView`, `OrderDetailView` | none |
| Orders | `OrderPlaced`, `OrderShipped`, `OrderCancelled`, `OrderDelivered` (via 4 notification handlers) | `IOrdersClient` (5 endpoints) | feeds `AdminDailyMetrics`, `OrderDetailView`, `CustomerServiceView`, `CustomerDetailView` | none |
| Returns | `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnReceived`, `ReturnRefunded` (5 notification handlers) | `IReturnsClient` (4 endpoints) | feeds `ReturnMetricsView`, `AlertFeedView` (via `ReturnAutoApprovalFailed`), `ReturnDetailView` | none |
| Product Catalog | `ProductCreated`, `ProductPublished` (2 notification handlers) | `ICatalogClient` (5 endpoints) | feeds `AdminDailyMetrics` (`TotalProducts` counter only) | none |
| Pricing | — | `IPricingClient` (3 endpoints; **no consumer in `Backoffice.Api/`**; the WASM `PriceEdit.razor` page calls `/api/pricing/...` against the `BackofficeApi` named client base — those paths are not served by `Backoffice.Api`, see §HTTP / API surface) | — | drift — see "Pricing client unused" below |
| Inventory | `LowStockAlertRaised`, `OutOfStockOccurred`, `BackorderCreated` (3 notification handlers) | `IInventoryClient` (5 endpoints) | feeds `AlertFeedView` (low-stock + out-of-stock), `AdminDailyMetrics` (low-stock counter) | none |
| Fulfillment | `ShipmentHandedToCarrier`, `ShipmentDelivered`, `ReturnToSenderInitiated`, `GhostShipmentDetected`, `ShipmentLostInTransit` (5 notification handlers) | `IFulfillmentClient` (1 endpoint, **registered with no consumer anywhere**) | feeds `FulfillmentPipelineView`, `AlertFeedView` (ghost / lost) | drift — CONTEXTS.md line 287 says Fulfillment is "queries"; the BC is in fact subscribed-to and the typed client is unused |
| Correspondence | `CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed` (3 notification handlers) | `ICorrespondenceClient` (2 endpoints) | feeds `CorrespondenceMetricsView`, `CorrespondenceHistoryView`, `AlertFeedView` (failed) | none |
| **Payments** (drift — not in CONTEXTS.md table) | `PaymentCaptured`, `PaymentFailed` (2 notification handlers) | — | feeds `AdminDailyMetrics`, `AlertFeedView` (failed) | drift — Payments is not listed in the Backoffice subscription table at CONTEXTS.md lines 279-289 |

Composition view records (DTOs assembled at the BFF, never persisted):

- `CorrespondenceHistoryView` + `CorrespondenceMessageView` (`src/Backoffice/Backoffice/Composition/CorrespondenceHistoryView.cs`)
- `CustomerDetailView` + `CustomerAddressView` (`src/Backoffice/Backoffice/Composition/CustomerDetailView.cs`)
- `CustomerServiceView` + `OrderSummaryView` (`src/Backoffice/Backoffice/Composition/CustomerServiceView.cs`)
- `OrderDetailView` + `OrderLineItemView` + `ReturnableItemView` (`src/Backoffice/Backoffice/Composition/OrderDetailView.cs`)
- `ReturnDetailView` + `ReturnItemView` (`src/Backoffice/Backoffice/Composition/ReturnDetailView.cs`)

## Read models / projections

Six Marten registrations in `Backoffice.Api/Program.cs#L88-L107`. All inline.

| # | Name | Shape | Identity | Source events | File |
|---|------|-------|----------|---------------|------|
| 1 | `OrderNote` snapshot | Single-stream aggregation of the `OrderNote` event stream | Stream `Guid` (UUID v7 minted in `AddOrderNoteEndpoint.cs#L52`) | `OrderNoteAdded`, `OrderNoteEdited`, `OrderNoteDeleted` | `src/Backoffice/Backoffice/OrderNote/OrderNote.cs` (registered at `Program.cs#L90`) |
| 2 | `AdminDailyMetricsProjection` | Date-keyed daily counters: `TotalOrdersToday`, `TotalProducts`, `TotalRevenueToday`, `OutOfStockSkus`, `FailedPaymentsToday` | `Id = "yyyy-MM-dd"` | Multi-stream: `OrderPlaced`, `ProductPublished`, `LowStockAlertRaised`, `OutOfStockOccurred`, `PaymentFailed` | `src/Backoffice/Backoffice/DashboardReporting/AdminDailyMetricsProjection.cs` (`Program.cs#L94`) |
| 3 | `AlertFeedViewProjection` | Per-alert `AlertFeedView` with 6 alert types × 3 severities (Critical/Warning/Info), acknowledgment fields | `Id = Guid.NewGuid()` per inbound event | Multi-stream over `LowStockAlertRaised`, `OutOfStockOccurred`, `PaymentFailed`, `ShipmentLostInTransit`, `GhostShipmentDetected`, `ReturnAutoApprovalFailed`, `CorrespondenceFailed` | `src/Backoffice/Backoffice/AlertManagement/AlertFeedViewProjection.cs` (`Program.cs#L97`) |
| 4 | `ReturnMetricsViewProjection` | Singleton `Id = "current"`; counters keyed by `ReturnStatus` and `RmaReason` | `Id = "current"` | Multi-stream over the 5 Returns events | `src/Backoffice/Backoffice/DashboardReporting/ReturnMetricsViewProjection.cs` (`Program.cs#L100`) |
| 5 | `CorrespondenceMetricsViewProjection` | Singleton `Id = "current"`; queue depth, deliveries, failures | `Id = "current"` | Multi-stream over the 3 Correspondence events | `src/Backoffice/Backoffice/DashboardReporting/CorrespondenceMetricsViewProjection.cs` (`Program.cs#L103`) |
| 6 | `FulfillmentPipelineViewProjection` | Singleton `Id = "current"`; counters per pipeline stage | `Id = "current"` | Multi-stream over the 5 Fulfillment events | `src/Backoffice/Backoffice/DashboardReporting/FulfillmentPipelineViewProjection.cs` (`Program.cs#L106`) |

All projections live in the `backoffice` Postgres schema.

## SignalR channels

One hub mounted at `/hub/backoffice` (`Backoffice.Api/Program.cs#L285`). Antiforgery is disabled on the WS endpoint with the in-line comment that browser same-origin enforcement of WS upgrade requests is the defence (`Program.cs#L286`).

### Group enrolment

`BackofficeHub.OnConnectedAsync` (`src/Backoffice/Backoffice.Api/BackofficeHub.cs#L41-L50`) reads the connecting principal's `ClaimTypes.Role` claim, lower-cases it, and adds the connection to a single group named `role:{role}` — for example `role:executive`, `role:operations-manager`, `role:customer-service`, `role:warehouse-clerk`, `role:warehouse-manager`, `role:system-admin`. Connections without a `UserId` claim **or** without a `Role` claim are aborted in `OnConnectedAsync` before the group join. A user is enrolled in exactly one role-group regardless of multi-role JWT scenarios — the hub takes the first `ClaimTypes.Role` claim it finds.

### Message types

Five polymorphic implementations of `IBackofficeWebSocketMessage`, each carrying its own `eventType` JSON discriminator (`src/Backoffice/Backoffice/RealTime/BackofficeEvent.cs`):

| Type | Fields | Emitted by | Status |
|------|--------|------------|--------|
| `LiveMetricUpdated` | `MetricName, Value, Timestamp` | `OrderPlacedHandler` returns one alongside the `OrderPlaced` event-store append | wired and emitted |
| `AlertCreated` | `AlertId, Severity, Message, Timestamp` | `PaymentFailedHandler` returns one alongside the `AlertFeedView` upsert | wired and emitted |
| `ActiveOrderIncremented` | `Delta, NewTotal, Timestamp` | — | declared, no instantiator in code (routes-without-instantiator) |
| `ActiveOrderDecremented` | `Delta, NewTotal, Timestamp` | — | declared, no instantiator in code |
| `PendingReturnIncremented` | `Delta, NewTotal, Timestamp` | — | declared, no instantiator in code |

Hub fan-out: handlers publish to `IHubContext<BackofficeHub>.Clients.Group("role:executive")` (and others) — there is no broadcast-to-all path. The WASM shell receives per-message updates through the singleton `BackofficeHubService` (`src/Backoffice/Backoffice.Web/Hub/BackofficeHubService.cs`) which stores and re-applies the JWT via an `AccessTokenProvider` lambda and reconnects with the staircase `[0s, 2s, 10s]` via `WithAutomaticReconnect`.

## Integration events

### Subscribed (inbound)

Backoffice configures 17 dedicated RabbitMQ queues in `Backoffice.Api/Program.cs#L221-L250`. The 21 notification handlers under `src/Backoffice/Backoffice/Notifications/` consume them. Every handler does at most two things: append a Marten event so the multi-stream projection above advances, and (for the two cases above) return a SignalR `IBackofficeWebSocketMessage`.

| Source BC | Inbound event(s) | Handler |
|-----------|------------------|---------|
| Orders | `OrderPlaced` | `OrderPlacedHandler` |
| Orders | `OrderShipped` | `OrderShippedHandler` |
| Orders | `OrderDelivered` | `OrderDeliveredHandler` |
| Orders | `OrderCancelled` | `OrderCancelledHandler` |
| Payments | `PaymentCaptured` | `PaymentCapturedHandler` |
| Payments | `PaymentFailed` | `PaymentFailedHandler` |
| Inventory | `LowStockAlertRaised` | `LowStockAlertHandler` |
| Inventory | `OutOfStockOccurred` | `OutOfStockHandler` |
| Inventory | `BackorderCreated` | `BackorderCreatedHandler` |
| Returns | `ReturnRequested` | `ReturnRequestedHandler` |
| Returns | `ReturnApproved` | `ReturnApprovedHandler` |
| Returns | `ReturnDenied` | `ReturnDeniedHandler` |
| Returns | `ReturnReceived` | `ReturnReceivedHandler` |
| Returns | `ReturnRefunded` | `ReturnRefundedHandler` |
| Fulfillment | `ShipmentHandedToCarrier` | `ShipmentHandedToCarrierHandler` |
| Fulfillment | `ShipmentDelivered` | `ShipmentDeliveredHandler` |
| Fulfillment | `ReturnToSenderInitiated` | `ReturnToSenderInitiatedHandler` |
| Fulfillment | `GhostShipmentDetected` | `GhostShipmentDetectedHandler` |
| Fulfillment | `ShipmentLostInTransit` | `ShipmentLostInTransitHandler` |
| Product Catalog | `ProductCreated` | `ProductCreatedHandler` |
| Product Catalog | `ProductPublished` | `ProductPublishedHandler` |
| Correspondence | `CorrespondenceQueued` | `CorrespondenceQueuedHandler` |
| Correspondence | `CorrespondenceDelivered` | `CorrespondenceDeliveredHandler` |
| Correspondence | `CorrespondenceFailed` | `CorrespondenceFailedHandler` |

The Fulfillment row uses event names `ShipmentHandedToCarrier` and `ReturnToSenderInitiated` per the M41.0 S5 migration that replaced the earlier `ShipmentDispatched` / `ShipmentDeliveryFailed` names — see §Prior event modeling for EM divergence.

### Published (outbound)

**None cross-BC.** Backoffice's outbound traffic is exclusively per-connection SignalR (intra-process) and synchronous HTTP proxy calls to upstream BCs (`IOrdersClient.CancelOrderAsync`, `IInventoryClient.AdjustAsync`, `IInventoryClient.ReceiveStockAsync`, `IReturnsClient.ApproveAsync`, `IReturnsClient.DenyAsync`). No `PublishMessage<>().ToRabbitQueue(...)` configuration exists in `Program.cs`.

## Sagas / orchestration

**N/A.** Backoffice runs no Wolverine saga. Every cross-BC workflow that involves Backoffice is choreographed: the operator clicks an action in the WASM shell → Backoffice HTTP endpoint translates to either a local Marten append (`OrderNote*`, `AlertFeedView`) or an HTTP delegation to the owning BC. Backoffice does not hold orchestration state across BC boundaries.

## HTTP / API surface

24 `[Wolverine{Get,Post,Put,Delete}]` endpoints across nine feature folders. All non-anonymous endpoints carry an `[Authorize(Policy="...")]`. The four broken policy strings are flagged in §Identity / auth posture.

### `AlertManagement/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/alerts/feed` | `OperationsManager` | `GetAlertFeed.Handle` (`src/Backoffice/Backoffice.Api/AlertManagement/GetAlertFeed.cs#L17`) |
| POST | `/api/backoffice/alerts/{alertId}/acknowledge` | `WarehouseClerk` | `AcknowledgeAlertEndpoint.Post` (`src/Backoffice/Backoffice.Api/AlertManagement/AcknowledgeAlertEndpoint.cs#L20`) |

### `CustomerService/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/customers/{customerId}` | `CustomerService` (broken) | `GetCustomerDetailView.Handle` |
| GET | `/api/backoffice/customers/{customerId}/correspondence` | `CustomerService` (broken) | `GetCorrespondenceHistory.Handle` |
| GET | `/api/backoffice/customers` | `CustomerService` (broken) | `GetCustomerServiceView.Handle` (composes Customer Identity + Orders) |

### `DashboardReporting/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/dashboard/metrics/{date}` | `Executive` | `GetDashboardMetrics.Handle` |
| GET | `/api/backoffice/dashboard/summary` | `Executive` | `GetDashboardSummary.Handle` |

### `OperationsHealth/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/operations/dead-letters` | `OperationsManager` | `GetDeadLetterSummary.Handle` (`src/Backoffice/Backoffice.Api/OperationsHealth/GetDeadLetterSummary.cs#L89`) |

The dead-letter endpoint opens a fresh `NpgsqlConnection` from the `ConnectionStrings:postgres` setting (`L108-L113`) rather than reusing `IDocumentSession.Connection`, then iterates an 18-schema default allow-list (`L50-L70`) using string-interpolated schema names (allow-listed against `IsValidSchemaIdentifier`) and a parameterised `@cutoff`. This is the M46.0 cross-schema dead-letter aggregator — the fresh-connection pattern is intentional because Marten's session connection is enlisted in the per-request transaction and would otherwise pin Backoffice's own schema for the lifetime of the read.

### `OrderManagement/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/orders/search` | `CustomerService` (broken) | `SearchOrders.Handle` |
| GET | `/api/backoffice/orders/{orderId}` | `CustomerService` (broken) | `GetOrderDetailView.Handle` |
| POST | `/api/backoffice/orders/{orderId}/cancel` | `CustomerService` (broken) | `CancelOrderCommandHandler.Handle` (proxies to `IOrdersClient.CancelOrderAsync`) |

### `OrderNotes/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/orders/{orderId}/notes` | `CustomerService` (broken) | `GetOrderNotes.Handle` (Marten LINQ on `OrderNote` documents filtered by `OrderId` and `IsDeleted == false`) |
| POST | `/api/backoffice/orders/{orderId}/notes` | `CustomerService` (broken) | `AddOrderNoteEndpoint.Handle` |
| PUT | `/api/backoffice/orders/{orderId}/notes/{noteId}` | `CustomerService` (broken) | `EditOrderNoteEndpoint.Handle` (author-only via `BeforeAsync`) |
| DELETE | `/api/backoffice/orders/{orderId}/notes/{noteId}` | `CustomerService` (broken) | `DeleteOrderNoteEndpoint.Handle` (author or `system-admin`) |

### `ProductCatalog/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/products` | `ProductManager` (unreachable) | `GetProductList.Handle` |

### `ReturnManagement/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/returns` | `CustomerService` (broken) | `GetReturns.Handle` |
| GET | `/api/backoffice/returns/{returnId}` | `CustomerService` (broken) | `GetReturnDetails.Handle` |
| POST | `/api/backoffice/returns/{returnId}/approve` | `CustomerService` (broken) | `ApproveReturn.Handle` (proxies to `IReturnsClient.ApproveAsync`) |
| POST | `/api/backoffice/returns/{returnId}/deny` | `CustomerService` (broken) | `DenyReturn.Handle` (proxies to `IReturnsClient.DenyAsync`) |

### `WarehouseOperations/`

| Method | Route | Authorize | Handler |
|--------|-------|-----------|---------|
| GET | `/api/backoffice/inventory` | `WarehouseClerk` | `GetInventoryList.Handle` |
| GET | `/api/backoffice/inventory/{sku}` | `WarehouseClerk` | `GetStockLevel.Handle` |
| GET | `/api/backoffice/inventory/low-stock` | `WarehouseClerk` | `GetLowStockAlerts.Handle` |
| POST | `/api/backoffice/inventory/{sku}/adjust` | `WarehouseManager` | `AdjustInventoryProxy.Handle` (proxies to `IInventoryClient.AdjustAsync`) |
| POST | `/api/backoffice/inventory/{sku}/receive` | `WarehouseManager` | `ReceiveStockProxy.Handle` (proxies to `IInventoryClient.ReceiveStockAsync`) |

The `Backoffice.Api` HTTP surface routes the WASM shell's `BackofficeApi` named `HttpClient` calls. The four `HttpClient` paths used by `Products/PriceEdit.razor` (`/api/pricing/products/{sku}/base-price`, `/api/pricing/products/{sku}/schedule`, `/api/pricing/products/{sku}`, and `/api/catalog/products/{sku}/...`) are not served by `Backoffice.Api` — only `/api/backoffice/...` and `/api/products` (singular) are registered here. This is a route-targeting drift between the Web and Api projects; the `IPricingClient` typed client is registered on the Api side but has no consumer there.

## Frontend surface

WASM shell at `src/Backoffice/Backoffice.Web/`. Bootstraps two named `HttpClient`s (`BackofficeApi` → port 5243, `BackofficeIdentityApi` → port 5051) and two singleton-lifecycle services (`BackofficeAuthService`, `BackofficeHubService`) — singletons because the WASM circuit is single-user and the JWT must outlive the per-component scope (`Backoffice.Web/Program.cs#L50-L56`).

Eight client-side authorization policies are registered (`Backoffice.Web/Program.cs#L62-L72`), keyed off the kebab-case role names that Backoffice Identity emits in JWTs (`executive`, `operations-manager`, `customer-service`, `warehouse-clerk`, `warehouse-manager`, `system-admin`). Each non-system-admin policy implicitly admits `system-admin` via an extra `RequireRole("system-admin")` `Or`. A ninth `ProductManager` policy is registered against the role string `product-manager`, which the Backoffice Identity `BackofficeRole` enum does not include — this policy is unreachable (see §Identity / auth posture).

22 `.razor` pages (route / role guard / consumed BFF endpoint / SignalR subscription):

| Page | Route | Auth | Calls (`BackofficeApi`) | SignalR subscription |
|------|-------|------|-------------------------|----------------------|
| `Index.razor` | `/` | `[AllowAnonymous]` | — | — |
| `Login.razor` | `/login` | `[AllowAnonymous]` | (calls `BackofficeAuthService.LoginAsync` → Identity API) | — |
| `Alerts.razor` | `/alerts` | `Roles = "operations-manager,system-admin"` | `GET /api/backoffice/alerts/feed`, `POST .../alerts/{id}/acknowledge` | `AlertCreated` → push into local list |
| `Dashboard.razor` | `/dashboard` | `Roles = "executive,system-admin"` | `GET /api/backoffice/dashboard/summary`, `.../dashboard/metrics/{date}` | `LiveMetricUpdated` |
| `CustomerSearch.razor` | `/customers` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/customers` | reconnect-on-update only |
| `CustomerDetail.razor` | `/customers/{customerId}` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/customers/{id}`, `.../customers/{id}/correspondence` | `LiveMetricUpdated` |
| `Orders/OrderSearch.razor` | `/orders` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/orders/search` | reconnect-on-update only |
| `Orders/OrderDetail.razor` | `/orders/{orderId}` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/orders/{id}`, `.../orders/{id}/notes`, `POST/PUT/DELETE .../orders/{id}/notes(/{noteId})`, `POST .../orders/{id}/cancel` | `LiveMetricUpdated` |
| `Returns/ReturnManagement.razor` | `/returns` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/returns` | `LiveMetricUpdated` |
| `Returns/ReturnDetail.razor` | `/returns/{returnId}` | `Roles = "customer-service,system-admin"` | `GET /api/backoffice/returns/{id}`, `POST .../approve`, `POST .../deny` | `LiveMetricUpdated` |
| `Inventory/InventoryList.razor` | `/inventory` | `Roles = "warehouse-clerk,warehouse-manager,system-admin"` | `GET /api/backoffice/inventory`, `.../inventory/low-stock` | — |
| `Inventory/InventoryEdit.razor` | `/inventory/{sku}` | `Roles = "warehouse-manager,system-admin"` | `POST /api/backoffice/inventory/{sku}/adjust`, `.../inventory/{sku}/receive` | — |
| `Products/ProductList.razor` | `/products` | `Roles = "product-manager,system-admin"` (effectively system-admin only) | `GET /api/backoffice/products` | — |
| `Products/ProductEdit.razor` | `/products/{sku}` | `Roles = "product-manager,system-admin"` | `GET /api/products/{sku}`, `PUT /api/products/{sku}/...` (target paths not served by `Backoffice.Api` — see §HTTP / API surface) | `LiveMetricUpdated` |
| `Products/PriceEdit.razor` | `/products/{sku}/price` | `Roles = "product-manager,system-admin"` | `GET/PUT /api/pricing/products/{sku}/...` (target paths not served by `Backoffice.Api`) | `LiveMetricUpdated` |
| `Listings/ListingsAdmin.razor` | `/listings` | `Roles = "product-manager,system-admin"` | (no `BackofficeApi` calls — listings UI is placeholder) | — |
| `Listings/ListingDetail.razor` | `/listings/{listingId}` | `Roles = "product-manager,system-admin"` | (placeholder) | — |
| `Marketplaces/MarketplacesList.razor` | `/marketplaces` | `Roles = "product-manager,system-admin"` | (placeholder) | — |
| `Marketplaces/CategoryMappingsList.razor` | `/marketplaces/category-mappings` | `Roles = "product-manager,system-admin"` | (placeholder) | — |
| `Users/UserList.razor` | `/users` | `Policy = "SystemAdmin"` | `GET /api/backoffice-identity/users` (Identity API directly) | — |
| `Users/UserCreate.razor` | `/users/new` | `Policy = "SystemAdmin"` | `POST /api/backoffice-identity/users` | — |
| `Users/UserEdit.razor` | `/users/{userId}` | `Policy = "SystemAdmin"` | `PUT /api/backoffice-identity/users/{id}/role`, `.../deactivate`, `.../reset-password` | — |

The `BackofficeHubService` is injected at the layout level so the singleton SignalR connection survives navigation; pages that subscribe attach handlers in `OnInitializedAsync` and detach in `IDisposable.Dispose`.

## Identity / auth posture

Backoffice consumes JWTs issued by **Backoffice Identity** (`BackofficeIdentityServer.Api` at port 5051). Tokens are HS256-signed with the symmetric key from `Jwt:SigningKey`; the API project validates issuer (`Jwt:Issuer`), audience (`Jwt:Audience`), lifetime, and signing key (`Backoffice.Api/Program.cs#L23-L52`). Multi-issuer setup (Customer Identity + Vendor Identity + Backoffice Identity) is governed by ADR 0032 — Backoffice accepts only the Backoffice issuer.

WS upgrade carries the JWT via the `access_token` query string parameter, intercepted by `JwtBearerEvents.OnMessageReceived` when the request path starts with `/hub/backoffice` (`Program.cs#L39-L51`). This is the standard SignalR pattern because browsers do not send `Authorization` headers on `WebSocket` upgrade requests.

The WASM shell stores the access token in-memory only (no `localStorage` / `sessionStorage`) inside `BackofficeAuthService` (`Backoffice.Web/Auth/BackofficeAuthService.cs`), and refreshes via the Identity API's HTTP-only refresh cookie. The hub service's `AccessTokenProvider` lambda re-reads the live in-memory token on every reconnect.

### Eight RBAC policies on the API (`Backoffice.Api/Program.cs#L55-L79`)

| Policy name | `RequireRole(...)` argument(s) | JWT claim emitted by Identity | Status |
|-------------|--------------------------------|--------------------------------|--------|
| `Executive` | `executive`, `system-admin` | `executive` | OK |
| `OperationsManager` | `operations-manager`, `system-admin` | `operations-manager` | OK |
| `CustomerService` | **`cs-agent`**, `system-admin` | `customer-service` | **broken — string mismatch; reachable only by `system-admin`** |
| `WarehouseClerk` | `warehouse-clerk`, `warehouse-manager`, `system-admin` | `warehouse-clerk` / `warehouse-manager` | OK |
| `WarehouseManager` | `warehouse-manager`, `system-admin` | `warehouse-manager` | OK |
| `ProductManager` | **`product-manager`**, `system-admin` | (`BackofficeRole` enum has no `ProductManager` value — Identity never emits this claim) | **unreachable for non-admins** |
| `SystemAdmin` | `system-admin` | `system-admin` | OK |
| `Backoffice` | any of the seven roles | (any) | OK — used as the catch-all default |

The seven `BackofficeRole` enum values issued by Backoffice Identity are: `Executive`, `OperationsManager`, `CustomerService`, `WarehouseClerk`, `WarehouseManager`, `SystemAdmin`, `Auditor` (`BackofficeIdentityServer.Domain/Roles/BackofficeRole.cs`). The `Auditor` value is enumerated and JWT-emitted as `auditor` but no Backoffice API policy admits it — `Auditor` users hit 403 on every `[Authorize]` route except those gated by the catch-all `Backoffice` policy. There is no `ProductManager` enum value at the Identity side, hence the `ProductManager` policy registered above admits only `system-admin`.

The Razor pages bypass the API policy registration in most cases by using `@attribute [Authorize(Roles = "...")]` with the kebab-case role string directly (e.g. `Alerts.razor#L5`). Three pages use `Policy = "SystemAdmin"` — the three `Users/*.razor` admin pages.

Together these are the four routes-without-instantiator items in §Project trajectory below: `cs-agent` (a role string the Identity issuer never emits), `ProductManager` (a policy that maps to a role the issuer cannot issue), `IFulfillmentClient` (DI-registered, never injected), `IBackofficeIdentityClient` (DI-registered, never injected on the Api side).

## Tests

### Gherkin (BDD)

- 16 features in `tests/Backoffice/Backoffice.E2ETests/Features/` totalling **137 scenarios** (13 of those features are operator-flow E2E across alerts / customer service / orders / inventory / returns / dashboard).
- 5 features in `docs/features/backoffice/` totalling **47 scenarios** (specification-by-example artefacts authored alongside the EM revisions; the corresponding step bindings live with the E2E features above where coverage exists).

### xUnit / Reqnroll integration

- `tests/Backoffice/Backoffice.IntegrationTests/` — 17 test classes, ~95 `[Fact]/[Theory]` tests using `Backoffice.Api.IntegrationTestFixture` (Alba host + Testcontainers Postgres + RabbitMQ container).
- `tests/Backoffice/Backoffice.UnitTests/` — 3 test classes, ~25 `[Fact]/[Theory]` tests covering `OrderNote.Apply` decisions, `AlertFeedView` document mutation, and `IsValidSchemaIdentifier` allow-listing.
- E2E projects use Reqnroll with Playwright drivers; bindings are split between `Backoffice.E2ETests/StepDefinitions/` and shared step files in `tests/SharedTesting/`.

## Prior event modeling

Three prior artefacts cover Backoffice modelling:

1. `docs/planning/backoffice-event-modeling.md` — initial workshop output.
2. `docs/planning/backoffice-event-model-critique.md` — review pass that flagged the `OrderNote` storage choice and the `AlertAcknowledgment` shape.
3. `docs/planning/backoffice-event-modeling-revised.md` (2026-03-14, 641 lines) — the post-critique reconciliation that names two aggregates (`OrderNote`, `AlertAcknowledgment`) plus an `EscalationTicket` Phase 2 stretch.

### Divergences between the revised EM and current code

- **`OrderNote` storage type.** EM revised lines 478-501 prescribe a Marten document store keyed by `{orderId}:{noteId}`. The shipped code is event-sourced with a single-`Guid` UUID v7 stream id per ADR 0037 (`AddOrderNoteEndpoint.cs#L52`). The composite-key form is unimplemented.
- **`AlertAcknowledgment` aggregate.** EM revised lines 510-525 carve acknowledgment out as a separate aggregate with its own stream. Code stores acknowledgment as fields directly on the `AlertFeedView` document (`AcknowledgeAlertHandler` mutates `IsAcknowledged`, `AcknowledgedByUserId`, `AcknowledgedByDisplayName`, `AcknowledgedAt`). No separate stream exists.
- **`EscalationTicket` Phase 2.** EM revised lines 527-545 outline an escalation-ticket aggregate gated as Phase 2 work. Not present in code — no aggregate, no events, no commands, no projections, no notification handler.
- **RabbitMQ subscription table divergences.**
  - EM lists `RefundCompleted` and `StockReplenished` handlers — neither exists in `Backoffice/Notifications/`.
  - EM lists Fulfillment events as `ShipmentDispatched` and `ShipmentDeliveryFailed` — code uses `ShipmentHandedToCarrier` and `ReturnToSenderInitiated` per the M41.0 S5 rename migration.
  - Code adds three handlers not enumerated in the EM table: `BackorderCreatedHandler`, `GhostShipmentDetectedHandler`, `ShipmentLostInTransitHandler`.

## ADRs

- **ADR 0032** — Multi-issuer JWT acceptance across BC APIs (Backoffice consumes Backoffice-Identity-issued tokens; rejects Customer / Vendor issuers).
- **ADR 0037** — Event-sourcing the `OrderNote` aggregate with UUID v7 stream ids minted client-side, in lieu of the EM-revised Marten document with composite key.
- **ADR 0046** — Cross-schema dead-letter aggregator using a fresh `NpgsqlConnection` and a 18-schema allow-list, validated by `IsValidSchemaIdentifier` (drives `OperationsHealth/GetDeadLetterSummary.cs`).

## Source citations

- `src/Backoffice/Backoffice.Api/Program.cs` — JWT config (L23-L52), 8 RBAC policies (L55-L79), Marten + 6 projections (L82-L107), 8 typed `HttpClient`s (L125-L171), 17 RabbitMQ queues (L221-L250), hub mount (L285).
- `src/Backoffice/Backoffice.Api/BackofficeHub.cs` — single hub, role-based group enrolment lower-cased on `OnConnectedAsync` (L41-L50); rejects connections without `UserId` or `Role` claims.
- `src/Backoffice/Backoffice.Api/OrderNotes/AddOrderNoteEndpoint.cs#L46-L75` — `MartenOps.StartStream<OrderNote>(noteId, addedEvent)` with UUID v7 mint at L52.
- `src/Backoffice/Backoffice.Api/OrderNotes/EditOrderNoteEndpoint.cs#L62-L77` — author-only `BeforeAsync` guard.
- `src/Backoffice/Backoffice.Api/OrderNotes/DeleteOrderNoteEndpoint.cs#L65-L80` — author or `system-admin` `BeforeAsync` guard.
- `src/Backoffice/Backoffice.Api/OperationsHealth/GetDeadLetterSummary.cs` — M46.0 dead-letter aggregator (L50-L70 schema allow-list, L89 `OperationsManager` policy, L108-L113 fresh `NpgsqlConnection`).
- `src/Backoffice/Backoffice.Api/AlertManagement/AcknowledgeAlertEndpoint.cs#L20-L52` — `WarehouseClerk` policy + JWT `sub` extraction + `IMessageBus.InvokeAsync(AcknowledgeAlert)`.
- `src/Backoffice/Backoffice/OrderNote/OrderNote.cs` — single-stream aggregate with `Create(IEvent<OrderNoteAdded>)` (L21-L32), `Apply(OrderNoteEdited)` (L34-L42), `Apply(OrderNoteDeleted)` (L44-L48).
- `src/Backoffice/Backoffice/OrderNote/OrderNoteEvents.cs` — three domain events.
- `src/Backoffice/Backoffice/AlertManagement/AcknowledgeAlert.cs` — command + handler that loads `AlertFeedView` and stores updated copy.
- `src/Backoffice/Backoffice/AlertManagement/AlertFeedView.cs` — document with 6 alert types and 3 severities.
- `src/Backoffice/Backoffice/AlertManagement/AlertFeedViewProjection.cs` — multi-stream over 7 inbound integration events.
- `src/Backoffice/Backoffice/DashboardReporting/AdminDailyMetricsProjection.cs` — date-keyed multi-stream projection.
- `src/Backoffice/Backoffice/DashboardReporting/ReturnMetricsViewProjection.cs` — singleton `"current"` multi-stream projection.
- `src/Backoffice/Backoffice/DashboardReporting/CorrespondenceMetricsViewProjection.cs` — singleton `"current"` multi-stream projection.
- `src/Backoffice/Backoffice/DashboardReporting/FulfillmentPipelineViewProjection.cs` — singleton `"current"` multi-stream projection.
- `src/Backoffice/Backoffice/Notifications/` — 21 handlers; only `OrderPlacedHandler` and `PaymentFailedHandler` additionally return SignalR messages.
- `src/Backoffice/Backoffice/RealTime/BackofficeEvent.cs` — 5 polymorphic SignalR message types with `eventType` discriminator (3 declared without an instantiator).
- `src/Backoffice/Backoffice/Composition/` — 5 view records (`CorrespondenceHistoryView`, `CustomerDetailView`, `CustomerServiceView`, `OrderDetailView`, `ReturnDetailView`).
- `src/Backoffice/Backoffice/Clients/` — 9 typed client interfaces (`IBackofficeIdentityClient`, `ICatalogClient`, `ICorrespondenceClient`, `ICustomerIdentityClient`, `IFulfillmentClient`, `IInventoryClient`, `IOrdersClient`, `IPricingClient`, `IReturnsClient`).
- `src/Backoffice/Backoffice.Api/Clients/` — 9 implementations; `IFulfillmentClient` and `IBackofficeIdentityClient` are registered without consumers in `Backoffice.Api/`.
- `src/Backoffice/Backoffice.Web/Program.cs#L50-L56` — singleton lifecycle for `BackofficeAuthService` and `BackofficeHubService`; `Program.cs#L62-L72` — 8 client-side authorization policies.
- `src/Backoffice/Backoffice.Web/Hub/BackofficeHubService.cs` — singleton WASM SignalR client; `AccessTokenProvider` lambda; `WithAutomaticReconnect([0s, 2s, 10s])`.
- `src/Backoffice/Backoffice.Web/Pages/` — 22 `.razor` pages.
- `tests/Backoffice/Backoffice.E2ETests/Features/` — 16 features / 137 scenarios.
- `docs/features/backoffice/` — 5 features / 47 scenarios.
- `tests/Backoffice/Backoffice.IntegrationTests/` and `Backoffice.UnitTests/` — 20 test classes total.
- `CONTEXTS.md` lines 273-291 — Backoffice section consulted for drift comparison.
- `docs/planning/backoffice-event-modeling-revised.md` lines 478-549 — EM revised aggregate / RabbitMQ table consulted for divergence analysis.
