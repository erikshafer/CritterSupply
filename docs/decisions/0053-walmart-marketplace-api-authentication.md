# ADR 0053: Walmart Marketplace API Authentication and Rate Limiting Patterns

**Status:** ✅ Accepted

**Date:** 2026-04-03

**Context:**

The `WalmartMarketplaceAdapter` is the second production `IMarketplaceAdapter` implementation in CritterSupply (M37.0 Session 3). It integrates with Walmart's Marketplace API to submit product listings to the Walmart US marketplace via the Item feed endpoint. This ADR documents the authentication flow, token management strategy, required request headers, rate limiting approach, and how the patterns differ from the Amazon SP-API adapter (ADR 0052).

Walmart Marketplace API authentication uses a standard OAuth 2.0 **client credentials** grant — simpler than Amazon's refresh token grant because no long-lived refresh token is involved. The client exchanges its client_id and client_secret directly for an access token.

**Decisions:**

### 1. OAuth 2.0 Client Credentials Grant Flow

The `WalmartMarketplaceAdapter` authenticates using the client credentials grant:

```
POST https://marketplace.walmartapis.com/v3/token
Content-Type: application/x-www-form-urlencoded
Authorization: Basic {Base64(client_id:client_secret)}

grant_type=client_credentials
```

This returns an access token (typically 15-minute TTL) used as the `WM_SEC.ACCESS_TOKEN` header on API calls.

**Key difference from Amazon (ADR 0052):** No refresh token is involved. The client_id and client_secret are the only credentials needed. The token exchange uses HTTP Basic auth (Base64-encoded `client_id:client_secret` in the Authorization header), not form-body parameters like Amazon LWA.

### 2. Token Caching Strategy

The `WalmartMarketplaceAdapter` caches the access token in memory with a 5-minute safety margin before the actual token expiry — the same `SemaphoreSlim` + double-check pattern established in `AmazonMarketplaceAdapter`:

- Token TTL from Walmart response: typically 900 seconds (15 minutes)
- Cache expiry: `token_ttl - 300 seconds` = effective 10-minute cache
- Thread-safe via `SemaphoreSlim` with double-check pattern after lock acquisition
- On token refresh, `IVaultClient.GetSecretAsync` is called for 2 credentials (client-id, client-secret)

**Why the same pattern:** Token caching is a cross-cutting concern for all marketplace adapters. Using an identical caching mechanism (in-memory with SemaphoreSlim) keeps the adapter implementations consistent and avoids introducing new concurrency patterns.

### 3. Required Request Headers

Walmart Marketplace API requires several custom headers on every API call:

| Header | Value | Source |
|--------|-------|--------|
| `WM_SEC.ACCESS_TOKEN` | OAuth access token | Token exchange response |
| `WM_CONSUMER.ID` | Seller/consumer ID | `IVaultClient: walmart/seller-id` |
| `WM_SVC.NAME` | Service name identifier | `"Walmart Marketplace"` (constant) |
| `WM_QOS.CORRELATION_ID` | Request correlation ID | `Guid.NewGuid()` per request |

The `WM_CONSUMER.ID` header identifies the seller account making the API call. Unlike Amazon (where seller ID is a URL path parameter), Walmart passes it as a request header.

### 4. Feed-Based Listing Submission

Walmart uses a feed-based submission model for item data:

```
POST https://marketplace.walmartapis.com/v3/feeds?feedType=MP_ITEM
Content-Type: application/json
```

The `SubmitListingAsync` method submits an item feed and returns the feed ID as `ExternalSubmissionId` (prefixed with `wmrt-` to match the `StubWalmartAdapter` convention). The feed is processed asynchronously by Walmart — actual item activation status requires polling the feed status endpoint, which is deferred to M38.x per D-3.

### 5. Rate Limiting — Log and Fail (Not Yet Implemented)

Walmart Item API allows approximately 10 requests per second. When rate-limited, Walmart returns HTTP 429.

**Current behavior (M37.0):** The adapter logs 429 responses as failures and returns `SubmissionResult.IsSuccess: false`. No automatic retry.

**Future enhancement (M38.x):** Implement exponential backoff with jitter on 429 responses, using either a delegating handler on the `HttpClient` pipeline or a Polly retry policy.

### 6. Credential Storage — Vault Path Conventions

All Walmart credentials follow the ADR 0051 vault path conventions:

| Credential | Vault Path | Environment Variable |
|-----------|-----------|---------------------|
| Client ID | `walmart/client-id` | `VAULT__WALMART__CLIENT_ID` |
| Client Secret | `walmart/client-secret` | `VAULT__WALMART__CLIENT_SECRET` |
| Seller ID | `walmart/seller-id` | `VAULT__WALMART__SELLER_ID` |

No credentials are hardcoded, read from `IConfiguration`, or passed as constructor parameters. All credential access goes through `IVaultClient.GetSecretAsync`.

### 7. Skeleton Methods

`CheckSubmissionStatusAsync` and `DeactivateListingAsync` are skeleton implementations in M37.0:

- `CheckSubmissionStatusAsync`: Returns `IsLive: false, IsFailed: false` — real feed status polling deferred to M38.x (D-3)
- `DeactivateListingAsync`: Returns `false` — feed-based retirement deferred to a future session

**Rationale:**

The client credentials grant is Walmart's standard OAuth flow for Marketplace API integrations. Unlike Amazon's refresh token flow, there is no long-lived refresh token to manage — the adapter simply re-authenticates with client_id and client_secret when the access token expires. The token caching strategy mirrors the Amazon adapter to maintain implementation consistency.

The feed-based submission model means `SubmitListingAsync` is inherently asynchronous — the feed ID returned is a handle for later status polling, not a confirmation of listing activation. This aligns with the deferred polling decision (D-3) and the `ExternalSubmissionId` design in `IMarketplaceAdapter`.

**Consequences:**

- Developers testing against real Walmart Marketplace API must configure 3 environment variables per ADR 0051
- The token cache is process-local; shorter TTL (15 min vs Amazon's 1 hour) means more frequent token refreshes under sustained load
- 429 errors from Walmart API are returned as submission failures until retry logic is implemented
- Feed submission is fire-and-forget until polling infrastructure is built in M38.x

**Related Decisions:**

- ADR 0050 — Marketplaces ProductSummaryView ACL (defines the `ListingApprovedHandler` → adapter flow)
- ADR 0051 — Vault Implementation Strategy (defines credential retrieval and path conventions)
- ADR 0052 — Amazon SP-API Authentication Patterns (establishes the token caching pattern reused here)
