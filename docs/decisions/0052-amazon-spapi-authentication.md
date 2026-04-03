# ADR 0052: Amazon SP-API Authentication and Rate Limiting Patterns

**Status:** ✅ Accepted

**Date:** 2026-04-03

**Context:**

The `AmazonMarketplaceAdapter` is the first production `IMarketplaceAdapter` implementation in CritterSupply (M37.0 Session 2). It integrates with Amazon's Selling Partner API (SP-API) to submit product listings to the Amazon US marketplace. This ADR documents the authentication flow, token management strategy, rate limiting approach, and the patterns that future marketplace adapters (Walmart, eBay) should follow.

Amazon SP-API authentication evolved significantly since the older MWS (Marketplace Web Services) API was deprecated. The current model uses Login with Amazon (LWA) OAuth 2.0 tokens for API authorization, with optional AWS Signature V4 signing for certain restricted endpoints.

**Decisions:**

### 1. LWA OAuth 2.0 Token Exchange Flow

The `AmazonMarketplaceAdapter` authenticates using the LWA Refresh Token grant:

```
POST https://api.amazon.com/auth/o2/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&client_id={from IVaultClient: amazon/client-id}
&client_secret={from IVaultClient: amazon/client-secret}
&refresh_token={from IVaultClient: amazon/refresh-token}
```

This returns an access token (typically 1-hour TTL) used as:
- `Authorization: Bearer {access_token}` header
- `x-amz-access-token: {access_token}` header (required by SP-API)

**Why refresh token grant:** SP-API applications authorized via Seller Central receive a long-lived refresh token. The adapter exchanges this for short-lived access tokens at runtime. This is the standard SP-API auth model for self-authorized applications.

### 2. AWS Signature V4 — Not Required for Listings Items API

The SP-API Listings Items API (`PUT /listings/2021-08-01/items/{sellerId}/{sku}`) does **not** require AWS Signature V4 signing. AWS Sig V4 was previously required for all SP-API calls when the API used IAM-based auth, but Amazon migrated to bearer-token-only auth for most endpoints in 2023.

Some restricted data endpoints (e.g., Finances API, certain Reports API calls) may still require additional authorization. If a future adapter method needs restricted data, AWS Sig V4 signing should be added at that point — not preemptively in M37.0.

### 3. Token Caching Strategy

The `AmazonMarketplaceAdapter` caches the LWA access token in memory with a 5-minute safety margin before the actual token expiry:

- Token TTL from LWA response: typically 3600 seconds (1 hour)
- Cache expiry: `token_ttl - 300 seconds` = effective 55-minute cache
- Thread-safe via `SemaphoreSlim` with double-check pattern after lock acquisition
- On token refresh, `IVaultClient.GetSecretAsync` is called for credentials (3 vault reads per refresh)

**Why in-memory caching:** The adapter is registered as a singleton in DI. In-memory caching avoids:
- Repeated vault reads on every HTTP request (security-positive: fewer secret retrievals)
- LWA rate limiting on token endpoint (Amazon enforces per-application rate limits)

**Tradeoff:** If the process restarts, the token is lost and re-acquired on next request. This is acceptable for a reference architecture; production deployments with multiple instances could use a distributed cache (Redis) but this is out of scope.

### 4. Rate Limiting — Retry Strategy (Not Yet Implemented)

SP-API uses per-operation rate limits with burst quotas. The Listings Items API `putListingsItem` operation has:
- Rate: 5 requests per second
- Burst: 10 requests

When rate-limited, SP-API returns HTTP 429 with `x-amzn-RateLimit-Limit` header indicating the current rate limit.

**Current behavior (M37.0):** The adapter logs 429 responses as failures and returns `SubmissionResult.IsSuccess: false`. No automatic retry.

**Future enhancement (M38.x or later):** Implement exponential backoff with jitter on 429 responses, respecting the `x-amzn-RateLimit-Limit` header. This could be implemented as:
- A delegating handler on the `HttpClient` pipeline
- A Polly retry policy registered with the named `HttpClient`

