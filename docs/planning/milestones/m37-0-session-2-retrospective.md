# M37.0 Session 2 Retrospective — Production IVaultClient + Amazon SP-API Adapter

**Date:** 2026-04-03
**Session type:** Phase 3 kickoff — decisions + implementation
**Duration:** Single session

---

## Decision Log

### D-1: Adapter Delivery Scope for M37.0

**Choice:** (b) Amazon only in M37.0; Walmart + eBay deferred to M38.x

**Rationale:** Amazon SP-API auth is the most complex of the three marketplaces (OAuth 2.0 LWA + potential AWS Sig V4 for restricted endpoints). Each marketplace's OAuth flow is different enough to warrant its own ADR. Delivering one well-structured reference adapter is more valuable than three shallow implementations. The `IMarketplaceAdapter` interface and dictionary-based `ChannelCode` resolution pattern already support adding adapters incrementally — Walmart and eBay stubs remain registered in Development/CI.

**Confidence:** High

### D-2: Production Vault Backend

**Choice:** (d) EnvironmentVaultClient — reads secrets from environment variables

**Rationale:** CritterSupply is a reference architecture. Adding Azure Key Vault SDK or HashiCorp Vault client would introduce cloud-specific infrastructure nobody cloning this repository will have configured. Environment variables work in CI (GitHub Actions secrets → env vars), Docker Compose (`environment:` blocks), and local development (`export`). The `VAULT__` prefix with double-underscore path separator matches ASP.NET Core's env-var configuration convention, making it familiar to .NET developers.

**Confidence:** High

### D-3: Submission Status Polling Scope

**Choice:** (b) Polling deferred to M38.x

**Rationale:** `StubAmazonAdapter.CheckSubmissionStatusAsync` returns `IsLive: true` immediately — there's no async gap to poll. The real SP-API feed submission workflow (submit → poll `getFeedDocument` → parse processing report) has its own error taxonomy and retry semantics that deserve dedicated design. M37.0 proves the `SubmitListingAsync` path; M38.x tackles the async lifecycle.

**Confidence:** High

### D-4: BasePrice Gap

**Choice:** (a) Accept `0m` fallback — already documented in ADR 0050 Decision 5

**Rationale:** ADR 0050 Decision 5 explicitly documents this gap: "BasePrice not populated; handler falls back to 0m." The Pricing BC doesn't publish integration events yet. The `0m` fallback is safe because Amazon's own validation will reject a $0.00 listing, surfacing as `SubmissionResult.IsSuccess: false` → `MarketplaceSubmissionRejected`. The gap is visible, documented, and has a clear future resolution path. No action needed in M37.0.

**Confidence:** High

---

## Deliverables

### A-1: ADR 0051 — Vault Implementation Strategy

- **Location:** `docs/decisions/0051-vault-implementation-strategy.md`
- **Content:** Documents `EnvironmentVaultClient` as the production implementation, vault path conventions for all marketplace credentials (`amazon/client-id` → `VAULT__AMAZON__CLIENT_ID`), the `VAULT__` prefix rationale, and the production/development guard pattern.
- **Vault path convention:** `{marketplace}/{credential-name}` → `VAULT__{MARKETPLACE}__{CREDENTIAL_NAME}` (hyphens → underscores, slashes → double underscores, all uppercase)
- **What it does NOT cover:** Azure Key Vault SDK, HashiCorp Vault, encryption at rest — these are future additions documented as non-goals.

### A-2: EnvironmentVaultClient

- **Location:** `src/Marketplaces/Marketplaces/Credentials/EnvironmentVaultClient.cs`
- **Implementation:**
  - `GetSecretAsync(string path)` converts path to env var name via `PathToEnvironmentVariable` and reads from `Environment.GetEnvironmentVariable`
  - Throws `InvalidOperationException` with descriptive message when env var is missing or empty (includes both the env var name and the original vault path)
  - `PathToEnvironmentVariable` is `internal static` — visible to tests via `InternalsVisibleTo`
  - Input validation: throws `ArgumentException` for null/whitespace paths
