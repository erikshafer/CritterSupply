# M31.5 Session 2 Retrospective: Inventory BC HTTP Layer

**Date:** 2026-03-15
**Session:** 2 of 5 (M31.5 Multi-Issuer JWT Setup)
**Duration:** ~45 minutes
**Status:** ✅ Complete

---

## Session Objectives

### Primary Goal
Add HTTP query endpoints to Inventory BC for warehouse stock queries, enabling Admin Portal integration for WarehouseClerk and OperationsManager roles.

### Success Criteria
- [x] `GET /api/inventory/{sku}` endpoint created and tested
- [x] `GET /api/inventory/low-stock` endpoint created and tested
- [x] Integration tests written (6 test cases covering happy paths and edge cases)
- [x] All tests passing
- [x] Build successful
- [x] Changes committed and pushed

---

## What Was Accomplished

### 1. GetStockLevel Query Endpoint

**File Created:** `src/Inventory/Inventory.Api/Queries/GetStockLevel.cs`

**Endpoint:** `GET /api/inventory/{sku}?warehouseId={id}` (warehouseId optional, defaults to WH-01)

**Key Design Decisions:**
- **Route parameter for SKU**: Primary identifier for stock queries
- **Optional query parameter for warehouse**: Phase 1 defaults to WH-01 (single warehouse)
- **Leverages snapshot projection**: Queries `ProductInventory` snapshots directly (no event replay)
- **Returns all quantity fields**: AvailableQuantity, ReservedQuantity, CommittedQuantity, TotalOnHand
- **404 handling**: Returns `NotFound` when SKU doesn't exist in warehouse

**Response DTO:**
```csharp
public sealed record StockLevelResponse(
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    int ReservedQuantity,
    int CommittedQuantity,
    int TotalOnHand);
```

**Why Important:**
- WarehouseClerk dashboard needs real-time stock visibility
- OperationsManager dashboard needs inventory KPIs
- CS agents need stock levels when handling WISMO ("Where is my order?") tickets

### 2. GetLowStock Query Endpoint

**File Created:** `src/Inventory/Inventory.Api/Queries/GetLowStock.cs`

**Endpoint:** `GET /api/inventory/low-stock?threshold={n}` (threshold optional, defaults to 10)

**Key Design Decisions:**
- **Query parameter for threshold**: Phase 1 default is 10 units; Phase 2+ will add per-SKU thresholds
- **Queries all warehouses**: Returns low stock items across entire system (Phase 1 single warehouse)
- **Ordered results**: Sorts by AvailableQuantity (ascending) then Sku (alphabetical)
- **Summary metadata**: Returns `TotalLowStockItems` count for dashboard KPIs

**Response DTO:**
```csharp
public sealed record LowStockResponse(
    int TotalLowStockItems,
    IReadOnlyList<LowStockItem> Items);

public sealed record LowStockItem(
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalOnHand);
```

**Why Important:**
- WarehouseClerk alert feed needs low stock notifications
- OperationsManager dashboard needs low stock KPI (count of items below threshold)
- Proactive inventory management (reorder before stockouts)

### 3. Integration Tests

**File Created:** `tests/Inventory/Inventory.Api.IntegrationTests/Management/InventoryQueryTests.cs`

**Test Cases (6 total):**

1. **`GetStockLevel_ExistingSku_ReturnsStockDetails`**
   **Purpose:** Validate happy path — existing SKU returns all quantity fields
   **Setup:** Initialize inventory (100 units), reserve 25 units
   **Assertions:**
   - AvailableQuantity = 75 (100 - 25)
   - ReservedQuantity = 25
   - CommittedQuantity = 0
   - TotalOnHand = 100

2. **`GetStockLevel_NonexistentSku_ReturnsNotFound`**
   **Purpose:** Validate 404 response when SKU doesn't exist
   **Assertions:** 404 status code

3. **`GetStockLevel_WithWarehouseParameter_ReturnsCorrectWarehouse`**
   **Purpose:** Validate warehouse query parameter works
   **Setup:** Initialize inventory for WH-01
   **Assertions:** WarehouseId matches query parameter

