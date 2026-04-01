# M37.x Session 1 Retrospective — Debt Clearance + ProductSummaryView ACL

**Date:** 2026-04-01
**Status:** ✅ Complete
**Duration:** Single session

---

## 1. Session Objective

Clear the two highest-priority debt items from M36.1 (message enrichment coupling and E2E shard tags) so that Phase 3 production adapter work in M37.x Session 2+ starts from a clean foundation.

**Outcome: All three deliverables completed.**

| Item | Status | Description |
|------|--------|-------------|
| D-1 | ✅ Complete | Added `@shard-3` to MarketplacesAdmin.feature, ListingsAdmin.feature, ListingsDetail.feature |
| D-2 | ✅ Complete | Built `ProductSummaryView` ACL — subscribe to 4 Product Catalog events, update `ListingApprovedHandler` to query local view |
| D-3 | ✅ Complete | Authored ADR 0050 — Marketplaces ProductSummaryView ACL decision |

---

## 2. D-1: E2E Shard Tags

**Files updated:**
- `tests/Backoffice/Backoffice.E2ETests/Features/MarketplacesAdmin.feature` — Added `@shard-3` on line 1
- `tests/Backoffice/Backoffice.E2ETests/Features/ListingsAdmin.feature` — Added `@shard-3` on line 1
- `tests/Backoffice/Backoffice.E2ETests/Features/ListingsDetail.feature` — Added `@shard-3` on line 1

All three files had no existing tags — `@shard-3` was added above the `Feature:` line. No scenario changes. CI confirmation pending PR merge (CI shard runners will discover the tagged scenarios on the next E2E workflow run).

---

## 3. D-2: ProductSummaryView ACL

### Product Catalog Events Subscribed To

| Event | Handler | Field Updated | Rationale |
|-------|---------|---------------|-----------|
| `ProductAdded` | `ProductAddedHandler` | Creates view (ProductName, Category, Status) | Initial population |
| `ProductContentUpdated` | `ProductContentUpdatedHandler` | ProductName | Name changes for submission display |
| `ProductCategoryChanged` | `ProductCategoryChangedHandler` | Category | Category mapping lookup key changes |
| `ProductStatusChanged` | `ProductStatusChangedHandler` | Status | Lifecycle eligibility tracking |

### Events NOT Subscribed To (and why)

- **`ProductImagesUpdated`** — Images not part of `ListingSubmission` interface
- **`ProductDimensionsChanged`** — Shipping dimensions not used in adapter submissions
- **`ProductDeleted` / `ProductRestored`** — Covered by `ProductStatusChanged` transitions
- **`ProductDiscontinued`** — Recall cascade is a Listings BC concern; status transition covered by `ProductStatusChanged`

### ProductSummaryView Fields

| Field | Type | Source |
|-------|------|--------|
| `Id` (SKU) | `string` | `ProductAdded.Sku` |
| `ProductName` | `string` | `ProductAdded.Name`, `ProductContentUpdated.Name` |
| `Category` | `string?` | `ProductAdded.Category`, `ProductCategoryChanged.NewCategory` |
| `BasePrice` | `decimal?` | **Not populated** — see Known Gap below |
| `Status` | `ProductSummaryStatus` | `ProductAdded.Status`, `ProductStatusChanged.NewStatus` |

### Known Gap: BasePrice

Product Catalog events do not carry price data — pricing is a Pricing BC concern. The `BasePrice` field remains null for products created via `ProductAdded`. The handler falls back to `0m` (`productSummary.BasePrice ?? 0m`), matching the previous behavior when `message.Price` was null. This is documented in ADR 0050 Decision 5.

### ListingApprovedHandler Residual Reads

**Zero residual reads from message payload.** The handler now reads only `ListingId`, `Sku`, and `ChannelCode` from the `ListingApproved` message (listing identifiers). All product data comes from the local `ProductSummaryView`:
- `productSummary.ProductName` (was `message.ProductName`)
- `productSummary.Category` (was `message.Category`)
- `productSummary.BasePrice` (was `message.Price`)

A new guard rail rejects submissions when the `ProductSummaryView` is missing for the given SKU, publishing `MarketplaceSubmissionRejected` with reason `"Product '{sku}' not yet known to Marketplaces BC — ProductSummaryView missing"`.

### RabbitMQ Configuration

- New queue: `marketplaces-product-catalog-events` (in `Marketplaces.Api/Program.cs`)
- Follows naming convention from Listings BC: `listings-product-catalog-events`
- `ProductSummaryView` registered in Marten schema with string identity

