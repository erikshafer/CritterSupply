# ADR 0041: Product Catalog ES Migration Decisions

**Status:** ✅ Accepted

**Date:** 2026-03-29

**Context:**

During M35.0, the Product Catalog bounded context was migrated from a document-store (CRUD) persistence pattern to event sourcing with Marten. This migration was one of the largest architectural changes in CritterSupply's history, affecting 14 handlers, the aggregate model, projections, and integration messaging.

Several architectural decisions were made during the M35.0 implementation that were not formally documented at the time. This ADR records those decisions retroactively so future sessions have a clear reference for why the current implementation looks the way it does.

**Decisions:**

### 1. Apply-Only Aggregate (No `Create()` Factory)

The `CatalogProduct` aggregate uses `Apply(ProductCreated)` and `Apply(ProductMigrated)` methods for initial state instead of a `Create()` factory method. This follows Marten's projection-style pattern where `Apply()` methods reconstitute state from events.

**Rationale:** Marten's projection system naturally handles stream creation via event-based apply methods. A `Create()` factory adds complexity without benefit when the aggregate is only rehydrated from its event stream.

### 2. Random UUID v4 Stream IDs (Not Deterministic UUID v5)

Stream IDs use `Guid.NewGuid()` instead of deterministic `catalog:{sku}` UUID v5 hashing. SKU-to-stream mapping is maintained via the `ProductCatalogView` inline projection.

**Rationale:** UUID v5 namespace hashing would enable idempotent stream creation and direct SKU-based lookups without projection queries. However, the current pattern works correctly and was simpler to implement during the initial migration. The projection-based lookup adds one query per handler but is consistent with how other BCs (Orders, Returns) map external identifiers to stream IDs.

**Future consideration:** UUID v5 migration may be revisited in a future milestone. See ADR 0042 for the namespace convention documentation.

### 3. Inline `ProductCatalogView` Projection

The read model `ProductCatalogView` is registered as an inline projection (`ProjectionLifecycle.Inline`), meaning it updates synchronously within the same transaction as event appends.

**Rationale:** Inline projections guarantee zero-lag reads, which is critical for handlers that query the projection before appending events (e.g., checking if a SKU already exists before creating a product). Async projections would introduce eventual consistency issues for these read-then-write patterns.

### 4. Handler Pattern: Manual `session.Events.Append()` + Projection Query

All mutation handlers follow the same pattern:
1. Query `ProductCatalogView` to find the product by SKU
2. Create domain event
3. Append event via `session.Events.Append(view.Id, @event)`
4. Return `(IResult, OutgoingMessages)` tuple

**Rationale:** This pattern was chosen over Wolverine's `[WriteAggregate]` compound handler because the handlers need to query by SKU (a business identifier) rather than by stream ID (a technical identifier). The projection-based lookup step makes `[WriteAggregate]` less natural.

### 5. 12 Domain Events (Not 14)

The plan called for 14 domain event records. M35.0 delivered 12, omitting `ProductBrandChanged` and a compliance-related event. These correspond to mutations that don't have dedicated handlers yet.

**Rationale:** Events were only created for mutations that have corresponding handlers. Adding events without handlers would be speculative. New events should be added when their handlers are implemented.

**Consequences:**

- The aggregate design is stable and all 48 integration tests pass
- Future migration to UUID v5 stream IDs would require a data migration step
- The projection-query-then-append pattern adds one database round-trip per handler invocation
- Granular integration messages (added in M36.1) follow the same handler pattern

**References:**
- `src/Product Catalog/ProductCatalog/Products/CatalogProduct.cs` — aggregate
- `src/Product Catalog/ProductCatalog/Products/ProductCatalogViewProjection.cs` — projection
- `src/Product Catalog/ProductCatalog/Products/ProductEvents.cs` — domain events
- `docs/planning/milestones/m36-1-phase-0-reconciliation.md` — full reconciliation audit
