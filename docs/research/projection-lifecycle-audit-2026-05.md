# Projection Lifecycle Audit — 2026-05

> **Source:** M44.0 Tier 2 F deliverable
> **Method:** `grep -rn "Projections\.\(Snapshot\|Add\)" src/` then per-row classification against the inline-vs-async heuristics in the plan.
> **Scope:** All Marten projections registered in `*.Api/Program.cs` across every bounded context.

## Headline

- **30 projections** registered across **13 bounded contexts**.
- **27 inline** (Snapshot or `Add(... Inline)`).
- **3 async** — all in Inventory, all from the M42.3 (Inventory remaster S3) cross-warehouse view set.
- **0 conversions needed** — the codebase already follows "inline by default."
- The 3 async projections all have explicit operational reasons to remain async; they are *not* candidates for inline conversion.

## Classification key

- **Inline-safe** — read by HTTP queries with no per-event throughput requirement; fits within the producing event's transaction without measurable latency penalty. Inline gives strict consistency for free and eliminates the daemon-highwater-mark class of test bugs entirely.
- **Operationally-required-async** — produces side effects, fans out to many events per stream, has multi-second projection cost, or is an aggregate-of-aggregates where inline would multiply commit latency by the fan-out factor.
- **Should-not-be-a-projection** — the data could be served from a query / live aggregation instead. (None found in this audit.)

## Audit table

| BC | Projection | Type | Lifecycle | Classification | Action |
|----|------------|------|-----------|----------------|--------|
| Orders | `Checkout` | `Snapshot<Checkout>` | Inline | Inline-safe | None |
| Inventory | `ProductInventory` | `Snapshot<ProductInventory>` | Inline | Inline-safe | None |
| Inventory | `InventoryTransfer` | `Snapshot<InventoryTransfer>` | Inline | Inline-safe | None |
| Inventory | `StockAvailabilityViewProjection` | `MultiStreamProjection<StockAvailabilityView, string>` | Inline | Inline-safe | None — feeds Fulfillment routing engine; strict consistency required (per M42.0 retrospective) |
| Inventory | `WarehouseSkuDetailViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Inventory | `FulfillmentCenterCapacityViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Inventory | `AlertFeedViewProjection` | `MultiStreamProjection` | **Async** | Operationally-required-async | **Keep async** — fans out across many warehouses per event; alert evaluation is non-trivial; downstream consumer (Backoffice alert feed) tolerates eventual consistency |
| Inventory | `NetworkInventorySummaryViewProjection` | `MultiStreamProjection` | **Async** | Operationally-required-async | **Keep async** — network-wide rollup; inline would multiply commit latency by warehouse count |
| Inventory | `BackorderImpactViewProjection` | `MultiStreamProjection` | **Async** | Operationally-required-async | **Keep async** — joins BackorderRegistered with stream-of-streams data; non-trivial work per event |
| Pricing | `ProductPrice` | `Snapshot<ProductPrice>` | Inline | Inline-safe | None |
| Pricing | `CurrentPriceViewProjection` | `EventProjection` | Inline | Inline-safe | None |
| Returns | `Return` | `Snapshot<Return>` | Inline | Inline-safe | None |
| Backoffice | `OrderNote` | `Snapshot<OrderNote>` | Inline | Inline-safe | None |
| Backoffice | `AdminDailyMetricsProjection` | `EventProjection` | Inline | Inline-safe | None |
| Backoffice | `AlertFeedViewProjection` (Backoffice copy) | `MultiStreamProjection` | Inline | Inline-safe | None — different from Inventory's AlertFeedView; this one is per-BC and small |
| Backoffice | `ReturnMetricsViewProjection` | `EventProjection` | Inline | Inline-safe | None |
| Backoffice | `CorrespondenceMetricsViewProjection` | `EventProjection` | Inline | Inline-safe | None |
| Backoffice | `FulfillmentPipelineViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Fulfillment | `Shipment` | `Snapshot<Shipment>` | Inline | Inline-safe | None |
| Fulfillment | `WorkOrder` | `Snapshot<WorkOrder>` | Inline | Inline-safe | None |
| Fulfillment | `ShipmentStatusViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Fulfillment | `CarrierPerformanceViewProjection` | `EventProjection` | Inline | Inline-safe | None |
| Fulfillment | `MultiShipmentViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Payments | `Payment` | `Snapshot<Payment>` | Inline | Inline-safe | None |
| Listings | `Listing` | `Snapshot<Listing>` | Inline | Inline-safe | None |
| Listings | `ListingsActiveViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Product Catalog | `CatalogProduct` | `Snapshot<CatalogProduct>` | Inline | Inline-safe | None |
| Product Catalog | `ProductCatalogViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Correspondence | `Message` | `Snapshot<Message>` | Inline | Inline-safe | None |
| Correspondence | `MessageListViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |
| Shopping | `Cart` | `Snapshot<Cart>` | Inline | Inline-safe | None |
| Promotions | `Promotion` | `Snapshot<Promotion>` | Inline | Inline-safe | None |
| Promotions | `Coupon` | `Snapshot<Coupon>` | Inline | Inline-safe | None |
| Promotions | `CouponLookupViewProjection` | `MultiStreamProjection` | Inline | Inline-safe | None |

