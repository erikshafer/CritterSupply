# M38.0 Session 2 Retrospective

**Date:** 2026-04-03
**Milestone:** M38.0 — Marketplaces Phase 4: Async Lifecycle + Resilience
**Session:** Session 2 — Polly Resilience + DeactivateListingAsync

---

## Baseline (Session Start)

- Build: 0 errors, 16 warnings (all pre-existing)
- Marketplaces integration tests: 79 (Session 1 close)
- Listings integration tests: 41 (Session 1 close)
- Combined: 120 tests

---

## Items Completed

| Item | Description |
|------|-------------|
| P-12 | ADR 0056 committed — Marketplace adapter resilience patterns |
| P-8 | Polly resilience pipelines on AmazonSpApi, WalmartApi, EbayApi HttpClients in `Marketplaces.Api/Program.cs` |
| P-8 tests | 9 resilience tests (429 retry, 5xx retry, 401 no-retry × 3 adapters) in `AdapterResilienceTests.cs` |
| P-9 | `WalmartMarketplaceAdapter.DeactivateListingAsync` — Option A (log SKU gap, return false) |
| P-9 tests | Replaced `DeactivateListing_ReturnsFalse` skeleton with `DeactivateListing_ReturnsFalse_DueToSkuGap` + TODO comments |
| P-10 | `AmazonMarketplaceAdapter.DeactivateListingAsync` — PATCH delete purchasable_offer |
| P-10 tests | 3 tests replacing skeleton: `ReturnsTrue_WhenApiSucceeds`, `ReturnsFalse_WhenApiFails`, `BuildsCorrectRequest` |
| P-11 | `EbayMarketplaceAdapter.DeactivateListingAsync` — POST offer withdraw |
| P-11 tests | 3 tests replacing skeleton: `ReturnsTrue_WhenApiSucceeds`, `ReturnsFalse_WhenApiFails`, `BuildsCorrectRequest` |

---

## P-8: Polly Resilience Pipelines

**Registration location:** `src/Marketplaces/Marketplaces.Api/Program.cs`, inside the `if (useRealAdapters)` block.

**Package added:** `Microsoft.Extensions.Http.Resilience` added to `Marketplaces.Api.csproj`. The package
was already in `Directory.Packages.props` at version 10.4.0 (used by `CritterSupply.ServiceDefaults`);
only the `<PackageReference>` entry in `Marketplaces.Api.csproj` was new.

**Registration pattern:**

```csharp
builder.Services.AddHttpClient("AmazonSpApi")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
    .AddResilienceHandler("amazon-resilience", pipeline =>
    {
        pipeline.AddRetry(retryOptions);
        pipeline.AddCircuitBreaker(circuitBreakerOptions);
    });
```

The same shared `retryOptions` and `circuitBreakerOptions` instances are referenced by all three
`AddResilienceHandler` calls. This is safe because `HttpRetryStrategyOptions` and
`HttpCircuitBreakerStrategyOptions` are configuration objects, not stateful; the Polly pipeline
instances themselves are per-`HttpClient`.

**Retry strategy:**
- `MaxRetryAttempts`: 3
- `BackoffType`: `DelayBackoffType.Exponential`
- `Delay`: 1 second (base; 1s → 2s → 4s on retries)
- `ShouldHandle`: default `HttpRetryStrategyOptions` default — covers 408, 429, 500+ and
  `HttpRequestException`; **excludes 401 by default**

**Circuit breaker:**
- `FailureRatio`: 0.5 (50% failure rate required to open)
- `MinimumThroughput`: 5 (minimum requests within the sampling window before breaker can open)
- `SamplingDuration`: 30 seconds
- `BreakDuration`: 30 seconds

**Explicit 30s timeout:** `ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))` replaces
the default 100s. Each of the three named `HttpClient` registrations sets this.

**Per-adapter scope:** Each `AddResilienceHandler` call attaches to an individual named `HttpClient`,
so the circuit breaker state is completely isolated between `AmazonSpApi`, `WalmartApi`, and `EbayApi`.
A Walmart API outage does not affect the Amazon or eBay breakers.

