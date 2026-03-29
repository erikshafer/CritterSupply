# M36.1 Session 1 Retrospective

**Date:** 2026-03-29
**Scope:** Phase 0 cleanup + Listings BC scaffold
**Build state:** 0 errors, 33 warnings (matches M36.0 baseline)

---

## Phase 0 Tasks Completed

### Part 1A — Granular Integration Message Contracts

7 new integration message contracts created in `src/Shared/Messages.Contracts/ProductCatalog/`:

| File | Description |
|------|-------------|
| `ProductContentUpdated.cs` | Carries SKU, Name, Description, OccurredAt |
| `ProductCategoryChanged.cs` | Carries SKU, PreviousCategory, NewCategory, OccurredAt |
| `ProductImagesUpdated.cs` | Carries SKU, ImageUrls (IReadOnlyList), OccurredAt |
| `ProductDimensionsChanged.cs` | Carries SKU, Weight, Length, Width, Height, OccurredAt |
| `ProductStatusChanged.cs` | Carries SKU, PreviousStatus, NewStatus, OccurredAt |
| `ProductDeleted.cs` | Carries SKU, OccurredAt |
| `ProductRestored.cs` | Carries SKU, OccurredAt |

Enriched contracts:
- `ProductAdded.cs` — added nullable `Status`, `Brand`, `HasDimensions` fields (backward compatible)
- `ProductDiscontinued.cs` — added `Reason` (nullable) and `IsRecall` (default false) fields

Deprecated:
- `ProductUpdated.cs` — marked `[Obsolete("Use granular product events instead...")]`

**Naming collision resolved:** `Messages.Contracts.ProductCatalog.ProductStatusChanged` (and 4 others) collide with domain events in `ProductCatalog.Products`. Resolved with:
- Import aliases in handler files (e.g., `using IntegrationProductStatusChanged = Messages.Contracts.ProductCatalog.ProductStatusChanged`)
- Fully qualified names in Program.cs routing configuration
- Removed unused `using Messages.Contracts.ProductCatalog` from AssignProductToVendorTests.cs

### Part 1B — Handler Updates

All 8 mutation handlers + CreateProductES updated to publish granular integration messages:

| Handler | Integration Message Published |
|---------|------------------------------|
| `ChangeProductNameES.cs` | `ProductContentUpdated` |
| `ChangeProductDescriptionES.cs` | `ProductContentUpdated` |
| `ChangeProductCategoryES.cs` | `ProductCategoryChanged` |
| `UpdateProductImagesES.cs` | `ProductImagesUpdated` |
| `ChangeProductDimensionsES.cs` | `ProductDimensionsChanged` |
| `ChangeProductStatusES.cs` | `ProductStatusChanged` + conditional `ProductDiscontinued` |
| `SoftDeleteProductES.cs` | `ProductDeleted` |
| `RestoreProductES.cs` | `ProductRestored` |
| `CreateProductES.cs` | `ProductAdded` (enriched with Status, Brand, HasDimensions) |

Key changes:
- All handlers now return `Task<(IResult, OutgoingMessages)>` (was `Task<IResult>`)
- Removed manual `SaveChangesAsync()` calls — AutoApplyTransactions handles persistence
- Added `IsRecall` flag to `ChangeProductStatusCommand`
- `ChangeProductStatusES` publishes `ProductDiscontinued` when `NewStatus == Discontinued`
- No handler publishes `ProductUpdated` (deprecated)

### Part 1C — Product-Recall Priority Exchange

Configured in `src/Product Catalog/ProductCatalog.Api/Program.cs`:

```csharp
opts.PublishMessage<Messages.Contracts.ProductCatalog.ProductDiscontinued>()
    .ToRabbitExchange("product-recall", exchange =>
    {
        exchange.ExchangeType = Wolverine.RabbitMQ.ExchangeType.Fanout;
    });
```

The `product-recall` exchange receives all `ProductDiscontinued` messages (in addition to the standard `product-catalog-product-discontinued` exchange). Downstream consumers like Listings BC can bind to this exchange for priority processing.

All 7 granular integration messages also have dedicated exchange routing:
- `product-catalog-product-content-updated`
- `product-catalog-product-category-changed`
- `product-catalog-product-images-updated`
- `product-catalog-product-dimensions-changed`
- `product-catalog-product-status-changed`
- `product-catalog-product-deleted`
- `product-catalog-product-restored`

### Part 1D — Event Sourcing Behavior Tests

10 new tests in `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/EventSourcingBehaviorTests.cs`:

