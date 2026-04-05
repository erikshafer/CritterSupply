# M39.0 Session 5 Retrospective

**Date:** 2026-04-05
**Milestone:** M39.0 — Critter Stack Idiom Refresh: Quick Wins + Promotions
**Session:** Session 5 — Fulfillment, Listings, Promotions, Vendor Portal idiom cleanup

---

## Baseline

- Build: 0 errors, 19 warnings (unchanged from S4 close)
- Fulfillment integration tests: 17/17 passing
- Listings integration tests: 41/41 passing
- Promotions integration tests: 29/29 passing
- Vendor Portal integration tests: 86 total (56 pre-existing failures on first run; 86/86 on subsequent runs — infrastructure warmup flakes)

---

## Items Completed

| Item | Description |
|------|-------------|
| D-1 (S5a) | Fulfillment `RequestFulfillmentHandler` → `IStartStream` return |
| D-2 (S5b) | Listings `CreateListingHandler` → `IStartStream` return |
| D-3 (S5c) | Listings `ApproveListing` + `SubmitListingForReview` → `[WriteAggregate]` compound handler |
| D-3 (S5d) | Listings `ActivateListing` + `PauseListing` + `ResumeListing` + `EndListing` → `[WriteAggregate]` compound handler |
| D-4 (S5e) | Promotions `RedeemCouponHandler` → `Load()` + `Before()` + `Handle()` compound pattern |
| D-4 (S5f) | Promotions `RevokeCouponHandler` → `Load()` + `Before()` + `Handle()` compound pattern |
| D-5 (S5g) | Promotions `RecordPromotionRedemptionHandler` → `[WriteAggregate]` compound handler |
| D-6 (S5h) | Vendor Portal — `AutoApplyTransactions()` added to `Program.cs` |

---

## D-1: Fulfillment — `RequestFulfillmentHandler` IStartStream Return

Trivial refactor. Removed `IDocumentSession` parameter, replaced `session.Events.StartStream<Shipment>()` with `MartenOps.StartStream<Shipment>()`, returned `(CreationResponse, IStartStream)` tuple. Added `using Wolverine.Marten;`, removed `using Marten;`.

No surprises. 17/17 tests pass unchanged.

---

## D-2: Listings — `CreateListingHandler` IStartStream Return

Replaced `session.Events.StartStream<Listing>()` with `MartenOps.StartStream<Listing>()`. Changed `IDocumentSession` to `IQuerySession` since the handler only reads (`LoadAsync<ProductSummaryView>`, `LoadAsync<ListingsActiveView>`) — it no longer writes to the session directly. Return type changed from `(CreateListingResponse, OutgoingMessages)` to `(CreateListingResponse, IStartStream, OutgoingMessages)`.

No surprises. 41/41 tests pass unchanged.

---

## D-3: Listings — 6 Write Handlers → `[WriteAggregate]` Compound Handler

### `[WriteAggregate]` Resolution Confirmation

**Confirmed:** Wolverine correctly resolves `ListingId` → `Listing` stream by the `{AggregateName}Id` convention for all 6 handlers. No explicit `[Aggregate]` or custom attribute was needed. Each command record has a `Guid ListingId` property, and Wolverine matches it to the `Listing` aggregate type by naming convention.

### ApproveListing — Option A (ProductSummaryView category lookup)

**Chose Option A:** Injected `IQuerySession` in `Handle()` alongside `[WriteAggregate] Listing listing` to perform the `session.LoadAsync<ProductSummaryView>(listing.Sku)` lookup. This is valid — `IQuerySession` is injectable alongside the aggregate parameter. The `TODO(M37.0)` comment was preserved.

### ActivateListing — OWN_WEBSITE Fast Path in `Before()`

The two-condition check translated cleanly to `Before()`:
```csharp
var isOwnWebsiteFastPath = listing.Status == ListingStatus.Draft
    && string.Equals(listing.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase);

if (listing.Status != ListingStatus.Submitted && !isOwnWebsiteFastPath)
    return new ProblemDetails { ... };
```
Same logic, just expressed as `WolverineContinue.NoProblems` (allow) vs `ProblemDetails` (reject).

### Test Updates (3 tests)

Three tests expected `InvalidOperationException` to be thrown for invalid state transitions. After refactoring to `Before()` returning `ProblemDetails`, the handler pipeline stops silently in message handler context (no exception thrown). Updated tests to verify the state is unchanged instead:

- `SubmitForReview_FromLive_ReturnsDomainError` — verifies listing stays `Live`
- `ApproveListing_FromDraft_ReturnsDomainError` — verifies listing stays `Draft`
- `InvalidTransition_EndedToLive_ThrowsError` — verifies listing stays `Ended`

All 41/41 tests pass after updates.

### Handlers Refactored

| Handler | Before Return Type | After Return Type | Has OutgoingMessages |
|---------|-------------------|-------------------|---------------------|
| `ApproveListing` | `Task<OutgoingMessages>` | `Task<(Events, OutgoingMessages)>` | Yes |
| `ActivateListing` | `Task<OutgoingMessages>` | `(Events, OutgoingMessages)` | Yes |
| `EndListing` | `Task<OutgoingMessages>` | `(Events, OutgoingMessages)` | Yes |
| `SubmitListingForReview` | `Task` | `Events` | No |
| `PauseListing` | `Task` | `Events` | No |
| `ResumeListing` | `Task` | `Events` | No |

---

## D-4: Promotions — `RedeemCoupon` + `RevokeCoupon` (UUID v5 — Load() Pattern)

### UUID v5 Confirmed — `[WriteAggregate]` Cannot Be Used

As expected, `Coupon.StreamId(cmd.CouponCode)` derives a UUID v5 from the coupon code string using SHA-1 hash. Wolverine cannot compute this deterministic hash from the command's `CouponCode` property — same reason as Pricing BC's `SetBasePriceHandler` in S3.

### Load/Before/Handle Pattern Applied

Both handlers decomposed to:
- `LoadAsync(cmd, IQuerySession, CancellationToken)` — loads aggregate by `Coupon.StreamId(cmd.CouponCode)`
- `Before(cmd, Coupon?)` — null check + status validation → `ProblemDetails`
- `Handle(cmd, Coupon, IDocumentSession)` — appends domain event via `session.Events.Append()`

`void` return is correct — these are internal commands with no integration events. The caller publishes integration messages, not these handlers.

### Command Record Field Names Verified

- `RedeemCoupon.CouponCode` ✅ (not `Code`)
- `RevokeCoupon.CouponCode` ✅ (not `Code`)

### Test Updates (2 tests)

- `RedeemCoupon_WhenAlreadyRedeemed_Fails` — now verifies coupon stays `Redeemed`
- `RevokeCoupon_WhenAlreadyRevoked_Fails` — now verifies coupon stays `Revoked`

Namespace disambiguation required: `Promotions.Coupon.Coupon.StreamId()` because `Coupon` is both a namespace and a type in the test project context.

---

## D-5: Promotions — `RecordPromotionRedemptionHandler` `[WriteAggregate]`

### `[WriteAggregate]` Resolution Confirmation

**Confirmed:** `RecordPromotionRedemption` has `Guid PromotionId` — Wolverine resolves `Promotion` stream by `{AggregateName}Id` convention. Natural UUID v7, not UUID v5.

### Concurrency Strategy Preserved

The detailed documentation block about optimistic concurrency + Wolverine retry policy was preserved in the class-level `<summary>` comment. Updated "via tuple return pattern" to "via `[WriteAggregate]` (`FetchForWriting` under the hood)" to accurately describe the new mechanism.

### Test Updates (2 tests)

- `RecordPromotionRedemption_ExceedingUsageLimit_Fails` — now verifies redemption count stays at 2
- `RecordPromotionRedemption_ForDraftPromotion_Fails` — now verifies status stays `Draft` with 0 redemptions

All 29/29 Promotions tests pass.

---

## D-6: Vendor Portal — `AutoApplyTransactions()`

Added `opts.Policies.AutoApplyTransactions();` to `Program.cs` alongside the existing `opts.Discovery.*` and `opts.UseSignalR()` calls.

**`SaveChangesAsync` sweep:** Only one `SaveChangesAsync` found in the entire Vendor Portal — in `VendorPortalSeedData.cs` (seed data, not a handler). No redundant handler `SaveChangesAsync` calls to remove.

86/86 Vendor Portal integration tests pass (initial run showed 56 failures that did not reproduce on subsequent runs — infrastructure warmup flakes in the test container lifecycle).

---

## Test Results