### The 401 Special Case (ADR 0056 Guard Rail 2)

The `HttpRetryStrategyOptions` default `ShouldHandle` predicate covers HTTP 408, 429, 500+ and
`HttpRequestException`. **HTTP 401 (Unauthorized) is not in the default set.** This means:

- A 401 from the token exchange endpoint (invalid credentials): Polly does NOT retry. The adapter
  propagates the failure immediately. This is correct — retrying on credential errors would only
  consume vault reads.

- A 401 from an API endpoint (expired token): Polly does NOT retry. The adapter itself does not
  implement automatic 401 retry with token refresh (that is a future enhancement); it logs and
  returns failure. This is correct for the current implementation.

If a future enhancement adds 401 → token-cache-clear → retry logic inside the adapter, that logic
should live in the adapter's API call method, not in the Polly pipeline. Polly retrying a 401 would
use the same stale token on the retry attempt (Polly does not mutate the request between retries).

### Polly v7 vs v8 API Note

The problem statement referenced `HandledEventsAllowedBeforeBreaking`, which is the Polly v7 API.
The repo uses Polly.Core 8.6.5 via `Microsoft.Extensions.Http.Resilience` 10.4.0. The Polly v8
circuit breaker uses:
- `FailureRatio` (proportion of failures that triggers break)
- `MinimumThroughput` (minimum requests before ratio is evaluated)
- `SamplingDuration` (rolling window)
- `BreakDuration`

The `HandledEventsAllowedBeforeBreaking` count-based approach maps to setting `FailureRatio = 1.0`
(100% failure rate) with `MinimumThroughput = N`. Session 2 chose the more typical
`FailureRatio = 0.5 / MinimumThroughput = 5` combination, which opens the breaker after 50% of
5+ requests fail in the sampling window. This is more appropriate for marketplace adapters that
may see intermittent failures mixed with successes.

---

## P-9: Walmart `DeactivateListingAsync` — Option A (Architectural Gap)

**Decision:** Option A — log the gap, return `false`, document the limitation.

**Why the gap exists:** The `externalListingId` parameter is in `wmrt-{feedId}` format — the
Walmart submission feed ID from the original `MP_ITEM` feed submission. A Walmart
`RETIRE_ITEM` feed requires the item **SKU** (e.g., "CritterKibble-001"), not the feed ID.
The feed ID cannot be reverse-mapped to the SKU at the adapter level without a data store lookup.

**Design decision (ADR 0056):** The caller — a future `ListingEndedHandler` in Marketplaces BC —
must provide the SKU when triggering deactivation, either by passing it as a separate parameter to
the interface method or by encoding the SKU in the `externalListingId` at submission time. This
design question is deferred to Session 3 / M38.1.

The implementation logs a `Warning`-level message explaining the gap and the required path to
resolution, then returns `false`.

---

## P-10: Amazon `DeactivateListingAsync`

**Endpoint:** `PATCH /listings/2021-08-01/items/{sellerId}/{sku}?marketplaceIds={marketplaceId}`

**Prefix stripping:** `externalListingId` is `amzn-{sku}`. The `"amzn-"` prefix is stripped:
```csharp
var sku = externalListingId.StartsWith("amzn-", StringComparison.OrdinalIgnoreCase)
    ? externalListingId["amzn-".Length..]
    : externalListingId;
```

**Request body (patches array):**
```json
{
  "productType": "PRODUCT",
  "patches": [
    { "op": "delete", "path": "/attributes/purchasable_offer", "value": [] }
  ]
}
```

The `op: delete` on `purchasable_offer` removes the listing from the marketplace without deleting
the ASIN. `sellerId` and `marketplaceId` are retrieved from `IVaultClient` (same vault paths as
`SubmitListingAsync`). Returns `true` on 2xx, `false` on 4xx/5xx with logging. Exception pattern
matches `SubmitListingAsync` (`catch (Exception ex) when (ex is not OperationCanceledException)`).

