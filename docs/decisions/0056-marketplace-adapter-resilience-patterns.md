# ADR 0056 — Marketplace Adapter Resilience Patterns

**Status:** Accepted
**Date:** 2026-04-03
**Milestone:** M38.0 Session 2

---

## Context

M37.0 delivered three production marketplace adapters (Amazon, Walmart, eBay), each using a named
`HttpClient` registered via `IHttpClientFactory`. ADRs 0052–0054 each included a "Rate Limiting —
Log and Fail" section documenting that transient failures (HTTP 429, 5xx) were logged and returned
as failures with no automatic retry, and explicitly deferred retry and circuit breaker
implementation to M38.x.

M38.0 Session 2 delivers that deferred resilience layer. All three real adapter `HttpClient`
pipelines now have:

1. **Retry** — exponential backoff on HTTP 429 and 5xx responses
2. **Circuit breaker** — per-adapter breaker to prevent hammering an unresponsive marketplace API
3. **Explicit timeout** — a 30-second per-request timeout replacing the default 100-second Polly
   no-op

The resilience layer uses `Microsoft.Extensions.Http.Resilience`, the idiomatic .NET 10 wrapper
over Polly that integrates directly with `IHttpClientFactory` via the `AddResilienceHandler()`
extension method. This is the same package already present in `Directory.Packages.props` (used by
`CritterSupply.ServiceDefaults`).

---

## Decisions

### 1. `Microsoft.Extensions.Http.Resilience` over raw Polly

The `Microsoft.Extensions.Http.Resilience` package (formerly `Polly.Extensions.Http`) provides:

- Direct `IHttpClientBuilder.AddResilienceHandler()` extension — attaches the pipeline at the
  `HttpMessageHandler` layer, meaning it intercepts all requests through the named `HttpClient`
  without requiring changes to the adapter code
- `HttpRetryStrategyOptions` and `HttpCircuitBreakerStrategyOptions` typed specifically for HTTP
  responses
- Integration with `ILogger` and OpenTelemetry via the .NET resilience pipeline infrastructure
- Already present in `Directory.Packages.props` at `10.4.0`

Raw Polly v8 could accomplish the same result but requires manual wiring into the `HttpClient`
pipeline; the `Microsoft.Extensions.Http.Resilience` abstraction is the idiomatic approach for
ASP.NET Core projects on .NET 10.

### 2. Per-adapter circuit breaker scope

Each named `HttpClient` (`AmazonSpApi`, `WalmartApi`, `EbayApi`) has its own independent circuit
breaker. This is the natural consequence of using `AddResilienceHandler()` per `AddHttpClient()`
registration: the breaker state is scoped to the pipeline instance, not shared globally.

**Why per-adapter is correct:** A Walmart API outage should not prevent Amazon or eBay submissions
from succeeding. A global or cross-adapter circuit breaker would be incorrectly coarse-grained for
three independent external services.

### 3. Retry strategy — uniform exponential backoff, 3 attempts, HTTP 429 + 5xx only

All three adapters use the same retry configuration:

| Parameter | Value |
|-----------|-------|
| `MaxRetryAttempts` | 3 |
| `BackoffType` | `DelayBackoffType.Exponential` |
| `Delay` (base) | 1 second |
| Retry triggers | HTTP 429, HTTP 5xx (500, 502, 503, 504), `HttpRequestException` |
| Non-retry codes | HTTP 4xx (including 401, 400, 403, 404) |

The per-adapter rate limits documented in ADRs 0052–0054:

| Adapter | Rate limit | Burst |
|---------|-----------|-------|
| Amazon SP-API (`putListingsItem`) | 5 req/s | 10 |
| Walmart Marketplace API | ~10 req/s | — |
| eBay Sell Inventory API | varies by endpoint; `sell.inventory` POST/PUT ~500 req/day | — |

