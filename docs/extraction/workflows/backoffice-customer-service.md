# Backoffice customer-service composition

> **Status:** Active (workflow); broken auth (4 endpoints with mis-spelled policy strings — operator access fails)
> **Type:** Synchronous BFF composition (HTTP fan-out across multiple BCs)
> **Initiating actor:** Operator (Customer Service role)
> **BCs involved:** Backoffice (BFF) ← Customer Identity, Orders, Returns, Correspondence
> **Most recent material milestone:** M43–M45 — composition view rollouts

## Purpose

A Customer Service operator opens a customer's record in the Backoffice WASM shell. Backoffice composes a `CustomerDetailView` (or `CustomerServiceView` / `OrderDetailView` / `CorrespondenceHistoryView` / `ReturnDetailView`) by synchronously calling four typed HTTP clients across upstream BCs and assembling a DTO. Operators may add `OrderNote`s (the only event-sourced aggregate Backoffice owns), acknowledge alerts on the customer's orders, or proxy actions (cancel order, approve / deny return) to the owning BC via the typed client.

## Actors and triggers

- **Initiating actor:** Operator with Backoffice Identity role `CustomerService` (per ADR 0032 cross-issuer registration)
- **Trigger:** Operator HTTP request to one of Backoffice's customer-service endpoints (e.g. `GET /api/backoffice/customers/{customerId}` or `GET /api/backoffice/orders/search?...`)
- **Prerequisite state:** Operator authenticated via Backoffice Identity; JWT carries `ClaimTypes.Role = "CustomerService"` (or other appropriate role)

## Trace (read composition — `GET /api/backoffice/customers/{customerId}`)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Backoffice | Operator request reaches `GetCustomerDetailView.Handle`; `[Authorize(Policy="CustomerService")]` (broken — see Declared vs. implemented) | Auth check (currently failing because policy name is mis-spelled) | `bcs/backoffice.md#http--api-surface` |
| 2 | Backoffice | If auth passes: handler calls `ICustomerIdentityClient` (4 endpoints) for customer profile + addresses | `CustomerDetailView` shell populated | `bcs/backoffice.md#composition-map` |
| 3 | Backoffice | Handler calls `IOrdersClient` (5 endpoints) for the customer's order history; composes `OrderSummaryView` records into the view | Order summaries attached | `bcs/backoffice.md#composition-map` |
| 4 | Backoffice | For each order, handler may call `IReturnsClient` (4 endpoints) and `ICorrespondenceClient` (2 endpoints) for return + correspondence detail | `CustomerDetailView`, `CustomerAddressView`, `CorrespondenceHistoryView`, `CorrespondenceMessageView` records composed | `bcs/backoffice.md#composition-map`, composition view records in `src/Backoffice/Backoffice/Composition/` |
| 5 | Backoffice | Returns the composed DTO to the operator | Operator sees a unified customer record | (HTTP response) |

## Trace (write action — `POST /api/backoffice/orders/{orderId}/cancel` proxy)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Backoffice | `CancelOrderCommand` DTO (named "Command" but not Wolverine-dispatched) reaches the cancel endpoint; validates request; extracts `adminUserId` from JWT | DTO validated | `bcs/backoffice.md#commands` |
| 2 | Backoffice | Calls `IOrdersClient.CancelOrderAsync(orderId, ct)` which `POST`s to Orders' `/api/orders/{orderId}/cancel` with the operator-acting-as-admin context | Cross-BC HTTP delegation | `bcs/backoffice.md#commands` |
| 3 | Orders | The Order saga's `CancelOrder` handler runs through the standard cancellation compensation (see `order-saga.md` — `CanBeCancelled` gate, `ReservationReleaseRequested` fan-out, `RefundRequested` if captured, `OrderCancelled` broadcast) | Saga compensation cascades to Inventory, Payments, Fulfillment, CX, Backoffice (via its own subscription) | `bcs/orders.md#compensation-and-divergent-paths`, `workflows/order-saga.md` |

## Trace (write action — `POST /api/backoffice/order-notes`)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Backoffice | `AddOrderNote` command — `AddOrderNoteEndpoint.Handle` mints a UUID v7 (`Guid.CreateVersion7()`) as the stream id; appends `OrderNoteAdded` to a new Marten stream | New `OrderNote` stream; `OrderNoteSnapshot` projection materialises via inline single-stream aggregation | `bcs/backoffice.md#aggregates`, `bcs/backoffice.md#domain-events` |
| 2 | Backoffice | Subsequent operator edits → `EditOrderNote` → `OrderNoteEdited`; deletes → `DeleteOrderNote` → `OrderNoteDeleted` (soft-delete — stream not archived; document left in place for audit) | Per-note state machine: `Created → Edited* → Deleted` | `bcs/backoffice.md#aggregates` |

`DeleteOrderNote.BeforeAsync` allows deletion only by the original author **or** any caller carrying the `system-admin` role claim.

## Projections and views

Composition view records assembled at the BFF — never persisted (all under `src/Backoffice/Backoffice/Composition/`):

- `CustomerDetailView` + `CustomerAddressView`
- `CustomerServiceView` + `OrderSummaryView`
- `OrderDetailView` + `OrderLineItemView` + `ReturnableItemView`
- `ReturnDetailView` + `ReturnItemView`
- `CorrespondenceHistoryView` + `CorrespondenceMessageView`