---

## P-11: eBay `DeactivateListingAsync`

**Endpoint:** `POST /sell/inventory/v1/offer/{offerId}/withdraw`

**Prefix stripping:** `externalListingId` is `ebay-{offerId}`. The `"ebay-"` prefix is stripped:
```csharp
var offerId = externalListingId.StartsWith("ebay-", StringComparison.OrdinalIgnoreCase)
    ? externalListingId["ebay-".Length..]
    : externalListingId;
```

**Key headers:** `Authorization: Bearer {accessToken}`, `X-EBAY-C-MARKETPLACE-ID: {marketplaceId}`.
No request body (POST to `{offerId}/withdraw` with no body is the correct eBay API pattern).

A successful `200 OK` response contains a `listingId` in the body. The withdrawn offer is NOT
deleted — it can be republished later. This is the correct approach for listing pause/end.

`marketplaceId` retrieved from vault at `ebay/marketplace-id`. Reuses the cached access token from
`GetAccessTokenAsync`. Exception pattern matches `SubmitListingAsync`.

---

## Tests: Which Skeletons Were Replaced

| File | Replaced | Replacement |
|------|----------|-------------|
| `WalmartMarketplaceAdapterTests.cs` | `DeactivateListing_ReturnsFalse` | `DeactivateListing_ReturnsFalse_DueToSkuGap` + TODO comments for 2 blocked tests |
| `AmazonMarketplaceAdapterTests.cs` | `DeactivateListing_ReturnsFalse` | 3 tests: `ReturnsTrue_WhenApiSucceeds`, `ReturnsFalse_WhenApiFails`, `BuildsCorrectRequest` |
| `EbayMarketplaceAdapterTests.cs` | `DeactivateListing_ReturnsFalse` | 3 tests: `ReturnsTrue_WhenApiSucceeds`, `ReturnsFalse_WhenApiFails`, `BuildsCorrectRequest` |

### Resilience Tests (`AdapterResilienceTests.cs`)

The resilience tests build each adapter with a real Polly pipeline (same retry config as
`Program.cs`) using `IHttpClientFactory` from a `ServiceCollection` with
`AddResilienceHandler()`. The primary handler is overridden with `FakeHttpMessageHandler` via
`ConfigurePrimaryHttpMessageHandler()`. Retry delay is set to `TimeSpan.FromMilliseconds(1)`
(constant backoff) for test speed.

Three scenarios per adapter (9 tests total):
1. `RetriesOnTooManyRequests_ThenSucceeds` — stage 429 → 200; verify success + correct request count
2. `RetriesOnInternalServerError_ThenSucceeds` — stage 500 → 200; verify success + correct request count
3. `DoesNotRetryOn401` — stage 401; verify failure + exactly 2 requests (1 token + 1 API, no retry)

The `SentRequests` count assertion is the key verification: if Polly incorrectly retried a 429,
we'd see more requests than staged. If Polly incorrectly retried a 401, we'd see more requests
than the expected 2.

### Walmart Deactivation Test Gap

`DeactivateListing_ReturnsTrue_WhenRetireFeedSucceeds` is not yet implemented (Option A gap).
Two TODO comments in `WalmartMarketplaceAdapterTests.cs` document the 3 tests that will be added
in M38.1 once the SKU delivery mechanism is designed. Net test count impact: Walmart contributes
net 0 to the deactivation test total (1 replacement for 1 skeleton).

---

## Test Counts

| BC | Session Start | Session Close | Delta |
|----|--------------|---------------|-------|
| Marketplaces | 79 | 92 | +13 |
| Listings | 41 | 41 | 0 |
| **Combined** | **120** | **133** | **+13** |

