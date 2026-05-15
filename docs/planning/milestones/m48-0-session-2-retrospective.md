# M48.0 Session 2 Retrospective — Commerce-Core Deep Dive (partial)

**Date:** 2026-05-15
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 2 — Promote 9 commerce-core stub dossiers to S2 — full depth

## Outcome (executive summary)

S2 closed in a partial state: **7 of the 9 planned dossiers were promoted to S2 — full depth in place** (Shopping, Customer Identity, Customer Experience, Product Catalog, Orders, Payments, Inventory). The two largest remaining dossiers — **Fulfillment (56 events) and Returns (21 events, no prior EM)** — were deferred. Fulfillment had source enumeration completed but no dossier file was written; Returns was not started. Both will be picked up in a follow-up Session 2 continuation before S3 begins.

## Baseline

- Build at session open: **0 errors, 359 warnings** (identical to M48.0 S1 close).
- Build at session close: **0 errors, 359 warnings** — identical (no code changed).
- Files changed: 9 (7 BC dossiers, this retrospective, `CURRENT-CYCLE.md`, `docs/extraction/README.md` status table).

## Items Completed

| Item | Description | File | Counts |
|------|-------------|------|--------|
| S2a | Shopping dossier (Variant A) | `docs/extraction/bcs/shopping.md` | 1 aggregate (Cart) / 9 events / 8 commands |
| S2b | Customer Identity dossier (Variant B: EF Core) | `docs/extraction/bcs/customer-identity.md` | 2 entities (Customer, CustomerAddress) / 0 events / 6 commands |
| S2c | Customer Experience dossier (Variant C: BFF) | `docs/extraction/bcs/customer-experience.md` | 0 aggregates / 8 upstream BCs in composition map / 5 SignalR channels / 10 frontend pages / 23 integration events subscribed / 0 published |
| S2d | Product Catalog dossier (Variant A; closed S1 carry-over) | `docs/extraction/bcs/product-catalog.md` | 1 ES aggregate (CatalogProduct) + 1 legacy doc (Product) / 12 events / 13 commands |
| S2e | Orders dossier (Variant A; saga orchestrator) | `docs/extraction/bcs/orders.md` | 2 aggregates (Checkout, Order) / 11 events / 11 commands; saga has 16 states; 10 integration events published, 24 consumed (9 saga-driving) |
| S2f | Payments dossier (Variant A; saga participant) | `docs/extraction/bcs/payments.md` | 1 aggregate (Payment) / 5 events / 4 commands |
| S2g | Inventory dossier (Variant A; sub-grouped) | `docs/extraction/bcs/inventory.md` | 2 aggregates (ProductInventory, InventoryTransfer) / 29 events sub-grouped by lifecycle phase / 20 commands |

## Items Deferred (not completed in this session)

| Item | Description | File | Status |
|------|-------------|------|--------|
| S2h | Fulfillment dossier (Variant A; heavy sub-grouping for 56 events) | `docs/extraction/bcs/fulfillment.md` | **Deferred.** Source enumeration completed (per-aggregate event counts, lifecycle-phase axes, command surface, projection set, HTTP surface) — captured below for the follow-up session. Dossier file unchanged from S1 stub. |
| S2i | Returns dossier (Variant A; first-time formal modeling) | `docs/extraction/bcs/returns.md` | **Deferred.** Not started. Dossier file unchanged from S1 stub. |

## Per-BC Dossier Notes

### Shopping (S2a)

- Aggregate: 1 (`Cart`). Events: 9. Commands: 8. Reconciliation: counts match S1 stub exactly (1 / 9 / 8).
- Saga participation: not a saga orchestrator; emits `CheckoutInitiated` integration message that drives the Order saga forward.
- Notable ADRs cited: ADR 0001 (Checkout migration to Orders), ADR 0017 (Price freeze at add-to-cart).
- Test-file pointer count: 1 features file referenced + integration suite under `tests/Shopping/`.
- Surprises surfaced at dossier depth (not in S1 stub):
  - `CartAbandoned` is declared and has a `Cart.Apply` overload + unit-test coverage, but no production handler appends it. The Shopping.Api README documents it as an unimplemented background-job emitter. Counted among the 9 events on the strength of the record declaration.
  - ADR 0017 describes a `PriceFrozenAt` field on `CartLineItem` and a TTL-driven `PriceRefreshed` flow; neither exists in the current code. Surfaced descriptively in the ADRs section.

### Customer Identity (S2b — Variant B: EF Core)

