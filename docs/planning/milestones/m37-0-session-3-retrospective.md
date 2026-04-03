# M37.0 Session 3 Retrospective — Walmart + eBay Production Adapters

**Date:** 2026-04-03
**Session type:** Phase 3 adapter delivery — implementation
**Duration:** Single session
**CI baseline:** Session 2 CI run #23935973028 (CI ✅), #23935973025 (E2E ✅)

---

## Session 2 CI Confirmation

Session 2's CI run #23935973028 (CI) and #23935973025 (E2E Tests) both completed successfully on branch `copilot/m37-0-session-2-phase-3-kickoff`. Confirmed green before starting Session 3 work.

Baseline at session start: 53 Marketplaces integration tests, 35 Listings tests = 88 total. Build: 0 errors, 12 pre-existing warnings.

---

## Deliverables

### S3-0: Shared Test Double Extraction

- **Location:** `tests/Marketplaces/Marketplaces.Api.IntegrationTests/Helpers/MarketplaceAdapterTestHelpers.cs`
- **Class:** `MarketplaceAdapterTestHelpers` (static class containing three `internal` nested classes)
- **Extracted fakes:**
  - `FakeHttpMessageHandler` — queues responses (FIFO) and records sent requests. Enhanced with URL-keyed response support (`EnqueueResponseForUrl`) for multi-step flows (eBay create-offer → publish-offer)
  - `FakeVaultClient` — in-memory secret dictionary implementing `IVaultClient`
  - `FakeHttpClientFactory` — returns a pre-configured `HttpClient` for any named client
- **Impact:** `AmazonMarketplaceAdapterTests` updated to import from `Helpers.MarketplaceAdapterTestHelpers` via `using static`. All 53 baseline tests continued passing after extraction (pure refactor).

### S3-1: ADR 0053 — Walmart Marketplace API Authentication

- **Location:** `docs/decisions/0053-walmart-marketplace-api-authentication.md`
- **Content:** Client credentials grant flow, token caching strategy, `WM_CONSUMER.ID` header requirement, feed-based submission model, rate limiting (log-and-fail), credential vault paths
- **Key contrast with Amazon:** No refresh token; client_id + client_secret are the only credentials. HTTP Basic auth on token exchange (same as eBay, different from Amazon).

### S3-2: WalmartMarketplaceAdapter

- **Location:** `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs`
- **ChannelCode:** `WALMART_US`
- **Authentication:** OAuth 2.0 client credentials grant
  - Token exchange: `POST https://marketplace.walmartapis.com/v3/token` with `Authorization: Basic {Base64(clientId:clientSecret)}`
  - Token caching: `SemaphoreSlim(1,1)` with 5-minute safety margin (same pattern as Amazon)
- **Vault paths used:** `walmart/client-id`, `walmart/client-secret`, `walmart/seller-id`
- **SubmitListingAsync:** Calls `POST /v3/feeds?feedType=MP_ITEM` with JSON item feed body. Returns feed ID as `ExternalSubmissionId` with `wmrt-` prefix.
- **Required headers:** `WM_SEC.ACCESS_TOKEN`, `WM_CONSUMER.ID`, `WM_SVC.NAME`, `WM_QOS.CORRELATION_ID`
- **Skeleton methods:**
  - `CheckSubmissionStatusAsync` — returns `IsLive: false, IsFailed: false` (M38.x)
  - `DeactivateListingAsync` — returns `false` (future session)

### S3-3: ADR 0054 — eBay Sell API Authentication

- **Location:** `docs/decisions/0054-ebay-sell-api-authentication.md`
- **Content:** Refresh token grant flow, HTTP Basic auth encoding (UTF-8), required scopes (`sell.inventory`), token caching strategy, two-step listing submission, rate limiting, credential vault paths
- **Key contrast with Walmart:** Uses refresh token (like Amazon), not client credentials only. Two-step submission (create offer → publish offer).

### S3-4: EbayMarketplaceAdapter