1. `ChangeProductName_EmitsProductNameChangedDomainEvent_And_ProductContentUpdatedIntegration` ✅
2. `ChangeProductDescription_EmitsProductDescriptionChangedDomainEvent_And_ProductContentUpdatedIntegration` ✅
3. `ChangeProductCategory_EmitsProductCategoryChangedDomainEvent_And_Integration` ✅
4. `ChangeProductStatus_ToDiscontinuedWithIsRecall_EmitsProductDiscontinuedWithRecallFlag` ✅
5. `ChangeProductStatus_ToDiscontinuedWithoutRecall_EmitsStatusChangedAndDiscontinuedWithRecallFalse` ✅
6. `ChangeProductStatus_ToComingSoon_EmitsStatusChangedOnly_NoProductDiscontinued` ✅
7. `CreateProduct_EmitsEnrichedProductAdded` ✅
8. `CreateProduct_WithoutDimensions_HasDimensionsFalse` ✅
9. `SoftDeleteProduct_EmitsProductDeleted` ✅
10. `RestoreProduct_EmitsProductRestored` ✅

**Test fixture update:** Added `ProductManager` role and `Backoffice` scheme override to TestFixture's AdminAuthHandler to support SoftDelete and Restore endpoint testing (which require ProductManager policy).

**Total Product Catalog tests:** 58/58 passing (48 existing + 10 new)
**AssignProductToVendor tests:** All passing — no regression from handler changes

---

## Listings BC Scaffold

### Project Structure Created

```
src/Listings/
  Listings/
    Listings.csproj                    ← Domain project, references Messages.Contracts
  Listings.Api/
    Listings.Api.csproj                ← API project, Web SDK
    Program.cs                         ← Wolverine + Marten + JWT + health check
    appsettings.json                   ← postgres connection, RabbitMQ config
    Properties/launchSettings.json     ← Port 5246
    Dockerfile                         ← Multi-stage build

tests/Listings/
  Listings.Api.IntegrationTests/
    Listings.Api.IntegrationTests.csproj
    TestFixture.cs                     ← PostgreSQL TestContainer + TestAuthHandler
    IntegrationTestCollection.cs
    HealthCheckTests.cs                ← 1 test, passing
    Usings.cs

src/Shared/Messages.Contracts/Listings/
    package-info.cs                    ← Namespace placeholder for Session 2
```

### Program.cs Configuration

- ✅ `opts.Policies.AutoApplyTransactions()` — present from first commit
- ✅ `opts.Policies.UseDurableLocalQueues()`
- ✅ `opts.Policies.UseDurableOutboxOnAllSendingEndpoints()`
- ✅ `opts.UseFluentValidation()`
- ✅ JWT Bearer authentication middleware
- ✅ `app.UseAuthentication()` and `app.UseAuthorization()` in correct pipeline position
- ✅ Marten with `listings` schema
- ✅ `/health` endpoint returns 200 (AllowAnonymous)
- ✅ RabbitMQ queues: `listings-product-catalog-events`, `listings-product-recall`

### Infrastructure

- ✅ Added to `CritterSupply.slnx` under `/Listings/` folder
- ✅ Added `listings-api` service to `docker-compose.yml` (port 5246:8080, profile: `[all, listings]`)
- ✅ Added `listings` database to `docker/postgres/create-databases.sh`

### Health Check

Listings.Api starts and responds to `/health` — confirmed via integration test (1/1 passing).

---

## ADRs

- ✅ **ADR 0041** — Product Catalog ES Migration Decisions (committed)
- ✅ **ADR 0042** — `catalog:` Namespace UUID v5 Convention (committed)

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 33 (matches M36.0 baseline)
- **Product Catalog tests:** 58/58 ✅
- **Listings tests:** 1/1 ✅

---

## What Session 2 Should Pick Up

1. **Listing aggregate** — event-sourced aggregate with domain events, `Create()` factory, `Apply()` methods
2. **ProductSummaryView** — anti-corruption layer projection built from Product Catalog integration messages
3. **Listing lifecycle handlers** — create, activate, deactivate, end listing
4. **Integration message handlers** — consume `ProductContentUpdated`, `ProductCategoryChanged`, `ProductStatusChanged`, `ProductDeleted`, `ProductDiscontinued` from Product Catalog
5. **Recall cascade handler** — consume from `listings-product-recall` queue, auto-end listings for recalled products
6. **Integration tests** — verify lifecycle + recall cascade

### Session 2 Prerequisites (All Met)

- ✅ Granular integration contracts exist in `Messages.Contracts/ProductCatalog/`
- ✅ ProductDiscontinued enriched with `Reason` and `IsRecall`
- ✅ `product-recall` exchange configured
- ✅ Listings.Api starts and responds to `/health`
- ✅ Test fixture with auth ready in `Listings.Api.IntegrationTests`
