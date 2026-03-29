# M36.0 Session 3 Retrospective: Track B Remaining + Track C Naming

**Date:** 2026-03-28
**Focus:** Track B items B-5 through B-7 (SaveChangesAsync sweep), bus.PublishAsync() audit, Track C items C-1 through C-3 (command renames), C-7 (convention documentation)
**Outcome:** All 7 items completed. Full solution builds cleanly (0 errors, 33 pre-existing warnings).

---

## Track B Items Completed

### B-5: Vendor Portal — Remove Redundant `SaveChangesAsync()` Calls

**Violation:** 27 `SaveChangesAsync()` calls across 21 handler files were redundant because `IntegrateWithWolverine()` is configured in `VendorPortal.Api/Program.cs` (line 70).

**Fix:** Removed all 27 `await session.SaveChangesAsync(ct);` calls from Wolverine handler files. Wolverine's transactional middleware automatically calls `SaveChangesAsync()` after handler completion.

**Verified:** `IntegrateWithWolverine()` confirmed at `VendorPortal.Api/Program.cs:70` before any removals.

**Files modified (21):**
- `ChangeRequests/`: DraftChangeRequest.cs, SubmitChangeRequest.cs, WithdrawChangeRequest.cs, ProvideAdditionalInfo.cs, DescriptionChangeApprovedHandler.cs, DescriptionChangeRejectedHandler.cs, DataCorrectionApprovedHandler.cs, DataCorrectionRejectedHandler.cs, ImageChangeApprovedHandler.cs, ImageChangeRejectedHandler.cs, AdditionalInfoRequestedHandler.cs, VendorTenantTerminated.cs
- `VendorAccount/`: SaveDashboardView.cs, DeleteDashboardView.cs, UpdateNotificationPreferences.cs, VendorTenantCreated.cs
- `Analytics/`: InventoryAdjustedHandler.cs, LowStockDetectedHandler.cs, StockReplenishedHandler.cs
- `VendorProductCatalog/`: VendorProductAssociatedHandler.cs
- `TeamManagement/`: TeamEventHandlers.cs (7 calls across 7 handler methods)

**Not touched:** `VendorPortal.Api/VendorPortalSeedData.cs` — seed data, not a Wolverine handler.

**Note:** The plan originally identified "12 handlers" but the actual count is 21 files with 27 calls. The higher count reflects handlers added after the plan was written.

**Build result:** 0 errors, 33 pre-existing warnings (unchanged).

---

### B-6: Pricing — Remove 5 Redundant `SaveChangesAsync()` Calls

**Violation:** 5 `SaveChangesAsync()` calls across 3 endpoint files.

**Fix:** Removed all 5 calls. Wolverine manages persistence via both `IntegrateWithWolverine()` (line 52) and `AutoApplyTransactions()` (line 119) in `Pricing.Api/Program.cs`.

**Files modified (3):**
- `SetBasePriceEndpoint.cs` — 2 calls removed (Unpriced and Published code paths)
- `SchedulePriceChangeEndpoint.cs` — 2 calls removed (schedule endpoint + activation handler)
- `CancelScheduledPriceChangeEndpoint.cs` — 1 call removed

**Note:** `SchedulePriceChangeEndpoint.cs` retains `await messaging.ScheduleAsync(...)` for delayed message delivery — this is the justified `IMessageContext` use, same as `bus.ScheduleAsync()` in Returns.

**Build result:** 0 errors, 33 warnings (unchanged).

---

### B-7: Product Catalog — Remove 2 Redundant `SaveChangesAsync()` Calls

**Violation:** 2 `SaveChangesAsync()` calls in `AssignProductToVendorES.cs`.

**Fix:** Removed both calls. `IntegrateWithWolverine()` is configured at `ProductCatalog.Api/Program.cs:47`.

**Files modified (1):**
- `AssignProductToVendorES.cs` — 1 call in single assignment handler (line 152), 1 call in bulk assignment handler (line 311)

**Not touched:** Other Product Catalog `*ES.cs` files also contain `SaveChangesAsync()` calls (12 total across 12 files). These are deferred to a future session as they are beyond B-7's scope, which targeted only `AssignProductToVendorES.cs`.

