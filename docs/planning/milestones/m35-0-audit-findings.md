# M35.0 Documentation Audit Findings

**Date:** 2026-03-27
**Audit Type:** Documentation accuracy verification against codebase
**Scope:** CURRENT-CYCLE.md, CONTEXTS.md, README.md — verified against git history and source code

---

## 1. Git History Analysis (Last 15 Commits on Main)

| Commit | Description | BC/Area | CURRENT-CYCLE.md Updated? |
|--------|-------------|---------|--------------------------|
| `bd3bcd1` | M35.0 Session 6: Returns test fix, Product Catalog ES handlers, VP team mgmt BFF | Returns, Product Catalog, Vendor Portal | ❌ **No** — Session 5 and 6 progress never recorded |
| `a2f7315` | M35.0 Session 5: Product Catalog ES migration + Exchange v2 | Product Catalog, Returns | ❌ **No** — Session 5 progress never recorded |
| `ca99d52` | M35.0 Session 4: Event modeling for Track 3 | Planning, Features | ✅ Yes |
| `45894d1` | Fix stale E2E locators in CustomerSearchPage POM | Backoffice E2E | ✅ Yes (Session 3) |
| `d96ec0b` | M35.0 Session 2: E2E coverage for customer detail page | Backoffice E2E | ✅ Yes |
| `635b3e8` | M35.0 Session 1: Customer detail page, milestone housekeeping | Backoffice | ✅ Yes |
| `4ba8a7b` | M34.0: Homepage experience, label drift, CI | Various | ✅ Yes |
| `feba3c6` | Add three new custom Copilot agents | Documentation | N/A |
| `8429cbc` | M34.0: Experience completion (F1) + vocabulary alignment (F2) | Backoffice, Returns | ✅ Yes |
| `0958961` | Fix Backoffice E2E bootstrap | Backoffice E2E | ✅ Yes |
| `710e62a` | Add M34.0 stabilization-first plan | Planning | ✅ Yes |
| `08b9097` | M34.0 planning: stabilization patterns + RBAC bug | Planning | ✅ Yes |
| `cb53851` | M33.0 Milestone Closure: E2E CI job + retrospective | CI, Documentation | ✅ Yes |
| `559bb65` | Extract M33.0 VP E2E lessons into testing guidance | Documentation | N/A |
| `e000e4c` | Add M33.0 E2E Test Final Report | Documentation | N/A |

**Documentation gap:** Sessions 5 and 6 of M35.0 shipped significant implementation work but CURRENT-CYCLE.md was never updated to reflect it. The Quick Status table still says "Session 4" and the Active Milestone section has no Session 5 or Session 6 progress entries.

---

## 2. Product Catalog Migration Status

### Handler-by-Handler Status Table

| Handler | Migrated to ES? | Projection Updated? | Integration Tests? |
|---------|-----------------|--------------------|--------------------|
| **CreateProductES** | ✅ Yes — `session.Events.StartStream<CatalogProduct>()` | ✅ `ProductCreated` handled in projection | ✅ `AddProductTests` |
| **ChangeProductNameES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductNameChanged` handled | ✅ `UpdateProductTests` |
| **ChangeProductDescriptionES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductDescriptionChanged` handled | ✅ `UpdateProductTests` |
| **ChangeProductCategoryES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductCategoryChanged` handled | ✅ `UpdateProductTests` |
| **UpdateProductImagesES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductImagesUpdated` handled | ✅ `UpdateProductTests` |
| **ChangeProductDimensionsES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductDimensionsChanged` handled | ✅ `UpdateProductTests` |
| **UpdateProductTagsES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductTagsUpdated` handled | ✅ `UpdateProductTests` |
| **ChangeProductStatusES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductStatusChanged` handled | ✅ `ChangeProductStatusTests` |
| **SoftDeleteProductES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductSoftDeleted` handled | ✅ `ChangeProductStatusTests` |
| **RestoreProductES** | ✅ Yes — `session.Events.Append()` | ✅ `ProductRestored` handled | ✅ `ChangeProductStatusTests` |
| **MigrateProductES** | ✅ Yes — `session.Events.StartStream<CatalogProduct>()` | ✅ `ProductMigrated` handled | ✅ (part of test setup) |
| **GetProductES** | ✅ Reads from `ProductCatalogView` projection | N/A (query) | ✅ `GetProductTests` |
| **ListProductsES** | ✅ Reads from `ProductCatalogView` projection | N/A (query) | ✅ `ListProductsTests` |
| **AssignProductToVendor** | ❌ **Not migrated** — still uses `session.Store(updated)` on `Product` document | ❌ Not event-sourced | ✅ `AssignProductToVendorTests` |