> *Aggregate sagas (e.g., `Order` in Orders, conceptually a saga rather than a projection) are not listed — they're persisted via Wolverine's saga storage path, not the projection registry.*

## Why the 3 remaining async projections must stay async

All three sit under `src/Inventory/Inventory/Management/`. Each was deliberately registered async in the M42.3 (Inventory remaster S3) work; the rationale carries forward unchanged.

### `AlertFeedViewProjection` (async)

- **Fan-out:** Every `LowStockTriggered` / `BackorderRegistered` / `StockDiscrepancyFound` event fans out across multiple warehouses for the same SKU.
- **Consumer SLA:** The Backoffice alert feed is read by humans, on-screen polling at 5–10s cadence. Sub-second freshness is not required.
- **Risk if converted to inline:** Every event commit on a hot SKU would block on the alert evaluation, doubling write latency for `Stock*` events on the critical reservation path.

### `NetworkInventorySummaryViewProjection` (async)

- **Fan-out:** Aggregates totals across the entire warehouse network per SKU per event.
- **Consumer SLA:** Used for executive dashboards and capacity planning — daily/hourly freshness is the actual business requirement.
- **Risk if converted to inline:** Inline summary updates would serialise stock-event commits across the entire network, which is exactly the production hotspot we want to avoid.

### `BackorderImpactViewProjection` (async)

- **Fan-out:** Joins `BackorderRegistered` events with downstream `BackorderStockAvailable` and `BackorderCleared` events across many streams.
- **Consumer SLA:** Procurement reads this on a daily replenishment cycle.
- **Risk if converted to inline:** Multi-stream joins on the inline path are exactly what Marten's docs warn against.

## Codified convention (going forward)

Captured in skill files (`docs/skills/marten-event-sourcing.md`, `docs/skills/event-sourcing-projections.md`):

> **Inline by default for any projection that will be read in integration tests with shared TestContainers fixtures.** Pick async only when:
>
> 1. The projection fans out across many streams per event, OR
> 2. The projection's per-event work is measured in hundreds of milliseconds, OR
> 3. The downstream consumer's freshness SLA is measured in seconds-to-minutes (not real-time).
>
> If async is selected, the projection's registration site **must** include a one-line comment explaining which of the three reasons applies, and the BC's integration tests **must** use `WaitForNonStaleProjectionDataAsync` rather than `DeleteAllDocumentsAsync`-then-immediate-read patterns.

## Re-running this audit

```bash
grep -rn 'Projections\.\(Add\|Snapshot\)' src/ | grep -v '\.Api\.' | grep -v 'docs/'
# Then for each Projections.Add<X>(...) row, check the lifecycle argument.
# Async hits are: ProjectionLifecycle.Async (or .UseAsyncDaemon equivalent).
```

When this audit is re-run, expect the row count to grow as new BCs ship. Any new row should justify its lifecycle choice in commit message or PR description; any row that picks `Async` without invoking one of the three rules above should be challenged in code review.