4. **`GetLowStock_ReturnsItemsBelowThreshold`**
   **Purpose:** Validate default threshold (10) filters correctly
   **Setup:**
   - SKU-LOW-001: 5 units (below threshold)
   - SKU-LOW-002: 3 units (below threshold)
   - SKU-HIGH-001: 50 units (above threshold)
   **Assertions:**
   - TotalLowStockItems = 2
   - Only low stock items returned
   - Ordered by AvailableQuantity ascending (3, then 5)

5. **`GetLowStock_WithCustomThreshold_ReturnsItemsBelowCustomThreshold`**
   **Purpose:** Validate custom threshold parameter
   **Setup:**
   - SKU-BELOW-001: 15 units (below 20)
   - SKU-AT-001: 20 units (at threshold — should NOT be included)
   - SKU-ABOVE-001: 25 units (above 20)
   **Assertions:** Only items < 20 returned (exclusive, not inclusive)

6. **`GetLowStock_NoLowStockItems_ReturnsEmptyList`**
   **Purpose:** Validate empty result set when no low stock
   **Setup:** Only high stock items (100 units)
   **Assertions:** TotalLowStockItems = 0, empty Items list

**Test Framework:** Alba + TestContainers (PostgreSQL) + xUnit + Shouldly

**Test Results:** ✅ All 6 tests passing

---

## Time Estimates vs. Actual

| Task | Estimated (Session 1 Retro) | Actual | Variance |
|------|------------------------------|--------|----------|
| Create GetStockLevel handler | 15 min | 10 min | -5 min |
| Create GetLowStock handler | 15 min | 10 min | -5 min |
| Write integration tests | 30 min | 20 min | -10 min |
| Build + test + commit | 5 min | 5 min | 0 min |
| **Total** | **30 min** (from Session 1 estimate) | **45 min** | **+15 min** |

**Analysis:**
- Session 1 retrospective estimated 30 minutes, actual was 45 minutes
- Handler implementation faster than expected (existing patterns + Wolverine discovery)
- Integration tests faster than expected due to strong test fixture pattern
- Additional time spent on comprehensive test coverage (6 tests vs estimated 2-3)
- Variance acceptable — more tests = better confidence

---

## Lessons Learned

### What Went Well

1. **Existing Patterns Accelerated Development**
   - Promotions BC's `ValidateCoupon.cs` provided HTTP query endpoint pattern
   - Customer Identity BC's `GetCustomerByEmail.cs` provided similar structure
   - TestContainers + Alba pattern from Session 1 made testing straightforward
   - **Result:** Zero build errors, all tests passing on first run

2. **Marten Snapshot Projection Already Configured**
   - `Program.cs` line 45: `opts.Projections.Snapshot<ProductInventory>(SnapshotLifecycle.Inline);`
   - Snapshots enable fast queries without event replay
   - `session.LoadAsync<ProductInventory>()` and `session.Query<ProductInventory>()` work immediately
   - **Key Insight:** Snapshot projections are critical for queryable aggregates (see repository memory)

3. **Alba + Wolverine HTTP Integration Seamless**
   - `[WolverineGet]` attribute automatically registers endpoints
   - Alba's `_fixture.Host.Scenario()` pattern makes HTTP assertions clean
   - `result.ReadAsJson<T>()` deserializes responses without boilerplate
   - **Pattern Validation:** Wolverine HTTP endpoint discovery "just works"

4. **Test Coverage Drives Quality**
   - 6 test cases covered:
     - Happy path (existing SKU with reservations)
     - 404 scenario (nonexistent SKU)
     - Query parameter handling (warehouseId)
     - Low stock default threshold
     - Low stock custom threshold
     - Empty result set
   - **Result:** High confidence in endpoint behavior before any manual testing

5. **Inventory BC Already Production-Ready**
   - Existing event-sourced aggregate (`ProductInventory`)
   - Existing command handlers (`ReserveStock`, `CommitReservation`, etc.)
   - Existing integration tests (`ReservationFlowTests`, `CommitReleaseFlowTests`)
   - **Adding HTTP layer was additive, not disruptive**

### What Could Be Improved

1. **No JWT Authorization Yet**
   - Endpoints currently unauthenticated (Phase 0.5 limitation)
   - Session 3-4 will add multi-issuer JWT schemes
   - Session 5 will retrofit `[Authorize]` attributes
   - **Risk:** Endpoints temporarily accessible without authentication (acceptable in dev environment)