- **Location:** `src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs`
- **ChannelCode:** `EBAY_US`
- **Authentication:** OAuth 2.0 refresh token grant
  - Token exchange: `POST https://api.ebay.com/identity/v1/oauth2/token` with `Authorization: Basic {Base64(clientId:clientSecret)}` and refresh_token + scope in body
  - Base64 encoding uses `Encoding.UTF8` explicitly per RFC 7617 (security review recommendation)
  - Token caching: `SemaphoreSlim(1,1)` with 5-minute safety margin (same pattern as Amazon/Walmart)
- **Vault paths used:** `ebay/client-id`, `ebay/client-secret`, `ebay/refresh-token`, `ebay/marketplace-id`
- **SubmitListingAsync:** Two-step process:
  1. Create offer: `POST /sell/inventory/v1/offer` → returns `offerId`
  2. Publish offer: `POST /sell/inventory/v1/offer/{offerId}/publish`
  3. Returns offer ID as `ExternalSubmissionId` with `ebay-` prefix
- **Request headers:** `Authorization: Bearer {token}`, `X-EBAY-C-MARKETPLACE-ID` (e.g., `EBAY_US`)
- **Skeleton methods:**
  - `CheckSubmissionStatusAsync` — returns `IsLive: false, IsFailed: false` (M38.x)
  - `DeactivateListingAsync` — returns `false` (future session)

### S3-5: Program.cs Registration Update

- **Location:** `src/Marketplaces/Marketplaces.Api/Program.cs`
- **Change:** The `UseRealAdapters` flag now registers all three real adapters when true:
  - `AmazonMarketplaceAdapter` + `HttpClient("AmazonSpApi")`
  - `WalmartMarketplaceAdapter` + `HttpClient("WalmartApi")`
  - `EbayMarketplaceAdapter` + `HttpClient("EbayApi")`
- When false (default), all three stubs are registered: `StubAmazonAdapter`, `StubWalmartAdapter`, `StubEbayAdapter`
- `IReadOnlyDictionary<string, IMarketplaceAdapter>` resolution by `ChannelCode` is preserved — multiple `IMarketplaceAdapter` registrations are intentional

---

## Auth Flow Observations

### Walmart
- No auth flow surprises. Client credentials grant is the simplest of the three — just `client_id:client_secret` encoded as Basic auth.
- Token TTL is shorter (15 min typical) than Amazon (1 hour) or eBay (2 hours). Under sustained load, Walmart will trigger more frequent token refreshes.
- Walmart requires 4 custom headers per API call (`WM_SEC.ACCESS_TOKEN`, `WM_CONSUMER.ID`, `WM_SVC.NAME`, `WM_QOS.CORRELATION_ID`). This is more header overhead than Amazon or eBay.

### eBay
- The refresh token grant is structurally similar to Amazon LWA but credentials go in the `Authorization: Basic` header (not form body). This is a critical implementation detail.
- The two-step create-then-publish flow adds complexity. If publish fails after create succeeds, an orphaned draft offer exists on eBay. Cleanup is deferred — not in scope for M37.0.
- eBay requires the `sell.inventory` scope in the token exchange request. Amazon doesn't use scopes; Walmart doesn't use scopes.
- The `X-EBAY-C-MARKETPLACE-ID` header is similar in purpose to Walmart's `WM_CONSUMER.ID` but identifies the marketplace (EBAY_US, EBAY_UK) rather than the seller.

---

## UseRealAdapters Flag Status

After Session 3, enabling `Marketplaces:UseRealAdapters` correctly registers all three real adapters:
- ✅ `AmazonMarketplaceAdapter` (Session 2)
- ✅ `WalmartMarketplaceAdapter` (Session 3)
- ✅ `EbayMarketplaceAdapter` (Session 3)

When the flag is false (default for Development/CI), all three stubs are registered. The `IReadOnlyDictionary<string, IMarketplaceAdapter>` resolution by `ChannelCode` works identically with either set.