| BC | Before | After | Δ |
|----|--------|-------|---|
| Fulfillment | 17/17 | 17/17 | 0 |
| Listings | 41/41 | 41/41 | 0 (3 tests updated for ProblemDetails behavior) |
| Promotions | 29/29 | 29/29 | 0 (4 tests updated for ProblemDetails behavior) |
| Vendor Portal | 86 total | 86/86 | 0 |
| **Total affected** | **173** | **173** | **0** |

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 19 (unchanged from session open — no new warnings introduced)
- **Files modified:**
  - `src/Fulfillment/Fulfillment/Shipments/RequestFulfillment.cs`
  - `src/Listings/Listings/Listing/CreateListing.cs`
  - `src/Listings/Listings/Listing/ApproveListing.cs`
  - `src/Listings/Listings/Listing/ActivateListing.cs`
  - `src/Listings/Listings/Listing/PauseListing.cs`
  - `src/Listings/Listings/Listing/ResumeListing.cs`
  - `src/Listings/Listings/Listing/EndListing.cs`
  - `src/Listings/Listings/Listing/SubmitListingForReview.cs`
  - `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs`
  - `src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs`
  - `src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs`
  - `src/Vendor Portal/VendorPortal.Api/Program.cs`
  - `tests/Listings/Listings.Api.IntegrationTests/ReviewWorkflowTests.cs`
  - `tests/Listings/Listings.Api.IntegrationTests/ListingLifecycleTests.cs`
  - `tests/Promotions/Promotions.IntegrationTests/CouponRedemptionTests.cs`

---

## Key Learnings

1. **`[WriteAggregate]` resolves `ListingId` → `Listing` by naming convention.** No special attributes or explicit configuration required. The `{AggregateName}Id` convention works out of the box for all 6 Listings write handlers.

2. **`ProblemDetails` in non-HTTP message handlers stops the pipeline silently.** When `Before()` returns `ProblemDetails` with a non-zero `Status`, Wolverine stops the handler pipeline in message context without throwing an exception. Tests that expected `InvalidOperationException` needed updating to verify state is unchanged instead.

3. **UUID v5 (deterministic) stream IDs require `Load()` pattern, not `[WriteAggregate]`.** Same finding as Pricing BC (S3). `Coupon.StreamId(code)` hashes the coupon code — Wolverine cannot compute this from the command property. The `Load()` + `Before()` + `Handle()` compound pattern with manual `session.Events.Append()` is the correct approach.

4. **Vendor Portal had zero redundant `SaveChangesAsync` in handlers.** The BFF is read-heavy with message handlers that primarily update Marten documents and push SignalR notifications. `AutoApplyTransactions()` was the only missing piece.

5. **Vendor Portal test flakes on first run.** 56 of 86 tests failed on the very first run (cold container), but all 86 passed on subsequent runs. This suggests a test fixture or container lifecycle initialization issue, not a code regression. Worth investigating in a future session.

---

## Verification Checklist

- [x] D-1: `RequestFulfillmentHandler` uses `MartenOps.StartStream<Shipment>()` and returns `(CreationResponse, IStartStream)`
- [x] D-2: `CreateListingHandler` uses `MartenOps.StartStream<Listing>()`, `IQuerySession` (not `IDocumentSession`), returns `(CreateListingResponse, IStartStream, OutgoingMessages)`
- [x] D-3: All 6 Listings write handlers use `[WriteAggregate]` + `Before()` compound pattern
- [x] D-3: `ApproveListing` uses Option A — `IQuerySession` injected for `ProductSummaryView` lookup
- [x] D-3: `ActivateListing.Before()` correctly handles OWN_WEBSITE fast path
- [x] D-4: `RedeemCouponHandler` uses `Load()` + `Before()` + `Handle()` — UUID v5 stream ID
- [x] D-4: `RevokeCouponHandler` uses `Load()` + `Before()` + `Handle()` — UUID v5 stream ID
- [x] D-5: `RecordPromotionRedemptionHandler` uses `[WriteAggregate]` + `Before()` — natural UUID v7
- [x] D-6: `AutoApplyTransactions()` added to Vendor Portal `Program.cs`
- [x] D-6: No redundant `SaveChangesAsync` found in handlers
- [x] Build: 0 errors, 19 warnings (unchanged)
- [x] Fulfillment: 17/17 passing
- [x] Listings: 41/41 passing
- [x] Promotions: 29/29 passing
- [x] Vendor Portal: 86/86 passing

---

## What Remains in M39.0

**S6 (Milestone Closure — documentation only):**
- Session retrospective for all M39.0 sessions
- CONTEXTS.md assessment (verify accuracy after idiom refresh)
- CURRENT-CYCLE.md milestone move (M39.0 → Recent Completions)

All implementation work in M39.0 is complete. S6 is the only remaining session.