Test count breakdown:
- 9 new resilience tests (`AdapterResilienceTests.cs`)
- +2 net Amazon deactivation (3 new, 1 skeleton replaced)
- +2 net eBay deactivation (3 new, 1 skeleton replaced)
- +0 net Walmart deactivation (1 gap test replaces 1 skeleton)

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 16 (all pre-existing from Session 1)
- **Test result:** 92 Marketplaces / 41 Listings = 133 total, 0 failures

---

## Session Learnings

### 1. Polly v7 vs v8 API Drift

The `HandledEventsAllowedBeforeBreaking` property referenced in the problem statement is Polly v7
API. Polly v8 (`Polly.Core` 8.x, used via `Microsoft.Extensions.Http.Resilience` 10.x) replaces
the count-based circuit breaker with a ratio-based model: `FailureRatio` + `MinimumThroughput` +
`SamplingDuration`. Future sessions touching circuit breaker configuration should verify against
the actual installed Polly.Core version before writing code.

### 2. `HttpRetryStrategyOptions` Default ShouldHandle Already Excludes 401

The default `HttpRetryStrategyOptions.ShouldHandle` covers 408, 429, 500+ — **not** 401. No
custom predicate is needed to exclude `Unauthorized` from retry. This means the ADR 0056 guard
rail (401 not retried by Polly) is satisfied by the defaults without a custom lambda.

This simplifies the `Program.cs` registration significantly compared to the custom-predicate
approach outlined in the problem statement.

### 3. Resilience Tests Require Real DI Pipeline, Not FakeHttpClientFactory

`FakeHttpClientFactory` bypasses the Polly pipeline entirely — it returns a plain `HttpClient`
wrapping `FakeHttpMessageHandler`. Resilience tests that need to verify retry behavior must build
a `ServiceCollection` with `AddHttpClient(...).ConfigurePrimaryHttpMessageHandler(...).AddResilienceHandler(...)`
and resolve `IHttpClientFactory` from the built container. This is the correct test pattern for
`Microsoft.Extensions.Http.Resilience`-based configurations.

### 4. Walmart Deactivation SKU Gap is an Interface Design Issue

The `IMarketplaceAdapter.DeactivateListingAsync(string externalListingId, ...)` signature carries
only the external ID, which for Walmart is the feed ID from submission — not a SKU. Walmart
item retirement requires a SKU. This is not a bug in the adapter code; it's an interface design
limitation that affects only Walmart (Amazon's `externalListingId` encodes the SKU; eBay's
encodes the offer ID; both are sufficient for deactivation).

The fix in M38.1 should either:
- Change the interface to accept additional context (e.g., `string sku`), or
- Change the Walmart submission to use the SKU as the `externalListingId` (store as `wmrt-{sku}`)
  and use the feed ID only for polling

Option B is cleaner: it keeps the `DeactivateListingAsync` interface stable and makes the Walmart
`externalListingId` semantically consistent with Amazon (`amzn-{sku}`) and potentially eBay
(`ebay-{offerId}`). The polling handler already has the feed ID separately in `CheckWalmartFeedStatus`.

---

## What Session 3 Should Pick Up

Session 3 targets **P-13** and **P-14** from the M38.0 plan:

- **P-13:** Listings admin action buttons (approve/pause/end) on the Backoffice detail page —
  currently rendered as disabled stubs. Requires wiring the Blazor WASM `approve`/`pause`/`end`
  button actions to call the Listings API.

- **P-14:** Unblock the 3 `@wip` E2E scenarios in `ListingsDetail.feature` (action button flows).
  Currently blocked because the buttons are disabled stubs. Once P-13 is complete, these scenarios
  become runnable. Requires the `@shard-N` tag on line 1 of the feature file (see M38.0 plan
  and the E2E shard tag convention stored in agent memory).

- **Walmart SKU gap (M38.1 candidate):** The deactivation caller handler (`ListingEndedHandler` in
  Marketplaces BC that reacts to `ListingEnded` from Listings BC) is not in Session 3 scope per
  the M38.0 plan, but the Walmart interface design decision (encode SKU vs. pass separately) should
  be made before that handler is built.