---

## Test Counts

| BC | Tests at Session Start | Tests at Session Close | Delta |
|----|----------------------|----------------------|-------|
| Marketplaces | 53 | 70 | +17 |
| Listings | 35 | 35 | 0 |
| **Combined** | **88** | **105** | **+17** |

### New Tests (17 total)

**WalmartMarketplaceAdapterTests (8):**
- `ChannelCode_IsWalmartUs`
- `SubmitListing_ReturnsSuccess_WhenApiReturns2xx`
- `SubmitListing_ReturnsFailure_WhenApiReturns4xx`
- `SubmitListing_BuildsCorrectRequest` — verifies `WM_CONSUMER.ID`, `WM_SEC.ACCESS_TOKEN`, `WM_SVC.NAME`, `WM_QOS.CORRELATION_ID` headers
- `SubmitListing_ReturnsFailure_WhenTokenExchangeFails`
- `SubmitListing_CachesToken_AcrossMultipleCalls`
- `CheckSubmissionStatus_ReturnsPendingStatus`
- `DeactivateListing_ReturnsFalse`

**EbayMarketplaceAdapterTests (9):**
- `ChannelCode_IsEbayUs`
- `SubmitListing_ReturnsSuccess_WhenBothStepsSucceed` — mocks create-offer + publish-offer
- `SubmitListing_ReturnsFailure_WhenCreateOfferFails`
- `SubmitListing_ReturnsFailure_WhenPublishOfferFails` — create succeeds, publish fails
- `SubmitListing_BuildsCorrectAuthHeader` — verifies `Authorization: Basic` is correctly base64-encoded
- `SubmitListing_ReturnsFailure_WhenTokenExchangeFails`
- `SubmitListing_CachesToken_AcrossMultipleCalls`
- `CheckSubmissionStatus_ReturnsPendingStatus`
- `DeactivateListing_ReturnsFalse`

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 12 (pre-existing only — NU1504 duplicate package refs, CS0219 unused variables in Correspondence, CS8602 null deref in Backoffice.Web). No new warnings introduced.
- **CI run:** Pending (PR push will trigger CI)

---

## What Session 4 Should Pick Up

Session 4 is the M37.0 milestone closure session:

1. **CONTEXTS.md update** — Add Walmart and eBay adapter references alongside Amazon in the Marketplaces BC section
2. **CURRENT-CYCLE.md milestone move** — Move M37.0 from Active to Recent Completions
3. **CI baseline recording** — Confirm Session 3 CI is green and record run number
4. **M38.x pre-planning notes** — Document what M38.x should tackle:
   - Async submission status polling infrastructure (Wolverine `bus.ScheduleAsync()` polling saga)
   - Rate limiting retry logic (Polly policies or delegating handlers)
   - Bidirectional marketplace feedback (Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`)
   - Orphaned eBay draft offer cleanup
5. **Final housekeeping** — Review all stub adapters for consistency, ensure test coverage ratios are healthy

---

## Files Changed

### New Files
- `docs/decisions/0053-walmart-marketplace-api-authentication.md` — ADR for Walmart auth patterns
- `docs/decisions/0054-ebay-sell-api-authentication.md` — ADR for eBay auth patterns
- `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs` — Walmart production adapter
- `src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs` — eBay production adapter
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/Helpers/MarketplaceAdapterTestHelpers.cs` — Shared test doubles
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/WalmartMarketplaceAdapterTests.cs` — Walmart adapter tests
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/EbayMarketplaceAdapterTests.cs` — eBay adapter tests
- `docs/planning/milestones/m37-0-session-3-retrospective.md` — This file

### Modified Files
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/AmazonMarketplaceAdapterTests.cs` — Updated to use shared test doubles (S3-0 extraction)
- `src/Marketplaces/Marketplaces.Api/Program.cs` — `UseRealAdapters` now registers all 3 real adapters
- `docs/planning/CURRENT-CYCLE.md` — Session 3 progress block
