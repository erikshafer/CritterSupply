# Product Catalog

> **Source folder:** `src/Product Catalog/`
> **Status:** Implemented
> **Most recent material milestone:** M35.0 — Product Catalog event-sourcing migration (Sessions 5+6, closure)
> **Dossier depth:** S2 — full

## Purpose

Product Catalog owns the master data for every SKU in the system — name, description, category, images, dimensions, status, tags, and vendor association. It is the upstream source for Pricing, Listings, Marketplaces, the storefront BFF, and Backoffice product-management views. The aggregate is event-sourced (`CatalogProduct`), with a single inline projection (`ProductCatalogView`) serving the read path. A pre-M35.0 Marten document model (`Product`) is retained in the BC for development seed bootstrap and for the one-shot migration handler that lifts existing documents into event streams.

## Aggregates

### `CatalogProduct` (event-sourced)

- **Stream ID:** UUID v7, generated at first write (`Guid.CreateVersion7()` in `CreateProduct` and `MigrateProduct`). The SKU→stream-id mapping is materialised in the `ProductCatalogView` projection rather than derived from the SKU; ADR 0042 records a proposed UUID v5 convention that is not yet in code.
- **Key state:** `Sku`, `Name`, `Description`, `Category`, `Status` (`Draft` | `Active` | `Inactive` | `Discontinued`), `IsDeleted`, `VendorTenantId`, `Images`, `Tags`, `Dimensions`, `AddedAt`, `UpdatedAt`.
- **Lifecycle stages:**
  - **Created** — via `ProductCreated` (new SKU) or `ProductMigrated` (lift from `Product` document). Status begins `Active`; `IsDeleted = false`.
  - **Active edits** — content, category, images, dimensions, tags, vendor assignment all mutate via dedicated events while the aggregate stays in a non-terminal state.
  - **Discontinued** — `ProductStatusChanged` to `Discontinued` flips `IsTerminal` true and unlocks the recall cascade integration path.
  - **Soft-deleted / Restored** — `ProductSoftDeleted` sets `IsDeleted = true`; `ProductRestored` clears it. Soft-deleted aggregates are excluded from non-admin read queries (e.g. `ListProducts` `where !p.IsDeleted` at `src/Product Catalog/ProductCatalog.Api/Products/ListProducts.cs#L19`).
- **File:** `src/Product Catalog/ProductCatalog/Products/CatalogProduct.cs`.

### `Product` (legacy Marten document — residual)

- **Persistence:** Marten document store, schema `productcatalog`, registered with indexes on `Sku`, `Category`, `Status` and Marten `SoftDeleted()` support at `src/Product Catalog/ProductCatalog.Api/Program.cs#L29-L33`.
- **Identity:** the SKU string is used as the Marten document `Id` (`src/Product Catalog/ProductCatalog/Products/Product.cs#L12`). Distinct from the `CatalogProduct` stream-id (UUID v7).
- **Active writers in the running BC:** none under HTTP. The only code path that calls `session.Store(product)` is the development seed at `src/Product Catalog/ProductCatalog/Products/SeedData.cs#L446`, invoked from `Program.cs` only when `app.Environment.IsDevelopment() && !IsRunningInTests()` (`src/Product Catalog/ProductCatalog.Api/Program.cs#L179`). The seed writes Product documents and immediately calls `MigrateProductsAsync` (`SeedData.cs#L455`) to start a `CatalogProduct` stream from each one. The previously-cited `AssignProductToVendor` document-store write was retired in the M35.0 closure session; the current handler appends `ProductVendorAssigned` to the `CatalogProduct` stream instead (`src/Product Catalog/ProductCatalog.Api/Products/AssignProductToVendor.cs#L86-L94`).
- **Active readers:** one — `MigrateProductHandler.Handle` loads the Product document at `src/Product Catalog/ProductCatalog.Api/Products/MigrateProduct.cs#L21` to construct a `ProductMigrated` event and start a `CatalogProduct` stream. No HTTP query path reads the `Product` document.
- **Behaviour methods on the document** (`Update`, `ChangeStatus`, `SoftDelete`, `AssignToVendor` at `Product.cs#L68-L125`) are no longer invoked by any handler in the BC; they remain compiled into the assembly because seed data, the migration handler, and unit tests in `tests/Product Catalog/ProductCatalog.UnitTests/Products/ProductTests.cs` reference the type.
- **Reserved purpose:** seed-and-migrate bootstrap. Reading the document is the bridge that lets the BC start with a populated catalog in dev environments without re-deriving sample data inside event streams.

