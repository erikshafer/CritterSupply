# ADR 0057 — Walmart Deactivation Identifier Design

**Status:** Accepted
**Date:** 2026-04-03
**Milestone:** M38.1 Session 1

---

## Context

The `IMarketplaceAdapter.DeactivateListingAsync(string externalListingId)` method receives a
single identifier — the same `ExternalListingId` that was published in
`MarketplaceListingActivated` when the listing was first activated on the marketplace.

For Amazon and eBay, the `ExternalListingId` encodes an identifier sufficient for deactivation:

| Marketplace | `ExternalListingId` format | Deactivation target |
|-------------|---------------------------|---------------------|
| Amazon | `amzn-{sku}` | SKU → `DELETE /listings/2021-08-01/items/{sellerId}/{sku}` |
| eBay | `ebay-{offerId}` | Offer ID → `POST /sell/inventory/v1/offer/{offerId}/withdraw` |

For Walmart, the original implementation (M37.0 Session 3) published `wmrt-{feedId}` as the
`ExternalListingId`. The feed ID is the Walmart internal processing artifact returned by the
MP_ITEM feed submission (`POST /v3/feeds?feedType=MP_ITEM`). This identifier is required for
polling feed status (`GET /v3/feeds/{feedId}`), but it cannot be used for deactivation.

The Walmart RETIRE_ITEM feed (`POST /v3/feeds?feedType=RETIRE_ITEM`) requires the item **SKU**
in the request body — not the feed ID. There is no API to reverse-map a feed ID back to a SKU.
This architectural gap was documented in ADR 0056 and left as the primary debt item for M38.1.

---

## The Two-Identifier Distinction

The Walmart submission flow involves two distinct identifiers that serve different purposes:

1. **`ExternalFeedId`** — Walmart's internal feed processing ID (e.g., `"FEED-ABC-123"`).
   Returned by the MP_ITEM feed submission. Used exclusively for polling feed status via
   `CheckSubmissionStatusAsync`. This is a transient processing artifact — it has no meaning
   once the feed is resolved (PROCESSED or ERROR).

2. **`ExternalListingId`** — The stable marketplace listing identifier published in
   `MarketplaceListingActivated`. Used by downstream consumers (Listings BC) to identify the
   listing on the marketplace for subsequent operations (e.g., deactivation via
   `DeactivateListingAsync`).

The `CheckWalmartFeedStatus` scheduled message already carries both as separate fields:

```csharp
public sealed record CheckWalmartFeedStatus(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ExternalFeedId,    // feed processing ID — for polling
    int AttemptCount);
```

The `Sku` field on this message is the stable listing identifier. The `ExternalFeedId` field is
the transient processing artifact.

---

## Decision

Change `CheckWalmartFeedStatusHandler` to publish `wmrt-{Sku}` (not `wmrt-{ExternalFeedId}`)
as the `ExternalListingId` in the `MarketplaceListingActivated` integration event.

**Before (M38.0):**
```csharp
var externalSubmissionId = $"wmrt-{message.ExternalFeedId}";
// ...
outgoing.Add(new MarketplaceListingActivated(
    message.ListingId, message.Sku, message.ChannelCode,
    externalSubmissionId,  // ← wmrt-{feedId}
    now));
```

**After (M38.1):**
```csharp
var externalSubmissionId = $"wmrt-{message.ExternalFeedId}";
// ...
outgoing.Add(new MarketplaceListingActivated(
    message.ListingId, message.Sku, message.ChannelCode,
    $"wmrt-{message.Sku}",  // ← wmrt-{sku} — SKU is the stable listing identifier
    now));
```

Key constraints:
- `SubmitListingAsync` remains unchanged — still returns `wmrt-{feedId}` for poll scheduling
- `CheckSubmissionStatusAsync` remains unchanged — still strips `wmrt-` to get the feed ID
- Only the `ExternalListingId` in `MarketplaceListingActivated` changes to carry the SKU
- `DeactivateListingAsync` strips `wmrt-` to get the SKU, then submits a RETIRE_ITEM feed

---

## Why Not Change the Interface?

**Option (b) — rejected:** Adding a `sku` parameter to `IMarketplaceAdapter.DeactivateListingAsync`
would mean Amazon and eBay must accept a parameter they don't need. The `IMarketplaceAdapter`
interface is intentionally uniform — each adapter encodes sufficient information in its
`ExternalListingId` prefix:

```csharp
Task<bool> DeactivateListingAsync(string externalListingId, CancellationToken ct = default);
```

This design keeps the interface clean and the caller agnostic of marketplace-specific identifier
requirements.

---

## Impact on Existing Events

Existing `MarketplaceListingActivated` events in the Marten event store carry `wmrt-{feedId}`
as the `ExternalListingId`. This has no operational impact because:

1. The `MarketplaceListingActivatedHandler` in Listings BC uses the `ExternalListingId` to set
   `MarketplaceListing.ExternalListingId`. Existing listings activated before this change will
   have the old `wmrt-{feedId}` format.
2. If a deactivation is attempted on an old-format listing, `DeactivateListingAsync` will strip
   `wmrt-` and receive a feed ID instead of a SKU. The RETIRE_ITEM feed will fail (Walmart
   won't recognize the feed ID as a SKU), and the adapter will return `false` — a safe,
   detectable failure.
3. The idempotency guards on `MarketplaceListingActivatedHandler` handle duplicate messages
   gracefully regardless of `ExternalListingId` format.

---

## Consequences

**Positive:**
- `DeactivateListingAsync` can now extract the SKU and submit a RETIRE_ITEM feed
- All three adapters follow the same pattern: prefix + deactivation-sufficient identifier
- No interface changes required — `IMarketplaceAdapter` remains clean
- `SubmitListingAsync` return value is unchanged — polling infrastructure is unaffected

**Negative:**
- Existing `MarketplaceListingActivated` events carry the old `wmrt-{feedId}` format — these
  listings cannot be deactivated via the Walmart API without manual SKU lookup

---

## References

- ADR 0053 — Walmart Marketplace API Integration
- ADR 0055 — Submission Status Polling Architecture
- ADR 0056 — Marketplace Adapter Resilience Patterns (Section: Walmart deactivation gap)
- `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs`
- `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs`
- `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs`