### 5. SP-API Endpoint Used

The adapter calls the **Listings Items API v2021-08-01**:

```
PUT {spApiBaseUrl}/listings/2021-08-01/items/{sellerId}/{sku}
```

Request body structure:
```json
{
  "productType": "PRODUCT",
  "requirements": "LISTING",
  "attributes": {
    "item_name": [{ "value": "...", "marketplace_id": "..." }],
    "purchasable_offer": [{ "marketplace_id": "...", "currency": "USD", "our_price": [...] }],
    "product_description": [{ "value": "...", "marketplace_id": "..." }]
  },
  "marketplaceIds": ["ATVPDKIKX0DER"]
}
```

The `sellerId` and `marketplaceId` are retrieved from `IVaultClient` (paths: `amazon/seller-id`, `amazon/marketplace-id`).

### 6. Credential Storage — Vault Path Conventions

All Amazon credentials follow the ADR 0051 vault path conventions:

| Credential | Vault Path | Purpose |
|-----------|-----------|---------|
| LWA Client ID | `amazon/client-id` | OAuth 2.0 client identifier |
| LWA Client Secret | `amazon/client-secret` | OAuth 2.0 client secret |
| LWA Refresh Token | `amazon/refresh-token` | Long-lived refresh token from Seller Central authorization |
| Marketplace ID | `amazon/marketplace-id` | SP-API marketplace identifier (e.g., `ATVPDKIKX0DER` for US) |
| Seller ID | `amazon/seller-id` | Seller/Merchant identifier |

No credentials are hardcoded, read from `IConfiguration`, or passed as constructor parameters. All credential access goes through `IVaultClient.GetSecretAsync`.

### 7. Applicability to Walmart and eBay Adapters

The `IMarketplaceAdapter` interface is auth-agnostic — it defines `SubmitListingAsync`, `CheckSubmissionStatusAsync`, and `DeactivateListingAsync` without any auth-specific types. Each adapter handles its own authentication internally:

| Marketplace | Auth Model | Token Endpoint | M37.0 Status |
|-------------|-----------|----------------|-------------|
| Amazon | LWA OAuth 2.0 (refresh token grant) | `https://api.amazon.com/auth/o2/token` | ✅ Implemented |
| Walmart | OAuth 2.0 (client credentials grant) | `https://marketplace.walmartapis.com/v3/token` | 📋 M38.x |
| eBay | OAuth 2.0 (refresh token grant) | `https://api.ebay.com/identity/v1/oauth2/token` | 📋 M38.x |

Each marketplace adapter will:
1. Inject `IVaultClient` for credentials (using marketplace-specific vault paths from ADR 0051)
2. Manage its own token cache
3. Build marketplace-specific HTTP requests

This confirms the `IMarketplaceAdapter` abstraction works: auth differences are encapsulated inside each adapter, not leaked into the handler layer.

**Rationale:**

The LWA OAuth 2.0 refresh token grant is the only supported auth flow for self-authorized SP-API applications. The token caching strategy balances security (minimized vault reads, short-lived tokens) with simplicity (in-memory cache, no distributed state). Rate limiting is deferred because the reference architecture won't hit rate limits during development; documenting the approach now ensures it's designed correctly when implemented.

**Consequences:**

- Developers testing against real Amazon SP-API must configure 5 environment variables (or vault entries) per ADR 0051
- The token cache is process-local; horizontal scaling requires either accepting occasional duplicate token refreshes or adding a distributed cache
- 429 errors from SP-API are returned as submission failures until retry logic is implemented
- Future Walmart and eBay adapters follow the same pattern (inject vault, manage own auth, return standard results)

**Related Decisions:**

- ADR 0050 — Marketplaces ProductSummaryView ACL (defines the `ListingApprovedHandler` → adapter flow)
- ADR 0051 — Vault Implementation Strategy (defines credential retrieval and path conventions)