> **Note on CONTEXTS.md drift:** the current `CONTEXTS.md` Product Catalog entry (line 158) still names `AssignProductToVendor` as "the sole remaining document-store write path." That description predates the M35.0 closure session; in code today the handler is event-sourced (see citation above). The dossier reflects the code.

## Commands

All commands write to the `CatalogProduct` event stream. No HTTP-exposed command in the BC writes to the `Product` document store.

### `CatalogProduct` — content & lifecycle

- `CreateProduct` — start a new `CatalogProduct` stream with a `ProductCreated` event after a duplicate-SKU check against the projection. Handler: `src/Product Catalog/ProductCatalog.Api/Products/CreateProduct.cs` (`CreateProductHandler.Handle`, lines 41–100).
- `MigrateProduct` — load an existing `Product` document and append `ProductMigrated` to start a stream; idempotent against the projection. Handler: `src/Product Catalog/ProductCatalog.Api/Products/MigrateProduct.cs` (lines 11–58).
- `ChangeProductName` — append `ProductNameChanged` and emit `ProductContentUpdated`. Handler: `src/Product Catalog/ProductCatalog.Api/Products/ChangeProductName.cs` (lines 25–59).
- `ChangeProductDescription` — append `ProductDescriptionChanged` and emit `ProductContentUpdated`. Handler: `src/Product Catalog/ProductCatalog.Api/Products/ChangeProductDescription.cs` (lines 23–57).
- `ChangeProductCategory` — append `ProductCategoryChanged` and emit the integration event of the same name. Handler: `src/Product Catalog/ProductCatalog.Api/Products/ChangeProductCategory.cs` (lines 23–57).
- `ChangeProductDimensions` — append `ProductDimensionsChanged` and emit the integration event of the same name. Handler: `src/Product Catalog/ProductCatalog.Api/Products/ChangeProductDimensions.cs` (lines 27–69).
- `ChangeProductStatusCommand` — append `ProductStatusChanged`; if the target is `Discontinued`, additionally emit `ProductDiscontinued` (carrying `IsRecall`). Handler: `src/Product Catalog/ProductCatalog.Api/Products/ChangeProductStatus.cs` (lines 24–71).
- `UpdateProductImages` — append `ProductImagesUpdated` and emit the integration event of the same name. Handler: `src/Product Catalog/ProductCatalog.Api/Products/UpdateProductImages.cs` (lines 29–67).
- `UpdateProductTags` — append `ProductTagsUpdated`. Does not emit an integration event. Handler: `src/Product Catalog/ProductCatalog.Api/Products/UpdateProductTags.cs` (lines 22–48).
- `SoftDeleteProduct` (route-bound, no body type) — append `ProductSoftDeleted` and emit `ProductDeleted`. Handler: `src/Product Catalog/ProductCatalog.Api/Products/SoftDeleteProduct.cs` (lines 11–41).
- `RestoreProduct` (route-bound, no body type) — append `ProductRestored` and emit the integration event of the same name. Handler: `src/Product Catalog/ProductCatalog.Api/Products/RestoreProduct.cs` (lines 11–41).

### `CatalogProduct` — vendor assignment

