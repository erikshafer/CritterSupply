# Product Catalog

> **Source folder:** `src/Product Catalog/`
> **Status:** Implemented
> **Most recent material milestone:** M35.0 — Product Catalog event-sourcing migration (Sessions 5+6)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Product Catalog owns the master data for every SKU in the system — name, description, category, images, dimensions, status, tags, and vendor association. It is the upstream source for Pricing, Listings, Marketplaces, the storefront BFF, and Backoffice product-management views. The aggregate is event-sourced (`CatalogProduct`), with a single inline projection serving the read path and a residual document-store write path for vendor-product assignment held over from the pre-M35.0 model.

## Top-level structure

### Aggregates

- `CatalogProduct` — stream ID: UUID v7.
- `Product` (legacy Marten document) — retained for the vendor-assignment write path and migration bootstrap.

### Commands

Catalog write paths and the assignment command live in `src/Product Catalog/ProductCatalog/Products/` and the API project; at S1 stub depth the enumeration is deferred to the S2 dossier.

### Domain events

- `ProductMigrated`
- `ProductCreated`
- `ProductNameChanged`
- `ProductDescriptionChanged`
- `ProductCategoryChanged`
- `ProductImagesUpdated`
- `ProductDimensionsChanged`
- `ProductStatusChanged`
- `ProductTagsUpdated`
- `ProductSoftDeleted`
- `ProductRestored`
- `ProductVendorAssigned`

### Projections

- `CatalogProduct` snapshot — inline, keyed by stream id.
- `ProductCatalogView` — inline.

### Integration events

- `ProductCatalog.ProductAdded` — publishes
- `ProductCatalog.ProductUpdated` — publishes
- `ProductCatalog.ProductContentUpdated` — publishes
- `ProductCatalog.ProductCategoryChanged` — publishes
- `ProductCatalog.ProductImagesUpdated` — publishes
- `ProductCatalog.ProductDimensionsChanged` — publishes
- `ProductCatalog.ProductStatusChanged` — publishes
- `ProductCatalog.ProductDiscontinued` — publishes (carries `IsRecall` flag triggering Listings cascade)
- `ProductCatalog.ProductDeleted` — publishes
- `ProductCatalog.ProductRestored` — publishes
- `ProductCatalog.VendorProductAssociated` — publishes
- `ProductCatalog.AdditionalInfoRequested` — publishes
- `ProductCatalog.DataCorrectionApproved` / `DataCorrectionRejected` — publishes
- `ProductCatalog.DescriptionChangeApproved` / `DescriptionChangeRejected` — publishes
- `ProductCatalog.ImageChangeApproved` / `ImageChangeRejected` — publishes

### HTTP / API surface (one line)

`ProductCatalog.Api` exposes product CRUD-style commands and read endpoints consumed by Backoffice, the storefront BFF, Listings, Marketplaces, and Pricing.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (default + Backoffice schemes).

## Prior event modeling

- `docs/planning/catalog-listings-marketplaces-discovery.md`
- `docs/planning/catalog-listings-marketplaces-glossary.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-variant-model.md`

## ADRs

- ADR 0041 — Product Catalog ES Migration Decisions
- ADR 0042 — `catalog:` Namespace UUID v5 Convention

## Source citations (S1 stub)

- `src/Product Catalog/`
- `src/Shared/Messages.Contracts/ProductCatalog/`
- `CONTEXTS.md` (section: `Product Catalog`)
- `docs/decisions/0041-product-catalog-es-migration-decisions.md`
- `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