- Entities: 2 (`Customer`, `CustomerAddress`). Events: 0. Commands: 6. Queries: 5 (`GetCustomer`, `GetCustomerAddresses`, `GetCustomerByEmail`, `GetAddressSnapshot`, `GetCurrentUser`).
- Saga participation: not a participant.
- Notable ADRs cited: ADR 0002 (EF Core for Customer Identity), ADR 0012 (session-cookie auth).
- Surprises surfaced at dossier depth:
  - **`CustomerIdentity.AddressSnapshot` is not a published RabbitMQ integration event** despite the S1 stub listing it as one. It is a synchronous HTTP response payload duplicated as a shared record in `Messages.Contracts/CustomerIdentity/AddressSnapshot.cs` so consumer BCs (Orders, Customer Experience) can deserialize without a project reference. Dossier corrects this.
  - DbSet name is `Addresses` but the CLR entity type is `CustomerAddress` — flagged.
  - Auth posture is broader than S1 indicated: a `JwtBearer("Backoffice")` scheme is registered alongside the session cookie, with two policies (`CustomerService`, `OperationsManager`) gating Backoffice-side reads. Dossier documents both schemes.
  - Two endpoints (`AddAddress`, `GetCustomerAddresses`) carry no `[Authorize]` attribute. Recorded factually as "anonymous on the endpoint."

### Customer Experience (S2c — Variant C: BFF)

- Aggregates: 0. Composition map covers 8 upstream BCs. SignalR channels: 5 (all on the single `customer:{customerId}` group through `StorefrontHub`). Frontend pages: 10 + cross-page Layout surface. Integration events: 23 subscribed, 0 published.
- Saga participation: not a participant; pure relay.
- Notable ADRs cited: ADR 0013 (SSE → SignalR migration), ADR 0034 (Backoffice BFF — referenced for the BFF-pattern background, not Customer Experience–specific), ADR 0043 (Storefront Web technology options).
- Surprises surfaced at dossier depth:
  - **CONTEXTS.md names 6 upstream BCs in the Customer Experience integration table; the code wires 8.** Inventory (`ReservationConfirmed`) and Returns (10 return-lifecycle events) are subscribed in code but absent from the CONTEXTS.md table. Surfaced in the dossier with a parenthetical note flagging the divergence on each BC entry. **Forward-note for S5 (structural observations).**

### Product Catalog (S2d)

- Aggregates: 1 ES (`CatalogProduct`) + 1 Marten document (`Product`). Events: 12 (matches S1). Commands: 13 (S1 deferred the count — this is the definitive enumeration).
  - Content/lifecycle commands (11): `CreateProduct`, `MigrateProduct`, `ChangeProductName`, `ChangeProductDescription`, `ChangeProductCategory`, `ChangeProductDimensions`, `ChangeProductStatusCommand`, `UpdateProductImages`, `UpdateProductTags`, `SoftDeleteProduct`, `RestoreProduct`.
  - Vendor-assignment commands (2): `AssignProductToVendor`, `BulkAssignProductsToVendor`.
- Saga participation: not a saga orchestrator or participant.
- Notable ADRs cited: only those that materially shaped Product Catalog's M35.0 ES migration; ADRs 0048–0050 are flagged as belonging primarily to Listings/Marketplaces (S3).
- Surprises surfaced at dossier depth:
  - **Status of the legacy `Product` document store:** no HTTP-exposed handler writes to it. The only `session.Store(product)` call is in `SeedData.cs#L446` (Development-only seed). `AssignProductToVendor` no longer writes to it — it appends `ProductVendorAssigned` to the `CatalogProduct` stream. One active reader: `MigrateProductHandler.Handle` loads the document to construct a `ProductMigrated` event.
  - **CONTEXTS.md drift surfaced:** line 158 of CONTEXTS.md still labels `AssignProductToVendor` as the "sole remaining document-store write path" — stale relative to current code. **Forward-note for S5.**

### Orders (S2e — saga orchestrator)

- Aggregates: 2 (`Checkout` event-sourced; `Order` saga document with numeric revisions). Events: 11 (matches S1). Commands: 11 (matches S1).
- Saga participation: **owns the Order saga.** State machine: 16 states (`OrderStatus` enum). Integration events published: 10. Integration events consumed: 24 (9 are saga-driving; the rest are passive replies, status-only mutators, or no-op acknowledgers).
- Notable ADRs cited: ADR 0029 (Decider pattern + pure-function saga logic — central), ADR 0001 (Checkout migration), ADR 0061 (replacement reservation — Orders is acknowledger, not orchestrator), ADR 0062 (Payments choreography — Orders is acknowledger).
- Surprises surfaced at dossier depth:
  - M45.1 fraud-review event names in source (`OrderPutOnHold`, `OrderReleasedFromHold`, `OrderRejectedForFraud`) are slightly shorter than the descriptive forms the S1 stub used.
  - `OrderPlaced` exists as both a domain record (`Orders.Placement.OrderPlaced`) and an integration contract (`Messages.Contracts.Orders.OrderPlaced`) — counted once in the dossier; reconciliation note added.