- `AssignProductToVendor` — append `ProductVendorAssigned` for a single SKU and emit `VendorProductAssociated`; idempotent on identical (SKU, VendorTenantId), 404 on unknown SKU, 400 on discontinued/deleted. Handler: `src/Product Catalog/ProductCatalog.Api/Products/AssignProductToVendor.cs` (`AssignProductToVendorHandler.Handle`, lines 32–113).
- `BulkAssignProductsToVendor` — append `ProductVendorAssigned` for each item in a list (≤100); emit one `VendorProductAssociated` per success; per-item failures are returned in a `BulkAssignmentResult` with HTTP 207 on partial success. Handler: `src/Product Catalog/ProductCatalog.Api/Products/BulkAssignProductsToVendor.cs` (`BulkAssignProductsToVendorHandler.Handle`, lines 69–157).

**Definitive command count: 13 commands.** All 13 write to the `CatalogProduct` event stream.

## Domain events

12 events defined on the `CatalogProduct` stream — matching the count recorded in the S1 stub. All defined in `src/Product Catalog/ProductCatalog/Products/ProductEvents.cs` and applied in `CatalogProduct.cs`.

### Stream initialisation

- `ProductMigrated` — captures the full Product-document state at lift-into-event-sourcing time. `ProductEvents.cs#L7-L25`.
- `ProductCreated` — a brand-new SKU was created directly in the event store. `ProductEvents.cs#L30-L42`.

### Content changes

- `ProductNameChanged` — display name was updated. `ProductEvents.cs#L44-L48`.
- `ProductDescriptionChanged` — short description was updated. `ProductEvents.cs#L50-L54`.
- `ProductCategoryChanged` — taxonomy assignment was changed. `ProductEvents.cs#L56-L60`.
- `ProductImagesUpdated` — the image list was replaced. `ProductEvents.cs#L62-L66`.
- `ProductDimensionsChanged` — physical dimensions / weight were changed. `ProductEvents.cs#L68-L72`.
- `ProductTagsUpdated` — the tag list was replaced. `ProductEvents.cs#L81-L85`.

### Lifecycle

- `ProductStatusChanged` — status moved between `Draft`/`Active`/`Inactive`/`Discontinued`; carries `Reason`. `ProductEvents.cs#L74-L79`.
- `ProductSoftDeleted` — the SKU was hidden from the catalog. `ProductEvents.cs#L87-L89`.
- `ProductRestored` — a previously soft-deleted SKU was reinstated. `ProductEvents.cs#L91-L93`.

### Vendor assignment

- `ProductVendorAssigned` — a vendor tenant became (or was reassigned as) the owner of the SKU; carries `PreviousVendorTenantId` and an optional `ReassignmentNote`. `ProductEvents.cs#L95-L101`.

## Projections

- `CatalogProduct` snapshot — inline `SnapshotLifecycle.Inline`; keyed by stream id (UUID v7); source events: all 12 above. Registered at `src/Product Catalog/ProductCatalog.Api/Program.cs#L36`. Serves write-side rehydration; not queried directly by HTTP.
- `ProductCatalogView` — inline single-stream projection (`SingleStreamProjection<ProductCatalogView, Guid>`); keyed by stream id, with `Sku` populated for all SKU-based queries; source events: all 12 above. File: `src/Product Catalog/ProductCatalog/Products/ProductCatalogViewProjection.cs`. Serves every HTTP read endpoint (`GetProduct`, `ListProducts`, `GetVendorAssignment`) and every command's pre-write SKU lookup, duplicate check, and current-state snapshot. Registered at `src/Product Catalog/ProductCatalog.Api/Program.cs#L39`.

## Integration events

All routed via Wolverine + RabbitMQ. Exchange bindings declared in `src/Product Catalog/ProductCatalog.Api/Program.cs#L130-L162`. Contract files live in `src/Shared/Messages.Contracts/ProductCatalog/`.

### Published by Product Catalog

