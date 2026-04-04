# M38.1 Milestone Closure Retrospective

**Milestone:** M38.1 — Marketplaces Phase 4b: Deactivation + Status Verification
**Status:** ✅ **COMPLETE**
**Sessions:** 2 implementation sessions
**Date:** 2026-04-04

---

## Session 2 Deliverables

### B-1: `AmazonMarketplaceAdapter.CheckSubmissionStatusAsync`

Replaced the skeleton implementation with a real SP-API GET call.

**Endpoint:** `GET /listings/2021-08-01/items/{sellerId}/{sku}?marketplaceIds={marketplaceId}&includedData=summaries`

**Response field mapping:**
- `summaries[0].status == "BUYABLE"` → `IsLive: true, IsFailed: false`
- `summaries[0].status` is non-empty and not BUYABLE (e.g., `"INACTIVE"`) → `IsLive: false, IsFailed: true` with reason string
- Empty/null status → `IsLive: false, IsFailed: false` (still pending)
- HTTP 404 → `IsLive: false, IsFailed: false` (listing not yet visible on marketplace — pending, not failed)
- Non-success HTTP status (not 404) → `IsLive: false, IsFailed: true` with status code and response body

**Auth headers:** `Authorization: Bearer {token}` + `x-amz-access-token: {token}` (reuses `GetAccessTokenAsync` with LWA token caching).

**New DTOs:** `SpApiListingStatusResponse` (summaries array) and `SpApiListingSummary` (status field), both using `[JsonPropertyName]` attributes alongside existing `JsonOptions` (snake_case).

### B-2: Amazon Status Check Tests (3)

1. `CheckSubmissionStatus_ReturnsLive_WhenListingIsBuyable` — 200 with `BUYABLE` status; asserts `IsLive: true`. Also verifies request URL contains `includedData=summaries` and both auth headers.
2. `CheckSubmissionStatus_ReturnsFailed_WhenListingIsInactive` — 200 with `INACTIVE` status; asserts `IsFailed: true` with reason containing "INACTIVE".
3. `CheckSubmissionStatus_ReturnsPending_WhenListingNotFound` — 404 response; asserts `IsLive: false, IsFailed: false` (guard rail: 404 is pending, not failed).

Replaced the skeleton `CheckSubmissionStatus_ReturnsPendingStatus` test (net +2 Amazon tests).

### B-3: `EbayMarketplaceAdapter.CheckSubmissionStatusAsync`

Replaced the skeleton implementation with a real eBay Sell API GET call.

**Endpoint:** `GET /sell/inventory/v1/offer/{offerId}`

**Response field mapping:**
- `status == "PUBLISHED"` → `IsLive: true, IsFailed: false`
- `status == "UNPUBLISHED"` → `IsLive: false, IsFailed: true` — **orphaned draft detection**. The offer was created (step 1 of the two-step submit succeeded) but publish failed (step 2). A `LogWarning` with the offerId is emitted for discoverability. The failure reason explicitly identifies this as an "orphaned draft from failed publish step." Cleanup is deferred to a future background sweep — this is a deliberate staged approach, not a bug being swept under the rug.
- Any other status → `IsLive: false, IsFailed: false` (unknown status treated as pending)
- Non-success HTTP status → `IsLive: false, IsFailed: true` with status code and response body

**Auth headers:** `Authorization: Bearer {token}` + `X-EBAY-C-MARKETPLACE-ID: {marketplaceId}` (reuses `GetAccessTokenAsync` with token caching).

**New DTO:** `EbayOfferStatusResponse` (status field), using `[JsonPropertyName]` with existing `JsonOptions` (camelCase).

### B-4: eBay Status Check Tests (3)

1. `CheckSubmissionStatus_ReturnsLive_WhenOfferIsPublished` — 200 with `PUBLISHED` status; asserts `IsLive: true`.
2. `CheckSubmissionStatus_ReturnsFailed_WhenOfferIsUnpublished` — 200 with `UNPUBLISHED` status; asserts `IsFailed: true` with reason containing both "UNPUBLISHED" and the offerId for discoverability.
3. `CheckSubmissionStatus_ReturnsFailed_WhenApiReturnsError` — 500 response; asserts `IsFailed: true` with reason containing "500".

Replaced the skeleton `CheckSubmissionStatus_ReturnsPendingStatus` test (net +2 eBay tests).

---

## Test Helper Changes

No changes to `MarketplaceAdapterTestHelpers.cs` were needed beyond the `SentRequestBodies` addition from Session 1. All new tests use the existing `FakeHttpMessageHandler` queue-based response staging pattern established in M37.0 and refined in M38.0/M38.1.

---

## DoD Verification