- **Registration in Program.cs:**
  - Development: `DevVaultClient` (unchanged)
  - Non-Development: `EnvironmentVaultClient`
  - Production safety guard for `DevVaultClient` preserved at line 160

### A-3: AmazonMarketplaceAdapter

- **Location:** `src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs`
- **ChannelCode:** `AMAZON_US`
- **Authentication:** LWA OAuth 2.0 refresh token grant
  - Token exchange: `POST https://api.amazon.com/auth/o2/token` with client_id, client_secret, refresh_token from `IVaultClient`
  - Token caching: In-memory with 5-minute safety margin before expiry, thread-safe via `SemaphoreSlim`
  - AWS Signature V4 not required for Listings Items API
- **SubmitListingAsync:** Full implementation
  - Calls SP-API `PUT /listings/2021-08-01/items/{sellerId}/{sku}`
  - Builds complete request body with item_name, purchasable_offer, product_description, recommended_browse_nodes
  - Handles success (returns `SubmissionResult.IsSuccess: true`) and failure (logs and returns error details)
  - Supports `ChannelExtensions` pass-through for Amazon-specific attributes
- **CheckSubmissionStatusAsync:** Skeleton — returns `IsLive: false, IsFailed: false` with explanation. Deferred to M38.x per D-3.
- **DeactivateListingAsync:** Skeleton — returns `false` with logging. Full implementation in a future session.
- **No hardcoded credentials** — all 5 secrets fetched via `IVaultClient` (client-id, client-secret, refresh-token, seller-id, marketplace-id)
- **Registration in Program.cs:**
  - Controlled by `Marketplaces:UseRealAdapters` config flag (default: `false`)
  - When `true`: registers `AmazonMarketplaceAdapter` + named `HttpClient("AmazonSpApi")`
  - When `false`: registers `StubAmazonAdapter` (unchanged behavior for Development/CI)
  - `StubAmazonAdapter` preserved — not deleted
  - Walmart and eBay stubs always registered (D-1: deferred to M38.x)

### A-4: ADR 0052 — Amazon SP-API Authentication Patterns

- **Location:** `docs/decisions/0052-amazon-spapi-authentication.md`
- **Content:** LWA OAuth 2.0 flow, token caching strategy, AWS Sig V4 decision (not required for Listings Items API), rate limiting approach (log-and-fail now, retry with backoff in M38.x), SP-API endpoint details, credential vault paths, and applicability table for Walmart/eBay adapters.

---

## SP-API Authentication Notes

- **No AWS Signature V4 surprises** — The Listings Items API v2021-08-01 uses bearer token auth only. AWS Sig V4 was required historically but Amazon migrated most endpoints to LWA-only auth.
- **Token refresh is straightforward** — Standard OAuth 2.0 refresh token grant. The adapter caches the access token in memory with a 5-minute buffer before TTL expiry.
- **Rate limits documented but not enforced** — SP-API `putListingsItem` allows 5 req/s with 10-burst. The adapter logs 429s as failures. Automatic retry deferred to M38.x.

---

## StubAmazonAdapter Preservation

`StubAmazonAdapter.cs` is preserved in `src/Marketplaces/Marketplaces/Adapters/`. The registration switch in `Program.cs` uses the `Marketplaces:UseRealAdapters` configuration flag:

```csharp
var useRealAdapters = builder.Configuration.GetValue<bool>("Marketplaces:UseRealAdapters");

if (useRealAdapters)
{
    builder.Services.AddHttpClient("AmazonSpApi");
    builder.Services.AddSingleton<IMarketplaceAdapter, AmazonMarketplaceAdapter>();
}
else
{
    builder.Services.AddSingleton<IMarketplaceAdapter, StubAmazonAdapter>();
}
```

Tests continue to use `StubAmazonAdapter` (the flag defaults to `false`). Developers with real Amazon credentials can opt in via `appsettings.json` or environment variables.