- `ProductAdded` — payload: `Sku: string, Name: string, Category: string, AddedAt: DateTimeOffset, Status: string?, Brand: string?, HasDimensions: bool?`. Publisher → subscribers: `Product Catalog → Inventory, Pricing, Listings`. File: `…/ProductCatalog/ProductAdded.cs`.
- `ProductDiscontinued` — payload: `Sku: string, DiscontinuedAt: DateTimeOffset, Reason: string?, IsRecall: bool`. Publisher → subscribers: `Product Catalog → Listings, Orders`. Routed to two exchanges: `product-catalog-product-discontinued` and the fanout `product-recall` exchange. File: `…/ProductCatalog/ProductDiscontinued.cs`.
- `ProductContentUpdated` — payload: `Sku: string, Name: string, Description: string, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductContentUpdated.cs`.
- `ProductCategoryChanged` (integration) — payload: `Sku: string, PreviousCategory: string, NewCategory: string, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings, Marketplaces`. File: `…/ProductCatalog/ProductCategoryChanged.cs`.
- `ProductImagesUpdated` (integration) — payload: `Sku: string, ImageUrls: IReadOnlyList<string>, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductImagesUpdated.cs`.
- `ProductDimensionsChanged` (integration) — payload: `Sku: string, Weight: decimal, Length: decimal, Width: decimal, Height: decimal, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductDimensionsChanged.cs`.
- `ProductStatusChanged` (integration) — payload: `Sku: string, PreviousStatus: string, NewStatus: string, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductStatusChanged.cs`.
- `ProductDeleted` — payload: `Sku: string, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductDeleted.cs`.
- `ProductRestored` (integration) — payload: `Sku: string, OccurredAt: DateTimeOffset`. Publisher → subscribers: `Product Catalog → Listings`. File: `…/ProductCatalog/ProductRestored.cs`.
- `VendorProductAssociated` — payload: `Sku: string, VendorTenantId: Guid, AssociatedBy: string, AssociatedAt: DateTimeOffset, PreviousVendorTenantId: Guid?, ReassignmentNote: string?`. Publisher → subscribers: `Product Catalog → Vendor Portal`. File: `…/ProductCatalog/VendorProductAssociated.cs`.
- `ProductUpdated` — payload: `Sku: string, Name: string, Category: string, UpdatedAt: DateTimeOffset`. Marked `[Obsolete]` in the contract; superseded by the granular content/category/images/dimensions/status events above. File: `…/ProductCatalog/ProductUpdated.cs`. No publish binding in `Program.cs`; no handler emits it.

### Defined under the `Messages.Contracts.ProductCatalog` namespace but not published by this BC

These contracts live alongside the catalog's outbound messages because they describe responses to vendor change requests. They are produced by the Vendor Portal change-request handlers (`src/Vendor Portal/VendorPortal/ChangeRequests/SubmitChangeRequest.cs#L120`, `…/ProvideAdditionalInfo.cs#L97`) and consumed by Vendor Portal's own handlers (`AdditionalInfoRequestedHandler`, `DescriptionChangeApprovedHandler`, `DataCorrectionApprovedHandler`, `ImageChangeApprovedHandler`, and the corresponding rejection handlers). Product Catalog has no `PublishMessage<>` binding for any of them in `Program.cs`.