| # | Criterion | Status | Notes |
|---|-----------|--------|-------|
| 1 | Walmart `DeactivateListingAsync` fully implemented | ✅ | Session 1: RETIRE_ITEM feed via POST |
| 2 | Walmart deactivation interface design decision documented | ✅ | ADR 0057 |
| 3 | Walmart 3 deactivation tests written | ✅ | Session 1: replaced TODO comments + gap test |
| 4 | `CheckWalmartFeedStatusHandler` publishes `wmrt-{sku}` | ✅ | Session 1: handler fix |
| 5 | Amazon `CheckSubmissionStatusAsync` real GET | ✅ | Session 2: B-1 |
| 6 | eBay `CheckSubmissionStatusAsync` real GET | ✅ | Session 2: B-3 |
| 7 | eBay orphaned draft surfaced as `IsFailed: true` | ✅ | Session 2: B-3 (UNPUBLISHED → IsFailed with warning log) |
| 8 | Build 0 errors; integration tests ≥ 139 | ✅ | 0 errors; 139 tests (98 Marketplaces + 41 Listings) |

**DoD: 8/8 green.**

---

## Build State at Session Close

- **Build:** 0 errors
- **Marketplaces integration tests:** 98 (was 94 at session start; +4 net from replacing 2 skeleton tests with 6 new tests)
- **Listings integration tests:** 41 (unchanged)
- **Total integration tests:** 139
- **E2E scenarios:** 9 active on `@shard-3` (unchanged)
- **CI:** Latest main CI run: #881 (green). This PR's CI will run on merge.

### Test Count Breakdown

| Test File | Count | Change |
|-----------|-------|--------|
| AmazonMarketplaceAdapterTests | 12 | +2 (replaced 1 skeleton with 3 new) |
| EbayMarketplaceAdapterTests | 13 | +2 (replaced 1 skeleton with 3 new) |
| WalmartMarketplaceAdapterTests | 14 | unchanged |
| WalmartPollingHandlerTests | 5 | unchanged |
| AdapterResilienceTests | 5 | unchanged |
| Baseline (handlers, config, etc.) | 49 | unchanged |
| **Marketplaces total** | **98** | **+4** |
| Listings total | 41 | unchanged |
| **Grand total** | **139** | **+4** |

---

## Inherited by M39.x

1. **eBay orphaned draft background sweep** — `CheckSubmissionStatusAsync` now detects UNPUBLISHED offers and surfaces them as failed with a warning log including the offerId. A future background sweep should periodically scan for these orphaned drafts and either delete them or retry the publish step. The detection mechanism is in place; the cleanup mechanism is not.

2. **`SemaphoreSlim` base class extraction** — All three adapters independently implement the same token caching pattern with `SemaphoreSlim` double-check locking. Extraction to a shared base class or utility deferred until a fourth adapter is added or a refactoring pass is scheduled.

3. **Rate limiting / 429 handling** — Polly resilience handles transient failures (retry + circuit breaker) but does not specifically implement marketplace-specific rate limit backing strategies (e.g., Amazon's x-amzn-RateLimit-Limit header, eBay's X-RateLimit-Remaining header). All adapters currently rely on Polly's default retry for 429 responses.

---

## Session 1 Deliverables (Summary)

For complete Session 1 details, see [m38-1-session-1-retrospective.md](./m38-1-session-1-retrospective.md).

- A-1: ADR 0057 — Walmart deactivation identifier design (two-identifier distinction)
- A-2: `CheckWalmartFeedStatusHandler` publishes `wmrt-{Sku}` in `MarketplaceListingActivated`
- A-3: `WalmartMarketplaceAdapter.DeactivateListingAsync` — RETIRE_ITEM feed
- A-4: 3 Walmart deactivation tests
- A-5: `WalmartPollingHandlerTests` assertion update

---

## Architecture Notes

With M38.1 complete, all three marketplace adapters now have fully implemented lifecycle methods:

| Method | Amazon | Walmart | eBay |
|--------|--------|---------|------|
| `SubmitListingAsync` | PUT SP-API Listings Items | POST feed (MP_ITEM) | POST create offer + POST publish |
| `CheckSubmissionStatusAsync` | GET SP-API listing status | Scheduled feed polling | GET offer status |
| `DeactivateListingAsync` | PATCH delete purchasable_offer | POST feed (RETIRE_ITEM) | POST withdraw offer |

The Catalog–Listings–Marketplaces Phase 4 arc that began with M37.0 is now complete. All three phases delivered:
- **M37.0:** Production adapter implementations (submit + auth)
- **M38.0:** Async lifecycle (polling, resilience, deactivation, bidirectional feedback, admin UI)
- **M38.1:** Deactivation fix (Walmart SKU gap) + status verification (Amazon/eBay real GETs)