**Build result:** 0 errors, 33 warnings (unchanged).

---

### `bus.PublishAsync()` Audit — Remaining BCs

**Scope:** Solution-wide search for `bus.PublishAsync(` and `.PublishAsync(` in handler files (excluding test projects).

**Result:** **Zero violations found.** Only two references remain, both are comments documenting the Session 2 fix:
- `src/Inventory/Inventory.Api/InventoryManagement/AdjustInventory.cs:110` — comment
- `src/Returns/Returns/ReturnProcessing/RequestReturn.cs:178` — comment

**`ScheduleAsync` inventory (justified uses):**
| File | Purpose |
|------|---------|
| `Pricing.Api/Pricing/SchedulePriceChangeEndpoint.cs:100` | Schedule delayed price activation |
| `Returns/ReturnProcessing/ApproveExchange.cs:151` | Schedule return expiration |
| `Returns/ReturnProcessing/ApproveReturn.cs:54` | Schedule return expiration |
| `Returns/ReturnProcessing/RequestReturn.cs:176` | Schedule return expiration |

All 4 `ScheduleAsync` calls require `IMessageBus`/`IMessageContext` for delayed delivery semantics. These are the only justified `IMessageBus` injections in handlers.

---

## Track B Summary

| Item | BC | Calls Removed | Files Changed |
|------|----|---------------|---------------|
| B-5 | Vendor Portal | 27 | 21 |
| B-6 | Pricing | 5 | 3 |
| B-7 | Product Catalog | 2 | 1 |
| **Total** | | **34** | **25** |

Combined with Session 2 (B-1 through B-4): Track B is **complete**. Zero `bus.PublishAsync()` calls remain in handler code. All `SaveChangesAsync()` calls in the targeted Marten-backed BCs have been removed.

---

## Track C Items Completed

### C-1: Rename `PaymentRequested` → `RequestPayment` (Internal Command)

**What was renamed:**
- Record: `PaymentRequested` → `RequestPayment`
- Validator: `PaymentRequestedValidator` → `RequestPaymentValidator`
- Handler: `PaymentRequestedHandler` → `RequestPaymentHandler`
- File: `PaymentRequested.cs` → `RequestPayment.cs`

**Source files changed:**
- `src/Payments/Payments/Processing/RequestPayment.cs` (renamed from `PaymentRequested.cs`)

**Test files changed:**
- `tests/Payments/Payments.UnitTests/Processing/RequestPaymentValidatorPropertyTests.cs` (renamed)
- `tests/Payments/Payments.UnitTests/Processing/RequestPaymentValidatorTests.cs` (renamed)
- `tests/Payments/Payments.Api.IntegrationTests/Processing/AuthorizationFlowTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/RefundFlowTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/PaymentFlowTests.cs`

**Event stream verification:** ✅ Confirmed — `PaymentRequested` does NOT appear in any `session.Events.Append()` call. It is a command, not a persisted domain event. The actual domain events are `PaymentInitiated`, `PaymentCaptured`, and `PaymentFailed`.

**No dispatch site in Orders:** `PaymentRequested` was not instantiated or dispatched from the Orders BC or any other BC. It is only invoked directly in tests.

**Solution-wide find:** Zero remaining code references to `PaymentRequested` (`.cs` files in `src/` and `tests/`). Documentation files (planning retrospectives, docs examples) retain historical references.

---

### C-2: Rename `RefundRequested` → `RequestRefund` (Internal Command Only)

**What was renamed:**
- Record: `Payments.Processing.RefundRequested` → `Payments.Processing.RequestRefund`
- Validator: `RefundRequestedValidator` → `RequestRefundValidator`
- Handler: `RefundRequestedHandler` → `RequestRefundHandler`
- File: `RefundRequested.cs` → `RequestRefund.cs`

**What was NOT renamed:**
- `Messages.Contracts.Payments.RefundRequested` — the integration event. This is correctly named per the `*Requested` convention (ADR 0040) and remains unchanged.

**Source files changed:**
- `src/Payments/Payments/Processing/RequestRefund.cs` (renamed from `RefundRequested.cs`)

**Test files changed:**
- `tests/Payments/Payments.UnitTests/Processing/RequestRefundValidatorPropertyTests.cs` (renamed)
- `tests/Payments/Payments.Api.IntegrationTests/Processing/RefundFlowTests.cs`