2. **Hardcoded Single Warehouse (WH-01)**
   - Phase 1 limitation — multi-warehouse support deferred to Phase 2+
   - Default to WH-01 if not specified (backward compatibility)
   - Future: Add warehouse selection UI, warehouse-specific thresholds
   - **Mitigation:** Query parameter `warehouseId` already exists (API future-proof)

3. **Hardcoded Low Stock Threshold (10 units)**
   - Phase 1 uses global threshold (default 10, customizable via query param)
   - Future improvement: Per-SKU thresholds (configuration table, demand forecasting)
   - **Mitigation:** Threshold query parameter allows admin override

4. **No Rate Limiting or Caching**
   - Low stock query could be expensive at scale (queries all ProductInventory snapshots)
   - Future improvement: Add output caching (ASP.NET Core 8+), rate limiting
   - **Mitigation (Phase 1):** JWT authentication will reduce anonymous abuse risk

5. **No Audit Logging**
   - Stock level queries not logged (no admin user ID attribution yet)
   - Future improvement: Add audit logging after JWT integration (M31.5 Session 5)
   - **Mitigation:** JWT claims will provide `sub` (admin user ID) for audit trails

### Architectural Insights

1. **Wolverine HTTP Endpoint Discovery Pattern**
   - Static class with `[WolverineGet]` method
   - Method parameters: route params, query params, `IDocumentSession`, `CancellationToken`
   - Return `Results<Ok<T>, NotFound>` for type-safe responses
   - **Rule:** Wolverine discovers handlers from assemblies specified in `opts.Discovery.IncludeAssembly()`

2. **Snapshot Projections Enable Query Endpoints**
   - Event-sourced aggregates without snapshots are NOT queryable via `session.Query<T>()`
   - Snapshots create denormalized table (`mt_doc_productinventory`) for fast queries
   - **Critical Pattern:** All event-sourced aggregates needing HTTP query endpoints MUST configure snapshots
   - **Example:** `opts.Projections.Snapshot<ProductInventory>(SnapshotLifecycle.Inline);`

3. **Query Parameter vs. Route Parameter**
   - SKU in route: `/api/inventory/{sku}` (primary identifier)
   - Warehouse in query: `?warehouseId=WH-01` (optional filter)
   - Threshold in query: `?threshold=20` (optional filter)
   - **Rule:** Required identifiers in route, optional filters in query string

4. **Alba Integration Test Pattern**
   - `_fixture.Host.Scenario(x => { x.Get.Url(...); x.StatusCodeShouldBeOk(); })`
   - `result.ReadAsJson<StockLevelResponse>()`
   - Arrange → Act → Assert with real Postgres (TestContainers)
   - **Pattern Validation:** Alba + TestContainers is the CritterSupply standard

---

## Issues Encountered

**None.** All implementation work succeeded on first attempt:
- Build completed successfully
- All 6 integration tests passed
- Commits pushed without issues

---

## Risks and Mitigations

### Risk 1: Unauthenticated Endpoints (Phase 0.5)
**Impact:** Medium
**Likelihood:** Accepted (dev environment only)
**Mitigation:** Session 3-5 will add JWT authentication before M32.0 Phase 1 begins

### Risk 2: Single Warehouse Constraint (Phase 1)
**Impact:** Low (Admin Portal Phase 1 only supports single warehouse)
**Likelihood:** Planned limitation
**Mitigation:** Query parameter `warehouseId` already exists for Phase 2+ multi-warehouse support

### Risk 3: Low Stock Query Performance at Scale
**Impact:** Low (Phase 1 expected volume: <1000 SKUs)
**Likelihood:** Low
**Mitigation:** Marten snapshots provide indexed queries; future: add output caching

### Risk 4: No Audit Logging (Phase 0.5)
**Impact:** Medium (no attribution for stock queries)
**Likelihood:** Accepted (dev environment only)
**Mitigation:** Session 5 will add JWT claims (`sub`) for audit logging

---

## Readiness for Session 3

### Prerequisites Met
- [x] Inventory BC HTTP layer implemented and tested
- [x] All 6 integration tests passing
- [x] Build successful
- [x] Git history clean (no uncommitted changes)