### Migration Assessment

**Completion: ~93% (13/14 handlers event-sourced)**

The only remaining non-event-sourced handler is `AssignProductToVendor`, which still reads from and writes to the `Product` document model using `session.LoadAsync<Product>()` and `session.Store()`. This handler manages vendor assignment metadata, not core product data, so it is the lowest priority for migration.

All 11 domain events are defined in `ProductEvents.cs`. The `CatalogProduct` aggregate has `Apply()` methods for all 11 events. The `ProductCatalogViewProjection` handles all 11 events. The legacy `Product` document model is retained for the `AssignProductToVendor` handler and the migration bootstrap path (`MigrateProductES` loads from document store).

Legacy document-store handlers (`UpdateProduct.cs`, `UpdateProductDescription.cs`, `UpdateProductDisplayName.cs`) were deleted in Session 6.

**Verdict:** The Product Catalog ES migration is substantively complete. All core product CRUD and lifecycle operations are event-sourced. The vendor assignment handler is the sole remaining document-store write path.

---

## 3. Returns Exchange Constraint

### CONTEXTS.md Currently Says
> Exchanges are same-SKU only (Phase 1). 30-day eligibility window post-delivery.

### What the Code Actually Shows

Cross-product exchange is **fully implemented** as of Session 5:

- `Return` aggregate has fields: `IsCrossProductExchange`, `PriceDifference`, `AdditionalPaymentAmount`, `AdditionalPaymentCaptured`
- 5 new domain events exist: `CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`
- `ApproveExchange` handler supports both same-SKU and cross-product exchanges with price difference handling
- `RequestReturn` command accepts `ReplacementSku` for exchange requests
- RabbitMQ routing configured for `CrossProductExchangeRequested` to Orders and Storefront
- Integration message contracts exist in `Messages.Contracts.Returns/`
- 6 unit tests for cross-product exchange Apply methods pass
- 7 exchange-specific integration tests pass

**Verdict:** The CONTEXTS.md constraint description is **incorrect**. Cross-product exchange with price difference handling (upcharge/partial refund) is implemented. The 30-day eligibility window is still accurate.

---

## 4. Vendor Portal Team Management

### Completion Status: **Partial — BFF backend complete, no frontend page**

**What exists:**
- ✅ BFF proxy endpoints in `VendorPortal.Api/TeamManagement/`:
  - `GET /api/vendor-portal/team/roster` → `TeamRosterView`
  - `GET /api/vendor-portal/team/invitations/pending` → `PendingInvitationsView`
- ✅ Local Marten read models: `TeamMember`, `TeamInvitation`
- ✅ Event handlers in `VendorPortal/TeamManagement/TeamEventHandlers.cs` (subscribes to 7 VendorIdentity events)
- ✅ RabbitMQ wiring for team management events (publish in VendorIdentity.Api, subscribe in VendorPortal.Api)
- ✅ 86/86 VendorPortal.Api integration tests pass

**What does NOT exist:**
- ❌ No Blazor WASM team management page in `VendorPortal.Web/Pages/`
- ❌ No E2E tests for team management
- ❌ No NavMenu entry for team management

**Verdict:** The BFF backend layer is complete (endpoints + event handlers + read models). The frontend Blazor page is not implemented. This feature is approximately 60% complete.

---

## 5. GitHub Issues #254 and #255