---

## 4. D-3: ADR 0050 Summary

**ADR 0050: Marketplaces ProductSummaryView Anti-Corruption Layer**

- **Decision:** Marketplaces BC maintains its own `ProductSummaryView` by subscribing to 4 Product Catalog events
- **Key rationale:** Each BC owns its own ACL; eliminates coupling to Listings BC message enrichment; resolves category taxonomy silent-break risk from ADR 0049
- **Known gap:** BasePrice not populated from Product Catalog (pricing is a Pricing BC concern)
- **Alternatives rejected:** Reusing Listings BC view, subscribing to all 9 events, keeping message enrichment, HTTP queries

---

## 5. Surprising Findings

1. **No message contract gaps for the 4 selected events.** All events carried the fields needed by the Marketplaces view. The `ProductAdded` event's optional `Status` field (added in M36.1) was particularly useful — without it, the handler would have had to default all new products to `Active`.

2. **Mutable class vs record for document store.** The Listings BC uses a `sealed record` with `init` setters and `with` expressions for its `ProductSummaryView`. The Marketplaces BC uses a `sealed class` with mutable setters, matching the Marketplaces BC's existing document patterns (`Marketplace`, `CategoryMapping`). Both are valid for Marten document store usage — the choice follows per-BC convention consistency.

3. **RabbitMQ routing required no special configuration.** Adding a second `ListenToRabbitQueue` call in `Program.cs` was sufficient — Wolverine's auto-provisioning handles queue creation and handler routing by message type.

---

## 6. Test Counts

| BC | Session Start | Session Close | Delta |
|----|---------------|---------------|-------|
| Listings | 35 | 35 | 0 |
| Marketplaces | 27 | 33 | +6 |
| **Total (Listings + Marketplaces)** | **62** | **68** | **+6** |

### New Tests Added

1. `ProductSummaryView_CreatedWhenProductAdded` — ProductAdded creates view
2. `ProductSummaryView_UpdatedWhenCategoryChanged` — ProductCategoryChanged updates category
3. `ProductSummaryView_UpdatedWhenContentUpdated` — ProductContentUpdated updates name
4. `ProductSummaryView_UpdatedWhenStatusChanged` — ProductStatusChanged updates status
5. `ProductSummaryView_ProductAdded_IdempotentWhenAlreadyExists` — Duplicate ProductAdded ignored
6. `ListingApproved_MissingProductSummaryView_PublishesRejected` — Missing view rejects submission

### Updated Tests (existing, now seed ProductSummaryView)

- `ListingApproved_AmazonChannel_CallsAdapterAndPublishesActivated`
- `ListingApproved_WalmartChannel_CallsAdapterAndPublishesActivated`
- `ListingApproved_MissingCategoryMapping_PublishesRejected`
- `ListingApproved_InactiveMarketplace_PublishesRejected`

---

## 7. Build State at Session Close

- **Errors:** 0
- **Warnings:** 8 (all pre-existing — 7 Correspondence CS0219 + 1 Backoffice.Web CS8602)
- **Note:** Baseline was reported as 33 warnings in M36.1 closure retrospective. The build system now reports 8 warnings — this discrepancy may be due to build caching, incremental build behavior, or changes in how warnings are counted across the full solution vs. incremental builds. No new warnings were introduced by this session.

---

## 8. What Session 2 Should Pick Up First

**First production adapter candidate: Amazon SP-API (AMAZON_US)**

Amazon is the recommended first adapter because:
1. It has the largest test surface from M36.1 (most test scenarios use AMAZON_US)
2. The SP-API has well-documented feed submission and status polling endpoints
3. The `StubAmazonAdapter` already returns `amzn-` prefixed submission IDs, establishing the expected contract

**Session 2 pre-requisites:**
- Resolve the `BasePrice` gap (ADR 0050, Decision 5) — either subscribe to a Pricing BC event or accept `0m` as acceptable for stub-to-production transition
- Implement production `IVaultClient` for API credentials (currently `DevVaultClient` only)
- Review Amazon SP-API authentication flow (OAuth 2.0 + AWS Signature V4) and determine if it warrants its own ADR

**Other Phase 3 items (Session 2+):**
- Walmart Marketplace API adapter
- eBay Sell API adapter
- Async submission status polling (`CheckSubmissionStatusAsync`)
- Bidirectional marketplace feedback (Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`)