**Not changed (correctly references integration event):**
- `tests/Orders/Orders.UnitTests/Placement/OrderDeciderCancellationTests.cs` — uses `PaymentContracts.RefundRequested` (integration event)
- `tests/Orders/Orders.UnitTests/Placement/OrderDeciderInventoryTests.cs` — uses `Messages.Contracts.Payments.RefundRequested`
- `tests/Orders/Orders.UnitTests/Placement/OrderSagaReturnWindowTests.cs` — uses `Messages.Contracts.Payments.RefundRequested`
- `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/ReturnsToOrdersPipelineTests.cs` — uses `Messages.Contracts.Payments.RefundRequested`

**Name collision resolved:** After this rename, `RefundRequested` unambiguously refers to the integration event (`Messages.Contracts.Payments.RefundRequested`) and `RequestRefund` unambiguously refers to the internal Payments command (`Payments.Processing.RequestRefund`).

**Event stream verification:** ✅ Confirmed — `RefundRequested` (internal command) does NOT appear in any `session.Events.Append()` call. The domain event for refunds is `PaymentRefunded`.

---

### C-3: Rename `CalculateDiscountRequest` → `CalculateDiscount`

**What was renamed:**
- Record: `CalculateDiscountRequest` → `CalculateDiscount`
- Validator: `CalculateDiscountRequestValidator` → `CalculateDiscountValidator`
- File: `CalculateDiscountRequest.cs` → `CalculateDiscount.cs`
- File: `CalculateDiscountRequestValidator.cs` → `CalculateDiscountValidator.cs`

**Handler class rename (collision resolution):**
The endpoint handler class in `Promotions.Api.Queries.CalculateDiscount` was already named `CalculateDiscount`, creating a naming collision with the renamed command record. Resolved by renaming the handler class to `CalculateDiscountEndpoint`, consistent with the endpoint naming pattern used elsewhere (`SetBasePriceEndpoint`, `CancelScheduledPriceChangeEndpoint`).

**Source files changed:**
- `src/Promotions/Promotions/Discount/CalculateDiscount.cs` (renamed from `CalculateDiscountRequest.cs`)
- `src/Promotions/Promotions/Discount/CalculateDiscountValidator.cs` (renamed from `CalculateDiscountRequestValidator.cs`)
- `src/Promotions/Promotions.Api/Queries/CalculateDiscount.cs` (handler class → `CalculateDiscountEndpoint`)
- `src/Shopping/Shopping.Api/Clients/PromotionsClient.cs` (private record rename)

**Test files changed:**
- `tests/Promotions/Promotions.IntegrationTests/DiscountCalculationTests.cs`

**Event stream verification:** ✅ Confirmed — `CalculateDiscountRequest` does NOT appear in any `session.Events.Append()` call. It is a stateless query/command, not a domain event.

---

### C-7: Document `*Requested` Integration Event Convention (ADR 0040)

Created `docs/decisions/0040-requested-integration-event-convention.md` documenting:
1. The convention: `*Requested` suffix signals command-intent integration messages between BCs
2. Why it exists: loose coupling via choreography, multiple subscriber support
3. What it is not: not a domain event, not an internal command, not a query
4. The 4 canonical examples: `FulfillmentRequested`, `ReservationCommitRequested`, `ReservationReleaseRequested`, `RefundRequested`
5. Future evolution: potential conversion to Wolverine routed commands (deliberate deferral)

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 33 (all pre-existing E2E test files, unchanged from Session 2)
- **CI run:** Pending (PR submitted)

---

## What Session 4 Should Pick Up

1. **C-4:** Vendor Portal vertical slice refactors (TeamEventHandlers.cs restructuring deferred from B-5)
2. **C-5:** Product Catalog vertical slice refactors (AssignProductToVendorES.cs restructuring + remaining SaveChangesAsync removals)
3. **C-6:** Additional vertical slice compliance per ADR 0039
4. **Observation:** Product Catalog has 12 additional `SaveChangesAsync()` calls beyond the 2 removed in B-7. These are in other `*ES.cs` handler files. Session 4 should consider sweeping these as part of C-5.