---

## Test Counts

| BC | Tests at Session Start | Tests at Session Close | Delta |
|----|----------------------|----------------------|-------|
| Marketplaces | 33 | 53 | +20 |
| Listings | 35 | 35 | 0 |
| **Combined** | **68** | **88** | **+20** |

### New Tests (20 total)

**EnvironmentVaultClientTests (13):**
- `GetSecretAsync_ReturnsSecret_WhenEnvVarSet`
- `GetSecretAsync_Throws_WhenEnvVarMissing`
- `GetSecretAsync_Throws_WhenEnvVarIsEmpty`
- `PathToEnvironmentVariable_FollowsAdr0051Convention` (7 Theory cases: amazon/client-id, amazon/client-secret, amazon/refresh-token, amazon/marketplace-id, amazon/seller-id, walmart/client-id, ebay/client-id)
- `GetSecretAsync_Throws_WhenPathIsNull`
- `GetSecretAsync_Throws_WhenPathIsWhitespace`

**AmazonMarketplaceAdapterTests (7):**
- `ChannelCode_IsAmazonUs`
- `SubmitListing_ReturnsSuccess_WhenSpApiReturns2xx`
- `SubmitListing_ReturnsFailure_WhenSpApiReturns4xx`
- `SubmitListing_BuildsCorrectRequest`
- `SubmitListing_ReturnsFailure_WhenLwaTokenFails`
- `SubmitListing_CachesLwaToken_AcrossMultipleCalls`
- `CheckSubmissionStatus_ReturnsPendingStatus`
- `DeactivateListing_ReturnsFalse`

*(Note: AmazonMarketplaceAdapterTests also establishes `FakeHttpMessageHandler`, `FakeVaultClient`, and `FakeHttpClientFactory` test doubles — reusable by Walmart and eBay adapter tests in M38.x.)*

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** Pre-existing only (NU1504 duplicate package refs, CS0219 unused variables in Correspondence, CS8602 null deref in Backoffice.Web) — no new warnings introduced
- **CI run:** Pending (PR branch push will trigger CI)

---

## What Session 3 Should Pick Up

1. **Walmart adapter** (`WalmartMarketplaceAdapter`) — OAuth 2.0 client credentials grant, different from Amazon's refresh token grant. Needs its own ADR (0053 candidate).
2. **eBay adapter** (`EbayMarketplaceAdapter`) — OAuth 2.0 refresh token grant (similar to Amazon LWA but different endpoint/scopes). Needs its own ADR (0054 candidate).
3. **Reuse test infrastructure** — `FakeHttpMessageHandler`, `FakeVaultClient`, and `FakeHttpClientFactory` from `AmazonMarketplaceAdapterTests` should be extracted to a shared test utility if needed by multiple test files.
4. **Rate limiting** — If any adapter needs retry logic before Session 4, implement as a delegating handler on the `HttpClient` pipeline.
5. **Polling infrastructure** — Deferred to M38.x (D-3). Design needed for Wolverine `bus.ScheduleAsync()` polling saga.

---

## Files Changed

### New Files
- `docs/decisions/0051-vault-implementation-strategy.md` — ADR for vault strategy
- `docs/decisions/0052-amazon-spapi-authentication.md` — ADR for SP-API auth patterns
- `src/Marketplaces/Marketplaces/Credentials/EnvironmentVaultClient.cs` — Production IVaultClient
- `src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs` — Amazon SP-API adapter
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/EnvironmentVaultClientTests.cs` — Vault client tests
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/AmazonMarketplaceAdapterTests.cs` — Adapter tests

### Modified Files
- `src/Marketplaces/Marketplaces.Api/Program.cs` — Vault registration (dev/prod split), adapter registration switch (`UseRealAdapters` flag)
- `src/Marketplaces/Marketplaces/Marketplaces.csproj` — Added `InternalsVisibleTo` for test project
- `docs/planning/CURRENT-CYCLE.md` — Session 2 progress block
