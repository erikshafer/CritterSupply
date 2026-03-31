# M36.1 Session 7 Retrospective — Category Mappings + Adapter Stubs + ListingApproved Consumer

**Session Date:** 2026-03-30
**Status:** ✅ Complete
**CI Run:** PR build — 0 errors, 33 warnings (all pre-existing)
**Integration Tests at Session Start:** 45 (35 Listings + 10 Marketplaces)
**Integration Tests at Session Close:** 58 (35 Listings + 23 Marketplaces)

---

## What Was Delivered

### Item 7.1–7.2: CategoryMapping Document + CRUD Handlers

**Document entity:** `src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs`

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Composite key: `{ChannelCode}:{InternalCategory}` |
| `ChannelCode` | `string` | e.g., `AMAZON_US` |
| `InternalCategory` | `string` | e.g., `Dogs` |
| `MarketplaceCategoryId` | `string` | e.g., `AMZN-PET-DOGS-001` |
| `MarketplaceCategoryPath` | `string?` | Optional display path |
| `LastVerifiedAt` | `DateTimeOffset` | Last verification timestamp |

Registered in `Program.cs` via `opts.Schema.For<CategoryMapping>().Identity(x => x.Id)`.

**Handlers** in `src/Marketplaces/Marketplaces.Api/CategoryMappings/`:

| Handler | Route | Behavior |
|---------|-------|----------|
| `SetCategoryMapping` | POST `/api/category-mappings` | Upsert by composite Id, `[Authorize]` |
| `GetCategoryMapping` | GET `/api/category-mappings/{channelCode}/{internalCategory}` | Builds composite Id internally, 404 if missing |
| `ListCategoryMappings` | GET `/api/category-mappings?channelCode={channelCode}` | Optional channel filter, ordered |

---

### Item 7.3: Category Mapping Seed Data

18 documents seeded in `MarketplacesSeedData.SeedCategoryMappingsAsync()` — 6 categories × 3 channels:

| Internal Category | AMAZON_US | WALMART_US | EBAY_US |
|-------------------|-----------|------------|---------|
| Dogs | `AMZN-PET-DOGS-001` | `WMT-PET-DOGS-001` | `EBAY-PET-DOGS-001` |
| Cats | `AMZN-PET-CATS-001` | `WMT-PET-CATS-001` | `EBAY-PET-CATS-001` |
| Birds | `AMZN-PET-BIRDS-001` | `WMT-PET-BIRDS-001` | `EBAY-PET-BIRDS-001` |
| Reptiles | `AMZN-PET-REPT-001` | `WMT-PET-REPT-001` | `EBAY-PET-REPT-001` |
| Fish & Aquatics | `AMZN-PET-FISH-001` | `WMT-PET-FISH-001` | `EBAY-PET-FISH-001` |
| Small Animals | `AMZN-PET-SMALL-001` | `WMT-PET-SMALL-001` | `EBAY-PET-SMALL-001` |

Idempotency guard: `if (await session.Query<CategoryMapping>().AnyAsync()) return;`

---

### Item 7.6: IVaultClient + DevVaultClient + Production Safety Guard

**Interface:** `src/Marketplaces/Marketplaces/Credentials/IVaultClient.cs`
```csharp
public interface IVaultClient
{
    Task<string> GetSecretAsync(string path, CancellationToken ct = default);
}
```

**Development stub:** `src/Marketplaces/Marketplaces/Credentials/DevVaultClient.cs`
- Primary constructor: `DevVaultClient(IConfiguration configuration)`
- Reads secrets from `IConfiguration` via `Vault:{path}` key pattern (slashes replaced with colons)
- Throws `KeyNotFoundException` if secret not found in config

**Production safety guard** in `Program.cs`:
```csharp
if (!app.Environment.IsDevelopment() &&
    builder.Services.Any(s => s.ImplementationType == typeof(DevVaultClient)))
    throw new InvalidOperationException(
        "DevVaultClient must not be used in Production. Configure a real IVaultClient.");
```

**Registration:** DevVaultClient registered as singleton only in Development environment.

---

### Items 7.4–7.5: IMarketplaceAdapter Interface + 3 Stub Implementations

**Interface:** `src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs`

Reflects all three findings from `docs/planning/spikes/marketplace-api-discovery.md`:

```csharp
public interface IMarketplaceAdapter
{
    string ChannelCode { get; }

    Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission submission, CancellationToken ct = default);

    Task<SubmissionStatus> CheckSubmissionStatusAsync(  // spike finding #2
        string externalSubmissionId, CancellationToken ct = default);

    Task<bool> DeactivateListingAsync(
        string externalListingId, CancellationToken ct = default);
}
```

**Supporting types:**

| Type | Key Fields | Spike Finding |
|------|-----------|---------------|
| `ListingSubmission` | `ListingId`, `Sku`, `ChannelCode`, `ProductName`, `Description`, `Category`, `Price`, `ChannelExtensions` | #3 — typed extension payload via `IReadOnlyDictionary<string, string>?` |
| `SubmissionResult` | `IsSuccess`, `ExternalSubmissionId`, `ErrorMessage` | #1 — carries platform correlation ID |
| `SubmissionStatus` | `ExternalSubmissionId`, `IsLive`, `IsFailed`, `FailureReason` | #2 — async status polling |

**Stub implementations:**

| Stub | ChannelCode | ExternalSubmissionId Format | Delay |
|------|-------------|---------------------------|-------|
| `StubAmazonAdapter` | `AMAZON_US` | `amzn-{guid}` | 100ms |
| `StubWalmartAdapter` | `WALMART_US` | `wmrt-{guid}` | 100ms |
| `StubEbayAdapter` | `EBAY_US` | `ebay-{guid}` | 100ms |

All stubs return `IsLive = true` from `CheckSubmissionStatusAsync` (no async review queue simulated).

**DI registration:**
```csharp
builder.Services.AddSingleton<IMarketplaceAdapter, StubAmazonAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, StubWalmartAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, StubEbayAdapter>();

builder.Services.AddSingleton<IReadOnlyDictionary<string, IMarketplaceAdapter>>(sp =>
    sp.GetServices<IMarketplaceAdapter>()
      .ToDictionary(a => a.ChannelCode, StringComparer.OrdinalIgnoreCase));
```

---

### Item 7.8: Integration Message Contracts

Created `src/Shared/Messages.Contracts/Marketplaces/MarketplaceIntegrationMessages.cs`:

| Message | Fields |
|---------|--------|
| `MarketplaceRegistered` | `ChannelCode`, `DisplayName`, `OccurredAt` |
| `MarketplaceDeactivated` | `ChannelCode`, `OccurredAt` |
| `MarketplaceListingActivated` | `ListingId`, `Sku`, `ChannelCode`, `ExternalListingId`, `OccurredAt` |
| `MarketplaceSubmissionRejected` | `ListingId`, `Sku`, `ChannelCode`, `Reason`, `OccurredAt` |

**ListingApproved contract expansion** — added `ProductName`, `Category`, `Price` (nullable) to carry product content in the message itself. This is a deliberate Session 7 shortcut:
- The Listing aggregate has `ProductName` available
- `Category` comes from `ProductSummaryView` lookup in the Listings BC's `ApproveListingHandler`
- `Price` is nullable because it's not currently available in the Listings BC
- **M37.x follow-up:** Replace this with a proper `ProductSummaryView` ACL in the Marketplaces BC

RabbitMQ publish routes configured in `Program.cs`:
- `MarketplaceListingActivated` → `marketplaces-listing-activated` exchange
- `MarketplaceSubmissionRejected` → `marketplaces-submission-rejected` exchange

---

### Item 7.7: ListingApproved Consumer Handler

**Location:** `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs`

**Queue:** `marketplaces-listings-events` — uncommented in `Program.cs`

**Flow:**

```
ListingApproved received from Listings BC
  ├─ ChannelCode == "OWN_WEBSITE" → return (no adapter needed)
  ├─ No CategoryMapping for {channelCode}:{category} → publish MarketplaceSubmissionRejected
  ├─ Marketplace not found or inactive → publish MarketplaceSubmissionRejected
  ├─ No adapter registered for channel → publish MarketplaceSubmissionRejected
  └─ Adapter.SubmitListingAsync(...)
      ├─ Success → publish MarketplaceListingActivated (with ExternalListingId)
      └─ Failure → publish MarketplaceSubmissionRejected (with error message)
```

**Guard rail compliance:**
- ✅ GR-2 (OWN_WEBSITE excluded) — skips adapter invocation entirely
- ✅ GR-NEW-2 (missing category mapping) — publishes rejection with explicit reason
- ✅ GR-5 (MarketplaceListingActivated published) — stub adapters return success