### Payments (S2f — saga participant)

- Aggregate: 1 (`Payment`). Events: 5. Commands: 4. All counts match S1 stub.
- Saga participation: participant in the Order saga (Orders → Payments) and in the cross-product-exchange Payments↔Returns choreography (ADR 0062).
- Notable ADRs cited: ADR 0010 (Stripe + IPaymentGateway strategy), ADR 0062 (cross-product-exchange Payments choreography).
- Surprises surfaced at dossier depth:
  - **ADR 0062 (M47.0/S2 + S4) added behavior without adding command or domain-event records.** Three new inbound integration-message handlers (`CaptureExchangeDeltaHandler`, `IssueExchangePartialRefundHandler`, `RefundExchangeDeltaHandler`) and six new integration-event contracts. `PaymentRefunded` was extended with an optional `ReturnId` field rather than replaced. Reconciliation note added.
  - Saga-reply set: `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `RefundCompleted`, `RefundFailed`. Cross-product-exchange set: `ExchangeDeltaCaptured`, `ExchangeDeltaCaptureFailed`, `ExchangePartialRefundIssued` (out); `ExchangeAdditionalPaymentRequired`, `ExchangePartialRefundRequested`, `RefundExchangeDeltaRequested` (in).

### Inventory (S2g)

- Aggregates: 2 (`ProductInventory` 24 events; `InventoryTransfer` 5 events). Total events: **29.** Commands: 20.
- Saga participation: participant in the Order saga and in the Returns cross-product-exchange (ADR 0061).
- Notable ADRs cited: ADR 0060 (BC remaster rationale — central), ADR 0061 (cross-product-exchange replacement reservation, M47.0/S1).
- Stream IDs: **UUID v5** via `InventoryStreamId.Compute(sku, warehouseId)` — a notable departure from UUID v7. Surfaced in the dossier.
- Async projections: 3 (`AlertFeedView`, `NetworkInventorySummaryView`, `BackorderImpactView`) — per the M42.3 audit.
- Surprises surfaced at dossier depth:
  - **+2 events vs S1 (S1 said 27, actual 29).** The two not separately tallied in S1 are `StockTransferredIn` and `StockTransferredOut` — distinct event records emitted on `ProductInventory` streams when a transfer ships/receives, easy to conflate with `TransferShipped`/`TransferReceived` on `InventoryTransfer`.
  - **−1 command vs S1 (S1 said 21, actual 20 explicit records).** The pick or ship operation is modeled as a handler reaction to a Fulfillment integration message rather than as a local command record.
  - Reconciliation notes added in the dossier.

### Fulfillment (S2h — DEFERRED)

Source enumeration was completed before time ran out; the dossier file itself was not written. Captured here for the follow-up session:

- Aggregates: 2 (`WorkOrder`, `Shipment`).
- Per-aggregate event counts (from `grep "public sealed record" *Events.cs`): WorkOrder = 24, Shipment = 31 (30 in `ShipmentEvents.cs` + 1 in `ShipmentDelivered.cs`). **Total = 55** in-domain Marten events. **S1 stub claimed 56; deviation = −1.** The closest candidate for the missing 56th is `FulfillmentRequested` — present as a record in `Shipments/FulfillmentRequested.cs` but used as the inbound integration message (handled by `FulfillmentRequestedHandler.cs`), not appended to the Shipment stream as a domain event. Including it yields 56 to match S1; excluding it (correct, since it is an inbound contract) yields 55. The follow-up dossier should record the correct count as 55 with this reconciliation note.
- Suggested lifecycle-phase axes (validated against file inspection):
  - **WorkOrder:** Intake → Picking → Packing → Hazmat → Exception → SLA / Cancel
  - **Shipment:** Routing → Label → Carrier handoff → Tracking → Delivery → Return / Reship → Exception / Claims
- Commands: 27 (matches S1 — verified all 27 source files exist under `WorkOrders/`, `Shipments/`, `Routing/`).
- HTTP surface: only 2 endpoints (`GET /api/fulfillment/shipments`, `POST /api/fulfillment/carrier-webhook`); all other commands are message-handled.
- Projections (from `Program.cs#L45-L55`, all inline): `Shipment` snapshot, `WorkOrder` snapshot, `ShipmentStatusViewProjection`, `CarrierPerformanceViewProjection`, `MultiShipmentViewProjection`.
- Legacy/successor pair status: `ShipmentDispatched` and `ShipmentDeliveryFailed` exist **only** in `src/Shared/Messages.Contracts/Fulfillment/` as integration record types — **not** as domain events in `Fulfillment/Shipments/ShipmentEvents.cs`. No handler in `src/Fulfillment/` currently emits them; the M41.0 successors `ShipmentHandedToCarrier` and `ReturnToSenderInitiated` are emitted instead. Present-as-record-only on the integration contract surface, retained alongside the M41.0 successors. (`Fulfillment.Api/README.md` still references the legacy pair in narrative diagrams — documentation lag, not active emission. **Forward-note for S5.**)
- Notable ADRs to cite: ADR 0059 (Fulfillment BC remaster — central).