A uniform 1-second exponential base (attempts at ~1s, ~2s, ~4s) is conservative for all three
rate limits. Per-adapter tuning (e.g., shorter delay for Walmart's higher rate limit) is deferred
to M38.1 if operational monitoring reveals unnecessary latency.

### 4. The 401 special case — retry predicate explicitly excludes `Unauthorized`

**HTTP 401 is NOT retried by the Polly pipeline.** This is a critical design constraint.

The Polly pipeline wraps ALL requests through the named `HttpClient`, including the token exchange
POST (`POST /v3/token`, `POST /auth/o2/token`, `POST /identity/v1/oauth2/token`). The two
contexts where a 401 can appear are:

1. **Token endpoint 401** — invalid `client_id`, `client_secret`, or `refresh_token`. This is a
   configuration error, not a transient failure. Retrying would burn vault reads and token
   exchange calls against permanently invalid credentials.

2. **API endpoint 401** — expired access token. This is a transient auth failure that the adapter
   handles by clearing the token cache and re-entering `GetAccessTokenAsync()`. Retrying at the
   Polly layer would call the endpoint again with the same stale token (Polly does not modify the
   request between retries), which would produce a second 401 before the token cache clears.

**Resolution:** The `HttpRetryStrategyOptions` default `ShouldHandle` predicate covers HTTP 408,
429, and 500+ status codes plus `HttpRequestException`. **HTTP 401 is not in the default set**,
so no custom predicate is required. The default behavior is used in `Program.cs` without
overriding `ShouldHandle`.

The adapter's own 401 handling on API calls (clearing the token cache and re-fetching) runs in
the adapter's own call path before the response propagates to Polly. This means a 401 from an
API endpoint triggers the adapter's token refresh exactly once, then the retried call proceeds
with a fresh token — Polly's circuit breaker does not count the 401 as a failure toward the
breaker threshold.

### 5. Circuit breaker configuration

The `HttpCircuitBreakerStrategyOptions` uses the Polly v8 ratio-based circuit breaker
(`Polly.Core` 8.x) — not the Polly v7 count-based API.

| Parameter | Value | Description |
|-----------|-------|-------------|
| `FailureRatio` | 0.5 | Break when ≥50% of requests fail within `SamplingDuration` |
| `MinimumThroughput` | 5 | Minimum requests in `SamplingDuration` before ratio is evaluated |
| `SamplingDuration` | 30 seconds | Rolling window for failure ratio calculation |
| `BreakDuration` | 30 seconds | How long the circuit stays open before allowing a test request |

The circuit opens when 50% or more of the last 5+ requests (within 30 seconds) return a handled
failure (5xx or `HttpRequestException`). The 30-second break duration gives a marketplace API time
to recover from a brief outage. After the break, the circuit enters half-open state and allows a
single test request before fully closing.

Handled events use the default `HttpCircuitBreakerStrategyOptions.ShouldHandle` predicate, which
covers 5xx responses and `HttpRequestException`. A 401 does not count toward the breaker threshold.

### 6. Explicit 30-second timeout

Each `AddHttpClient()` registration sets:

```csharp
.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
```

This replaces the default 100-second `HttpClient.Timeout`, which is far too permissive for
marketplace API calls. A 30-second timeout is consistent with the `docs/skills/httpclient.md`
recommendation and ensures that a hung marketplace API connection does not hold a thread for
nearly two minutes.

The `TimeoutRejectedException` propagated by the timeout is caught by the adapter's `catch`
clause (`when ex is not OperationCanceledException`) — it surfaces as a failure result with an
appropriate error message.

---

## What This ADR Does Not Cover

- **Amazon and eBay `CheckSubmissionStatusAsync` real implementations** — both are still skeleton
  returns deferred to M38.1. The resilience pipeline wraps the named `HttpClient` and will apply
  to those calls once implemented.

- **Deactivation caller wiring** — P-9/P-10/P-11 implement `DeactivateListingAsync` on the
  adapters but do not add a caller handler. The handler that reacts to `ListingEnded` or
  `ListingPaused` events from the Listings BC to trigger deactivation is a Session 3 or M38.1
  candidate (see M38.0 plan Section 2, Q4).

- **Walmart deactivation gap** — `WalmartMarketplaceAdapter.DeactivateListingAsync` receives
  only `externalListingId` (format: `wmrt-{feedId}`). A Walmart RETIRE_ITEM feed requires the
  item SKU, not the submission feed ID. The SKU is not available in the `externalListingId` at
  the adapter level. For M38.0, the implementation logs the architectural gap and returns `false`.
  The caller design (a future `ListingEndedHandler` in Marketplaces BC) must pass the SKU or
  encode it in the external listing ID. This will be resolved when the deactivation trigger
  handler is added in Session 3 or M38.1.

- **Per-adapter `Retry-After` header handling** — eBay returns a `Retry-After` header on 429
  responses. Respecting this header requires a custom `OnRetry` delegate. Deferred to M38.1.

---

## Consequences

- All transient failures on real adapter API calls (429, 5xx, network errors) are automatically
  retried up to 3 times with exponential backoff before surfacing as a `SubmissionResult` failure.
- Sustained outages open the circuit breaker per-adapter, failing fast for 30 seconds and
  preventing queue buildup during marketplace downtime.
- Integration tests for resilience use `FakeHttpMessageHandler` to stage error responses and
  verify retry behavior without real network calls.
- The adapter code is unchanged by P-8 — all resilience configuration is in `Program.cs`.
- Stub adapters (`StubAmazonAdapter`, `StubWalmartAdapter`, `StubEbayAdapter`) are not affected;
  they do not use `IHttpClientFactory` and are not registered under `UseRealAdapters`.
