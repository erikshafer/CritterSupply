# ADR 0054: eBay Sell API Authentication and Rate Limiting Patterns

**Status:** ✅ Accepted

**Date:** 2026-04-03

**Context:**

The `EbayMarketplaceAdapter` is the third production `IMarketplaceAdapter` implementation in CritterSupply (M37.0 Session 3). It integrates with eBay's Sell APIs (Inventory API) to create and publish offers on the eBay US marketplace. This ADR documents the authentication flow, token management strategy, the two-step listing submission model, rate limiting approach, and how the patterns differ from both the Amazon SP-API adapter (ADR 0052) and the Walmart adapter (ADR 0053).

eBay uses OAuth 2.0 with a **refresh token** grant — structurally similar to Amazon LWA but with key differences: the client_id and client_secret are sent as HTTP Basic auth (Base64 in the Authorization header) rather than as form body parameters, and specific API scopes are required.

**Decisions:**

### 1. OAuth 2.0 Refresh Token Grant Flow

The `EbayMarketplaceAdapter` authenticates using the refresh token grant:

```
POST https://api.ebay.com/identity/v1/oauth2/token
Content-Type: application/x-www-form-urlencoded
Authorization: Basic {Base64(client_id:client_secret)}

grant_type=refresh_token&refresh_token={token}&scope=https://api.ebay.com/oauth/api_scope/sell.inventory
```

This returns an access token (typically 2-hour TTL) used as `Authorization: Bearer {access_token}` on API calls.

**Key difference from Amazon (ADR 0052):** Client_id and client_secret are Base64-encoded in the Authorization header (HTTP Basic auth), not passed as form body parameters. This is a critical implementation detail — sending credentials in the body will result in a 401 error.

**Key difference from Walmart (ADR 0053):** eBay uses a refresh token (like Amazon) rather than client credentials only. The refresh token is a long-lived credential that must be stored securely in the vault.

### 2. Base64 Encoding — UTF-8 Required

The HTTP Basic auth header for the token exchange must use UTF-8 encoding:

```csharp
Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"))
```

Using `Encoding.Default` or `Encoding.ASCII` can cause issues with special characters in client secrets on non-English locales. UTF-8 is the safe default per RFC 7617.

### 3. Required Scopes

The eBay Sell Inventory API requires the `sell.inventory` scope:

```
https://api.ebay.com/oauth/api_scope/sell.inventory
```

This scope grants access to both the Offer API (create/publish offers) and the Inventory Item API. The scope is included in the token exchange request, not on individual API calls.

### 4. Token Caching Strategy

The `EbayMarketplaceAdapter` caches the access token in memory with a 5-minute safety margin before the actual token expiry — the same `SemaphoreSlim` + double-check pattern established in `AmazonMarketplaceAdapter`:

- Token TTL from eBay response: typically 7200 seconds (2 hours)
- Cache expiry: `token_ttl - 300 seconds` = effective 115-minute cache
- Thread-safe via `SemaphoreSlim` with double-check pattern after lock acquisition
- On token refresh, `IVaultClient.GetSecretAsync` is called for 3 credentials (client-id, client-secret, refresh-token)

### 5. Two-Step Listing Submission

eBay listing submission is a two-step process:

1. **Create Offer:** `POST /sell/inventory/v1/offer` — creates a draft offer, returns an `offerId`
2. **Publish Offer:** `POST /sell/inventory/v1/offer/{offerId}/publish` — publishes the draft, making it live

The `SubmitListingAsync` method executes both steps. The `ExternalSubmissionId` returned is the offer ID (prefixed with `ebay-` to match the `StubEbayAdapter` convention).

**Failure handling:**
- If step 1 (create) fails, the submission fails immediately
- If step 2 (publish) fails, the submission fails — the draft offer exists on eBay but is not live. Cleanup of orphaned drafts is deferred to M38.x

### 6. Rate Limiting — Log and Fail (Not Yet Implemented)

eBay Sell APIs enforce per-call rate limits that vary by operation. When rate-limited, eBay returns HTTP 429 with a `Retry-After` header.

**Current behavior (M37.0):** The adapter logs 429 responses as failures and returns `SubmissionResult.IsSuccess: false`. No automatic retry.

**Future enhancement (M38.x):** Implement exponential backoff with jitter, respecting the `Retry-After` header.

### 7. Credential Storage — Vault Path Conventions

All eBay credentials follow the ADR 0051 vault path conventions:

| Credential | Vault Path | Environment Variable |
|-----------|-----------|---------------------|
| Client ID (App ID) | `ebay/client-id` | `VAULT__EBAY__CLIENT_ID` |
| Client Secret (Cert ID) | `ebay/client-secret` | `VAULT__EBAY__CLIENT_SECRET` |
| Refresh Token | `ebay/refresh-token` | `VAULT__EBAY__REFRESH_TOKEN` |
| Marketplace ID | `ebay/marketplace-id` | `VAULT__EBAY__MARKETPLACE_ID` |

The marketplace ID (e.g., `EBAY_US`) is passed as the `X-EBAY-C-MARKETPLACE-ID` header on API calls.

No credentials are hardcoded, read from `IConfiguration`, or passed as constructor parameters. All credential access goes through `IVaultClient.GetSecretAsync`.

### 8. Skeleton Methods

`CheckSubmissionStatusAsync` and `DeactivateListingAsync` are skeleton implementations in M37.0:

- `CheckSubmissionStatusAsync`: Returns `IsLive: false, IsFailed: false` — real status checking via `GET /sell/inventory/v1/offer/{offerId}` deferred to M38.x (D-3)
- `DeactivateListingAsync`: Returns `false` — offer withdrawal via `POST /sell/inventory/v1/offer/{offerId}/withdraw` deferred to a future session

**Rationale:**

The refresh token grant is eBay's standard OAuth flow for applications authorized by sellers. The two-step create-then-publish model for offers is eBay's recommended approach — it allows validation of the offer before making it live, and aligns with eBay's separation between inventory items and marketplace offers.

The HTTP Basic auth encoding follows RFC 7617 and uses UTF-8 explicitly to prevent locale-specific encoding issues. This was flagged during security review as a common implementation mistake.

**Consequences:**

- Developers testing against real eBay Sell API must configure 4 environment variables per ADR 0051
- The two-step submission means a partial failure (create succeeds, publish fails) can leave orphaned draft offers on eBay — cleanup logic is deferred
- The token cache benefits from eBay's longer TTL (2 hours vs Walmart's 15 minutes), reducing token refresh frequency
- 429 errors from eBay API are returned as submission failures until retry logic is implemented

**Related Decisions:**

- ADR 0050 — Marketplaces ProductSummaryView ACL (defines the `ListingApprovedHandler` → adapter flow)
- ADR 0051 — Vault Implementation Strategy (defines credential retrieval and path conventions)
- ADR 0052 — Amazon SP-API Authentication Patterns (establishes the token caching pattern reused here)
- ADR 0053 — Walmart Marketplace API Authentication (contrasting client credentials vs refresh token)