### Returns (S2i — DEFERRED, not started)

No source enumeration performed. The S1 stub remains in place. The follow-up session should follow the same Variant A template, give substantial weight to the Sagas / orchestration section (Returns owns the cross-product-exchange saga per ADRs 0061 and 0062, with Inventory and Payments as participants), and acknowledge in the "Prior event modeling" section that the dossier is the first formal modeling artifact for this BC.

## Confirmation Checks (over the 7 dossiers that were written)

- **Banned evaluative language:** `grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over the 7 newly-written dossiers returns **no matches**. (Each sub-agent verified its own file before reporting back.)
- **Sibling-project / successor framing:** `grep -wEi 'critterbids|crittercab|successor'` over the 7 newly-written dossiers returns **no matches**.
- **Source-cited file paths for behavioral claims:** every behavioral claim in every written dossier source-cites a specific file (often with line range). Structural lists cite the folder.

## Cross-Reference Forward to S3

S2 work surfaced these non-obvious S3 dependencies:

- **Customer Experience ↔ Inventory** — Storefront subscribes to `Inventory.ReservationConfirmed` (not in CONTEXTS.md table). Verify whether Inventory's S3 dossier should mirror this edge.
- **Customer Experience ↔ Returns** — Storefront subscribes to 10 Returns events (not in CONTEXTS.md table). When Returns' deferred S2i dossier is written, surface this edge symmetrically.
- **Customer Experience ↔ Pricing** (forward-note from prompt) — confirm whether the cart price display path involves a Pricing query or relies entirely on the Shopping `UnitPrice` freeze.
- **Orders ↔ Promotions** (forward-note from prompt) — `RecordPromotionRedemption` integration. To be examined when Promotions is deepened in S3.

## Build State at Session Close

- Errors: 0 (delta from baseline: 0)
- Warnings: 359 (delta from baseline: 0; warning set unchanged)
- Files changed: 9 — 7 BC dossiers, this retrospective, `CURRENT-CYCLE.md` entry, `docs/extraction/README.md` status table. No code changes; no project file changes.

## Verification Checklist

- [x] 7 of 9 commerce-core dossiers exist at full S2 depth in place.
- [ ] All 9 commerce-core dossiers exist at full S2 depth — **partial** (Fulfillment + Returns deferred).
- [x] Customer Identity follows Variant B (Entities; "Not applicable" for Domain events / Projections; DbContext + migrations section present).
- [x] Customer Experience follows Variant C (Composition map per upstream BC; Read models; SignalR channels; Frontend surface).
- [x] No written dossier contains evaluative language (verified by grep).
- [x] No written dossier references CritterBids, CritterCab, or any successor project (verified by grep).
- [x] No written dossier frames itself as preparation for a downstream operation.
- [x] Every behavioral claim in every written dossier source-cites a specific file.
- [x] `docs/extraction/README.md` status table reflects 7-of-9 S2-full state.
- [x] This retrospective committed.
- [x] `CURRENT-CYCLE.md` updated.
- [x] Build baseline recorded at session open and close (identical: 0 errors, 359 warnings).
- [x] S2 event/command counts per BC reconciled against S1 stub counts; deviations explained per BC above.

## What Remains / Next Session Should Verify

- **Complete S2 by promoting Fulfillment and Returns dossiers** before starting S3. Fulfillment can lean on the source-enumeration notes above; Returns is greenfield from a stub.
- **S3 — Channels / vendor / admin deep dive (9 BCs).** Listings, Marketplaces, Vendor Identity, Vendor Portal, Pricing, Correspondence, Backoffice Identity, Backoffice, Promotions.
- **CONTEXTS.md drift items surfaced for S5 (structural observations) consideration:**
  - CONTEXTS.md Customer Experience integration table omits Inventory and Returns edges.
  - CONTEXTS.md line 158 description of `AssignProductToVendor` as the "sole remaining document-store write path" is stale.
  - `Fulfillment.Api/README.md` still references retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events in narrative diagrams.

The next session is **S2 continuation (Fulfillment + Returns) → then S3 — Channels / vendor / admin deep dive**.