Backoffice-owned projections relevant to customer service:

- `OrderNote` snapshot (single-stream aggregation; queryable without `LiveStreamAggregation`).
- `AlertFeedView` — operator filters alerts pertaining to the customer / their orders.

## Compensation paths

### Failure: Customer Identity HTTP call times out / 5xx
- **Compensating action:** No system-level compensation today. Operator sees the partial composition or a 5xx; retry through the WASM shell.
- **Resulting state:** Operator must retry; no Backoffice state mutated.

### Failure: Cancel order proxy returns 4xx from Orders
- **Compensating action:** Backoffice surfaces the error to the operator; no local state mutated. Order saga's `CanBeCancelled` gate decides eligibility.
- **Resulting state:** Operator must address the underlying ineligibility (e.g. order already `Delivered` or `Closed`).

### Failure: Operator's role policy is mis-spelled (4 customer-service endpoints)
- **Compensating action:** None. The endpoints are inaccessible until the policy strings are corrected. Documented in `bcs/backoffice.md#identity--auth-posture` and flagged across the workflow as the operative blocker.
- **Resulting state:** Customer Service operators cannot use `GET /api/backoffice/customers/{customerId}`, `GET /api/backoffice/customers/{customerId}/correspondence`, `GET /api/backoffice/customers`, or `GET /api/backoffice/orders/search` until fixed.

## Variants and edge cases

### `OrderNote` is the only ES aggregate in Backoffice
Per the "C-hybrid" label on the dossier. Multiple notes per order are independent streams; `orderId` is a field on every event but not part of the stream key. Dossier: `bcs/backoffice.md#aggregates`.

### Cross-BC writes are HTTP proxies, not message-based
Backoffice publishes no cross-BC integration events — its outbound surface is HTTP proxy calls (`IOrdersClient.CancelOrderAsync`, `IInventoryClient.AdjustAsync`, `IInventoryClient.ReceiveStockAsync`, `IReturnsClient.ApproveAsync`, `IReturnsClient.DenyAsync`) plus per-connection SignalR. Dossier: `bcs/backoffice.md#integration-events` (Published section).

### Soft delete on `OrderNote`
`OrderNoteDeleted` sets `IsDeleted = true` but does not archive the stream; the document is left in place so audit reads can still load the soft-deleted note.

## BCs and roles

- **Backoffice** — BFF composer + HTTP-proxy router + `OrderNote` owner. Dossier: `bcs/backoffice.md`.
- **Customer Identity** — Read source via `ICustomerIdentityClient` (4 endpoints). Dossier: `bcs/customer-identity.md`.
- **Orders** — Read source via `IOrdersClient` (5 endpoints); write target via `IOrdersClient.CancelOrderAsync`. Dossier: `bcs/orders.md`.
- **Returns** — Read source via `IReturnsClient` (4 endpoints); write target via `IReturnsClient.ApproveAsync` / `DenyAsync`. Dossier: `bcs/returns.md`.
- **Correspondence** — Read source via `ICorrespondenceClient` (2 endpoints). Dossier: `bcs/correspondence.md`.

## Tests as behavioral evidence

- **Integration tests:** `tests/Backoffice/Backoffice.Api.IntegrationTests/` covers each composition endpoint, each typed client interaction (with stubbed upstreams), and the `OrderNote` aggregate state machine.

## ADRs

- **ADR 0032** — Cross-issuer JWT registration; Backoffice + Vendor Identity registered as separate schemes on every downstream API. File: `docs/decisions/0032-*.md`.

## Declared vs. implemented

- **Declared shape:** Customer-service endpoints carry `[Authorize(Policy="CustomerService")]` etc., expecting role-based access for `CustomerService` operators.
- **Implemented shape:** Four endpoint policies are mis-spelled (see `bcs/backoffice.md#identity--auth-posture` — flagged as "broken" against `AddOrderNote`, `EditOrderNote`, `DeleteOrderNote`, customer-service composition, `SearchOrders`). Operators with the `CustomerService` role cannot access the endpoints.
- **Gap:** Operative blocker; **fix is one-line per endpoint** (correct the policy string). Dossier source: `bcs/backoffice.md#identity--auth-posture`.

- **Declared shape:** `IBackofficeIdentityClient` and `IFulfillmentClient` are registered typed `HttpClient`s.
- **Implemented shape:** No consumer in `Backoffice.Api/` — `IBackofficeIdentityClient` is unused because the Web shell calls the Identity API directly via the `BackofficeIdentityApi` named `HttpClient`; `IFulfillmentClient` is unused entirely.
- **Gap:** Dead code in the composition root. Dossier source: `bcs/backoffice.md#composition-map`.

## Source citations

- Dossier sections referenced: `bcs/backoffice.md#aggregates`, `bcs/backoffice.md#commands`, `bcs/backoffice.md#composition-map`, `bcs/backoffice.md#http--api-surface`, `bcs/backoffice.md#identity--auth-posture`, `bcs/backoffice.md#integration-events`.
- ADRs: 0032.
- Tests: `tests/Backoffice/Backoffice.Api.IntegrationTests/`.