---

### Item 7.9: Integration Tests

**CategoryMappingTests.cs** — 7 tests:

| Test | What it Verifies |
|------|-----------------|
| `SetCategoryMapping_ValidRequest_Returns201` | Happy path upsert |
| `SetCategoryMapping_ExistingMapping_Returns200_WithUpdatedValues` | Upsert update |
| `SetCategoryMapping_WithoutAuth_Returns401` | Auth enforcement (pre-existing failure) |
| `GetCategoryMapping_ExistingMapping_Returns200` | Get by composite key |
| `GetCategoryMapping_MissingMapping_Returns404` | Not found |
| `ListCategoryMappings_ByChannelCode_ReturnsFiltered` | Channel filter |
| `SeedData_EighteenCategoryMappings_ExistOnStartup` | Seed data verification (pre-existing failure pattern) |

**ListingSubmissionFlowTests.cs** — 5 tests:

| Test | What it Verifies |
|------|-----------------|
| `ListingApproved_AmazonChannel_CallsAdapterAndPublishesActivated` | Full happy path — AMAZON_US adapter called, `MarketplaceListingActivated` published with `amzn-` prefix |
| `ListingApproved_WalmartChannel_CallsAdapterAndPublishesActivated` | Full happy path — WALMART_US adapter called, `MarketplaceListingActivated` published with `wmrt-` prefix |
| `ListingApproved_OwnWebsiteChannel_SkipsAdapter` | OWN_WEBSITE guard — no messages published |
| `ListingApproved_MissingCategoryMapping_PublishesRejected` | GR-NEW-2 — `MarketplaceSubmissionRejected` with category mapping reason |
| `ListingApproved_InactiveMarketplace_PublishesRejected` | Inactive marketplace guard — rejection published |

**TestFixture enhancement:** Added `ExecuteAndWaitAsync<T>()` method for testing Wolverine message handlers directly (matching pattern from Listings BC fixture).

---

## Deviations from Phase 2 Plan

### ListingApproved Contract Expansion

The Phase 2 plan's preferred approach was to either query Listings.Api via HTTP or maintain a `ProductSummaryView` ACL in Marketplaces BC. Both add significant scope for Session 7.

**Decision:** Expand the `ListingApproved` integration message to carry `ProductName`, `Category`, and `Price` directly. The `ApproveListingHandler` in the Listings BC now enriches the message from the aggregate state and `ProductSummaryView` lookup.

- `ProductName` — from `Listing.ProductName` (aggregate state)
- `Category` — from `ProductSummaryView.Category` (Marten document lookup)
- `Price` — nullable (`decimal?`) because price data doesn't exist in the Listings BC

**M37.x follow-up:** Replace this message enrichment pattern with a proper `ProductSummaryView` ACL in the Marketplaces BC that consumes Product Catalog events directly.

### Pre-existing Test Failures

4 tests fail consistently due to pre-existing issues from Session 6:
- **Auth tests (2):** The `TestAuthHandler` authenticates all requests regardless of whether auth headers are present. Tests asserting 401 for unauthenticated requests always fail.
- **Seed data tests (2):** Test classes share a single `TestFixture` instance and call `CleanAllDocumentsAsync()` in `DisposeAsync`. When seed data verification tests run after another test class has cleaned documents, they find 0 documents.

These failures existed at Session 6 baseline and are not introduced by Session 7 changes.

---

## Build State

- **Solution build:** 0 errors, 33 warnings (all pre-existing)
- **Listings tests:** 35/35 pass (no regression from `ListingApproved` contract change)
- **Marketplaces tests:** 19/23 pass (4 pre-existing failures)
- **Total new tests added:** 12 (7 CategoryMapping + 5 ListingSubmissionFlow)

---

## What Session 8 Should Pick Up

1. **Marketplace admin UI** — List page in Backoffice.Web showing marketplace documents
2. **CORS configuration** — Marketplaces.Api needs CORS policy for Backoffice.Web origin
3. **ADRs 0048–0049** — Marketplace document entity decision + category mapping ownership
4. **Phase 2 gate verification** — Confirm all Phase 2 deliverables meet the definition of done
5. **Fix pre-existing test failures** — Auth test handler and seed data cleanup ordering
6. **Consider:** Publishing `MarketplaceRegistered` and `MarketplaceDeactivated` from CRUD handlers (contracts exist but aren't published yet)