| Issue | Title | State | Assessment |
|-------|-------|-------|------------|
| #254 | [Vendor Identity] Create EF Core project structure + migrations | **Still Open** | All tasks described are fully implemented in `src/Vendor Identity/`. Stale issue — work was completed but issue was never closed. |
| #255 | [Vendor Identity] Implement CreateVendorTenant command + handler | **Still Open** | Fully implemented: `CreateVendorTenant` command, handler, validator, integration event, endpoint, tests all exist. Stale issue. |

Both issues were identified as stale in Session 4 and flagged for closure in Session 6. They remain open.

---

## 6. Returns Integration Test Failures

The Session 5 retrospective noted 14 pre-existing failures in Returns integration tests with `WeaselDatabase` setup errors. The Session 6 commit message states the fix:

> Root cause: GET endpoints (GetReturn, GetReturnsForOrder) have [Authorize(Policy = "CustomerService")] but the test fixture had no authentication bypass. Tests received 401 Unauthorized. Fix: Register TestAuthHandler for both 'Backoffice' and 'Vendor' named schemes. Result: 44/44 tests pass (6 cross-BC smoke tests skipped, need RabbitMQ).

**Verdict:** Returns integration test failures were **fixed** in Session 6. 44/44 tests pass. The 6 skipped tests require RabbitMQ (cross-BC smoke tests) and are not broken — they are infrastructure-dependent.

---

## 7. Backoffice BC Status

CONTEXTS.md lists Backoffice under "Planned" with a one-line description:
> Internal tooling for operations, customer service, and merchandising. BFF pattern composing data from multiple BCs with RBAC via Backoffice Identity.

**What actually exists (substantially implemented):**
- 92 C# files across `Backoffice/`, `Backoffice.Api/`, `Backoffice.Web/`
- 24 Razor files including 20 pages/components
- BFF API with 8 feature folders: AlertManagement, CustomerService, DashboardReporting, OrderManagement, OrderNotes, ProductCatalog, ReturnManagement, WarehouseOperations
- Blazor WASM frontend with JWT auth, role-based navigation, session management
- 12 HTTP client interfaces for cross-BC composition
- SignalR hub for real-time updates
- Marten projections for dashboard metrics and alert feeds
- 95+ integration tests passing
- E2E tests with Playwright (CustomerSearch, CustomerDetail, Returns, Products, Pricing, Warehouse, Users)

**Verdict:** Backoffice is **substantially implemented** and should be moved from "Planned" to "Implemented" in CONTEXTS.md.

---

## 8. M35.0 Completion Assessment

### What M35.0 Has Delivered (Sessions 1–6)

| Track | Deliverable | Status |
|-------|-------------|--------|
| Track 1 | CURRENT-CYCLE.md housekeeping | ✅ Complete |
| Track 2 | CustomerSearch detail page (BFF + Blazor + tests) | ✅ Complete |
| Track 2 | E2E coverage for customer detail page | ✅ Complete |
| Track 2 | Stale E2E locator fix | ✅ Complete |
| Track 3 | Event modeling for Exchange v2, VP Team Mgmt, Product Catalog ES | ✅ Complete |
| Track 3 | Product Catalog ES migration (all core handlers) | ✅ Complete |
| Track 3 | Exchange v2 cross-product exchange | ✅ Complete |
| Track 3 | VP Team Management BFF endpoints | ✅ Complete |
| Track 3 | Returns integration test fix | ✅ Complete |
| Track 3 | VP Team Management Blazor page | ❌ Not implemented |
| Track 3 | VP Team Management E2E tests | ❌ Not implemented |

### Outstanding Items
1. **VP Team Management frontend** — BFF is done, Blazor page is not
2. **AssignProductToVendor ES migration** — Only non-ES handler in Product Catalog (low priority)
3. **GitHub issues #254 and #255** — Still open, need closure
4. **Documentation** — CURRENT-CYCLE.md has not been updated since Session 4

### Milestone Readiness
M35.0 has delivered the vast majority of its planned scope. The VP team management frontend is the only significant outstanding item. The milestone is approximately **90% complete** and could be closed with the VP frontend deferred to M36.0, or completed with one more session focused on the Blazor page.

---

*Audit conducted: 2026-03-27*