- `AdditionalInfoRequested` — payload: `RequestId, Sku, VendorTenantId, Question, RequestedAt`. File: `…/ProductCatalog/AdditionalInfoRequested.cs`.
- `DataCorrectionApproved` / `DataCorrectionRejected` — payloads `RequestId, Sku, VendorTenantId, ApprovedAt|RejectedAt(+Reason)`. Files: `…/ProductCatalog/DataCorrectionApproved.cs`, `…/DataCorrectionRejected.cs`.
- `DescriptionChangeApproved` / `DescriptionChangeRejected` — same shape. Files: `…/ProductCatalog/DescriptionChangeApproved.cs`, `…/DescriptionChangeRejected.cs`.
- `ImageChangeApproved` / `ImageChangeRejected` — same shape. Files: `…/ProductCatalog/ImageChangeApproved.cs`, `…/ImageChangeRejected.cs`.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. Choreography only: it publishes lifecycle integration events that downstream BCs (notably Listings' recall cascade reacting to `ProductDiscontinued` with `IsRecall=true`) react to without round-tripping back to Product Catalog.

## HTTP / API surface

All endpoints declared in `src/Product Catalog/ProductCatalog.Api/Products/` via Wolverine HTTP attributes. Application URL: `http://localhost:5133` (`Properties/launchSettings.json`). Swagger mounted at `/api` in development.

### Catalog content & lifecycle

- `POST /api/products` — create a new SKU. Handler: `CreateProduct.cs`. Auth: `[Authorize(Policy = "VendorAdmin")]`. Consumers: Vendor Portal admin UI, Backoffice product-management.
- `POST /api/products/{sku}/migrate` — lift the existing `Product` document to an event stream. Handler: `MigrateProduct.cs`. Auth: `[Authorize]` (any authenticated principal). Consumers: one-shot operational tooling; not invoked at runtime by other BCs.
- `PUT /api/products/{sku}/name` — change display name. Handler: `ChangeProductName.cs`. Auth: `[Authorize]`.
- `PUT /api/products/{sku}/description` — change short description. Handler: `ChangeProductDescription.cs`. Auth: `[Authorize]`.
- `PUT /api/products/{sku}/category` — change category assignment. Handler: `ChangeProductCategory.cs`. Auth: `[Authorize]`.
- `PUT /api/products/{sku}/dimensions` — change physical dimensions. Handler: `ChangeProductDimensions.cs`. Auth: `[Authorize]`.
- `PUT /api/products/{sku}/images` — replace image list. Handler: `UpdateProductImages.cs`. Auth: `[Authorize]`.
- `PUT /api/products/{sku}/tags` — replace tag list. Handler: `UpdateProductTags.cs`. Auth: `[Authorize]`.
- `PATCH /api/products/{sku}/status` — change lifecycle status (carries optional `IsRecall` for the Discontinued transition). Handler: `ChangeProductStatus.cs`. Auth: `[Authorize]`.
- `DELETE /api/products/{sku}` — soft-delete. Handler: `SoftDeleteProduct.cs`. Auth: `[Authorize(Policy = "ProductManager")]` (Backoffice scheme).
- `POST /api/products/{sku}/restore` — undo soft-delete. Handler: `RestoreProduct.cs`. Auth: `[Authorize(Policy = "ProductManager")]` (Backoffice scheme).

### Catalog reads

- `GET /api/products/{sku}` — single product view. Handler: `GetProduct.cs`. Auth: anonymous. Consumers per `CONTEXTS.md`: Customer Experience storefront, Backoffice, Pricing, Listings.
- `GET /api/products` — paged list with optional `category` and `status` filters. Handler: `ListProducts.cs`. Auth: anonymous. Consumers: Backoffice product list, Customer Experience storefront browse.

### Vendor assignment

- `GET /api/admin/products/{sku}/vendor-assignment` — read current assignment. Handler: `GetVendorAssignment.cs`. Auth: `[Authorize(Policy = "VendorAdmin")]`. Consumers: Vendor Portal admin UI.
- `POST /api/admin/products/{sku}/vendor-assignment` — assign or reassign one SKU. Handler: `AssignProductToVendor.cs`. Auth: `[Authorize(Policy = "VendorAdmin")]`. Consumers: Vendor Portal admin UI.
- `POST /api/admin/products/vendor-assignments/bulk` — assign up to 100 SKUs in one call (HTTP 200 on full success, 207 on partial). Handler: `BulkAssignProductsToVendor.cs`. Auth: `[Authorize(Policy = "VendorAdmin")]`. Consumers: Vendor Portal bulk-assignment tooling.

## Frontend surface

Not applicable — no frontend in this BC. Front-end consumption happens in Backoffice (Blazor) and Vendor Portal (Blazor WASM), both of which call the endpoints listed above.

## Identity / auth posture

- **Schemes:** two JWT bearer schemes registered at `src/Product Catalog/ProductCatalog.Api/Program.cs#L50-L83`.
  - Default scheme — symmetric-key HS256, validates issuer `Jwt:Issuer` (default `vendor-identity`) and audience `Jwt:Audience` (default `vendor-portal`). Used for the Vendor Portal admin endpoints.
  - `Backoffice` scheme — OIDC against `https://localhost:5249`, role claim type `role`. Used for Backoffice catalog-management policies.
- **Policies** (`Program.cs#L86-L106`):
  - `VendorAdmin` — default scheme, requires authenticated user with role `Admin`. Gates `CreateProduct`, vendor-assignment endpoints.
  - `CopyWriter` — Backoffice scheme, accepts roles `CopyWriter`, `ProductManager`, `SystemAdmin`. Defined but not currently attached to any handler in the BC.
  - `ProductManager` — Backoffice scheme, accepts roles `ProductManager`, `SystemAdmin`. Gates soft-delete and restore.
- **Roles in use:** `Admin` (vendor scheme), `ProductManager` and `SystemAdmin` (Backoffice scheme).
- **Bare `[Authorize]`:** the content-mutation endpoints (name/description/category/dimensions/images/tags/status) accept any authenticated principal regardless of policy.
- **Source:** `src/Product Catalog/ProductCatalog.Api/Program.cs`.

## Tests as behavioral evidence

### Gherkin features — `docs/features/product-catalog/`

- `add-product.feature` — Feature: *Add Product to Catalog*. 4 scenarios: add a valid product; add product with images; cannot add product with duplicate SKU; cannot add product with invalid SKU format.
- `catalog-event-sourcing-migration.feature` — Feature: *Product Catalog Event Sourcing Migration*. 11 scenarios covering: migrate existing product document to event stream; migration is idempotent; create new product via event sourcing; cannot create product with duplicate SKU; change product name emits granular event; change product description emits granular event; change product category emits granular event; discontinue a product; reactivate a discontinued product; soft delete a product; restore a soft-deleted product; multiple changes produce correct final state.

No `@pending` or `@wip` tags are present in either feature file (`grep -E '@(pending|wip)' docs/features/product-catalog/*.feature` returns no matches).

### Integration tests — `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/`

Total Alba-driven integration tests: **58**.

- `AddProductTests.cs` — 5 tests: covers `POST /api/products` happy path, validation, and duplicate-SKU 409.
- `AssignProductToVendorTests.cs` — 17 tests: covers single and bulk vendor-assignment endpoints, idempotency, reassignment with `PreviousVendorTenantId`, 404 on unknown SKU, 400 on discontinued/deleted, 207 partial-success path.
- `ChangeProductStatusTests.cs` — 4 tests: covers `PATCH /api/products/{sku}/status`, including the Discontinued + `IsRecall=true` integration-event path.
- `EventSourcingBehaviorTests.cs` — 10 tests: validates that `CatalogProduct` rehydration, projection updates, and event-stream invariants hold across the migrated handlers.
- `GetProductTests.cs` — 4 tests: read endpoint, including soft-deleted exclusion.
- `ListProductsTests.cs` — 6 tests: paging, category filter, status filter, soft-delete exclusion.
- `UpdateProductTests.cs` — 12 tests: covers name/description/category/dimensions/images/tags handlers and their integration-event emissions.
- `TestFixture.cs`, `IntegrationTestCollection.cs`, `SeedData.cs`, `Usings.cs` — test infrastructure (Testcontainers Postgres + RabbitMQ via the standard CritterSupply pattern).

### Unit tests — `tests/Product Catalog/ProductCatalog.UnitTests/Products/`

Total xUnit unit tests: **83**, covering value objects and the legacy `Product` document.

- `SkuTests.cs` — 17 tests on the `Sku` value object.
- `ProductNameTests.cs` — 17 tests on the `ProductName` value object.
- `CategoryNameTests.cs` — 14 tests on the `CategoryName` value object.
- `ProductDimensionsTests.cs` — 11 tests on the `ProductDimensions` value object.
- `ProductTests.cs` — 24 tests on the legacy `Product` document's behaviour methods (`Create`, `Update`, `ChangeStatus`, `SoftDelete`, `AssignToVendor`). These cover compiled behaviour that is no longer exercised by HTTP handlers; they remain green because seed and migration paths still construct `Product` instances.

## ADRs

- **ADR 0041 — Product Catalog ES Migration Decisions.** Records the decisions taken during M35.0 to migrate Product Catalog from the document store to event sourcing — choice of inline projection, event granularity, the migration handler bootstrap path, and the interim retention of the `Product` document. File: `docs/decisions/0041-product-catalog-es-migration-decisions.md`.
- **ADR 0042 — `catalog:` Namespace UUID v5 Convention.** Documents a proposed deterministic stream-id convention (`Guid.CreateVersion5(NamespaceCatalog, sku)`) to replace the current UUID v7 stream id, with the implementation deferred. The current code uses UUID v7; the SKU→stream-id mapping lives in the projection. File: `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`.

ADRs 0048 (Marketplace document entity design), 0049 (Category mapping ownership), and 0050 (Marketplaces ProductSummaryView ACL) trace back to Product Catalog's event-sourcing migration but materially shape the Listings and Marketplaces dossiers; they are referenced in the S3 cohort, not here. ADR 0049's only Product Catalog-facing decision is the negative one — category mappings are not owned here.

## Prior event modeling

Files in `docs/planning/` informing this BC:

- `docs/planning/catalog-listings-marketplaces-discovery.md` — joint discovery session covering SKU master data, listing lifecycle, and channel publication. The Product Catalog cuts identify the master-data invariants that the ES migration later locked in.
- `docs/planning/catalog-listings-marketplaces-glossary.md` — ubiquitous-language glossary; defines `CatalogProduct`, `Sku`, `VendorAssignment`, `Discontinuation`, `Recall` and their boundaries.
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md` — planned evolution roadmap; the variants/listings work it scopes is unlocked by the Product Catalog ES migration but lives outside this BC.
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md` — milestone-cycle plan that scheduled the M35.0 catalog migration and the dependent Listings / Marketplaces work.
- `docs/planning/catalog-variant-model.md` — variant-product modelling notes. Catalog-side prerequisite identified: a stable per-SKU stream identity; documented but not yet realised in code.

## Source citations (S2 full)

- `src/Product Catalog/` (folder root) — `ProductCatalog/Products/` (aggregate, events, projection, value objects, seed), `ProductCatalog.Api/Products/` (command handlers, query handlers, DTOs), `ProductCatalog.Api/Program.cs` (Marten/Wolverine wiring, JWT schemes, policies, RabbitMQ exchange bindings).
- `src/Shared/Messages.Contracts/ProductCatalog/` — every contract listed in the Integration events section.
- `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/` — all integration test files cited above.
- `tests/Product Catalog/ProductCatalog.UnitTests/Products/` — all unit test files cited above.
- `docs/features/product-catalog/add-product.feature`
- `docs/features/product-catalog/catalog-event-sourcing-migration.feature`
- `CONTEXTS.md` (section: `Product Catalog`)
- `docs/decisions/0041-product-catalog-es-migration-decisions.md`
- `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`
- `docs/planning/catalog-listings-marketplaces-discovery.md`
- `docs/planning/catalog-listings-marketplaces-glossary.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
- `docs/planning/catalog-variant-model.md`
- `docs/planning/milestones/m48-0-session-1-retrospective.md` (Product Catalog row, S1 → S2 hand-off)