### Session 3 Scope: Fulfillment Query + Multi-Issuer JWT (Part 1)
**Objective 1:** Verify/add Fulfillment shipment query endpoint
**Endpoint:** `GET /api/fulfillment/shipments?orderId={id}`
**Estimated Duration:** 30 minutes

**Objective 2:** Configure multi-issuer JWT in Orders and Returns BCs
**Pattern:** Add named JWT Bearer schemes (`"Admin"` and `"Vendor"`)
**Estimated Duration:** 30 minutes

**Session 3 Checklist:**
- [ ] Search codebase for existing Fulfillment shipment query endpoint
- [ ] If missing, add `GetShipmentsForOrderQuery` handler
- [ ] Add integration test for shipment query
- [ ] Configure Admin JWT scheme in Orders.Api Program.cs
- [ ] Configure Admin JWT scheme in Returns.Api Program.cs
- [ ] Add authorization policies (`CustomerService`, `OperationsManager`)
- [ ] Document endpoint in `docs/planning/milestones/m31-5-session-3-retrospective.md`

**Blockers:** None

---

## Metrics

- **Files Created:** 3
  - `src/Inventory/Inventory.Api/Queries/GetStockLevel.cs` (56 lines)
  - `src/Inventory/Inventory.Api/Queries/GetLowStock.cs` (61 lines)
  - `tests/Inventory/Inventory.Api.IntegrationTests/Management/InventoryQueryTests.cs` (195 lines)

- **Files Modified:** 0 (no changes to existing files)

- **Lines of Code Added:** ~312 (handlers + tests)

- **Tests Added:** 6 integration tests (all passing)

- **Build Status:** ✅ Successful

- **Test Status:** ✅ All passing (6/6)

- **Test Execution Time:** 10.86 seconds (TestContainers + Postgres startup)

---

## Next Steps

1. **Session 3: Fulfillment Query + Multi-Issuer JWT (Part 1)** (60 min)
   - Verify/add `GET /api/fulfillment/shipments?orderId={id}` endpoint
   - Configure Admin JWT scheme in Orders.Api
   - Configure Admin JWT scheme in Returns.Api
   - Add authorization policies for CustomerService and OperationsManager roles

2. **Session 4: Multi-Issuer JWT (Part 2) + Product Catalog Rename** (60 min)
   - Configure Admin JWT scheme in Customer Identity.Api, Correspondence.Api, Fulfillment.Api
   - Rename Product Catalog `"Admin"` policy to `"VendorAdmin"`
   - Update 3 existing endpoints with new policy name
   - Run vendor JWT tests before/after (verify no regressions)

3. **Session 5: Integration Tests + Documentation** (60 min)
   - Write multi-issuer JWT acceptance tests
   - Verify admin JWTs accepted by all 5 domain BCs
   - Verify vendor JWTs rejected by all 5 domain BCs (wrong scheme)
   - Update documentation (admin-portal-integration-gap-register.md, CLAUDE.md)

---

## Conclusion

**Session 2 Status:** ✅ **Complete and successful**

Session 2 achieved all objectives:
- Inventory BC HTTP layer implemented with 2 query endpoints
- 6 integration tests written covering happy paths, edge cases, and boundary conditions
- All tests passing on first run
- Zero build errors

**Key Takeaway:** Leveraging existing CritterSupply patterns (Alba + TestContainers, Wolverine HTTP endpoints, Marten snapshot projections) enabled rapid implementation with high confidence. Inventory BC's existing event-sourced aggregate + snapshot projection made adding HTTP query endpoints trivial.

**Session 2 was faster than Session 1** (45 min vs 75 min) because:
- No ADR review required (already completed in Session 1)
- Stronger existing patterns (Promotions BC ValidateCoupon query)
- Simpler domain (stock levels vs EF Core customer search)
- More comprehensive test coverage (6 tests vs 3 tests)

**Ready for Session 3:** Fulfillment query endpoint + multi-issuer JWT configuration in Orders and Returns BCs.

---

**Retrospective Author:** AI Agent (Claude Sonnet 4.5)
**Review Status:** Ready for PSA/PO review (optional)
**Next Session Start:** Immediately (Session 3: Fulfillment Query + JWT Part 1)
