# M38.1 Planning Notes

**Status:** 📋 Pre-planning notes (not a formal plan)
**Authored:** 2026-04-03
**Source:** M38.0 Milestone Closure Session (C-5)
**Purpose:** Prevent M38.1 from starting cold. These are structured notes, not a plan. Implementation details belong in the M38.1 plan document that will be authored in its own planning session.

---

## What M38.1 Means

M38.0 completed the adapter lifecycle at the submission and activation layer: a listing approved in the Backoffice is submitted to the marketplace, the marketplace outcome is polled (Walmart) or immediately resolved (Amazon, eBay), and the Listings BC reflects the result (`Live` or `Ended`). Admin users can approve, pause, and end listings through the Backoffice UI.

M38.1 completes the deactivation and status-checking layers that were deferred. It also resolves the Walmart interface design limitation that prevents deactivation from working end-to-end. This is the final Phase 4 session — after M38.1, the adapter lifecycle is fully closed for all three marketplaces.

---

## Known Scope (from M38.0 Debt Table)

The following items were explicitly deferred from M38.0 and are the primary candidates for M38.1:

| Item | Complexity note |
|------|----------------|
| **Walmart `DeactivateListingAsync` — SKU gap** | **Prerequisite gate.** Returns false today. Cannot be implemented until the interface design decision is made (see below). Requires 3 TODO tests in `WalmartMarketplaceAdapterTests.cs`. |
| **Walmart deactivation interface design** | **Prerequisite gate.** Must be resolved before any deactivation work. Two options analyzed in Session 2 retro; Option (a) recommended. See detailed section below. |
| **Amazon `CheckSubmissionStatusAsync` real implementation** | Low-medium. GET to verify BUYABLE status after synchronous ACCEPTED. Requires vault read for seller ID. Enhancement, not a correctness fix. |
| **eBay `CheckSubmissionStatusAsync` real implementation** | Low-medium. GET offer status after synchronous publish. Enhancement, not a correctness fix. |
| **Orphaned eBay draft offer cleanup** | Medium. Publish fails after create succeeds → orphaned draft on eBay. Background sweep vs retry saga. Deferred from M38.0 Q5. |
| **`SemaphoreSlim` token caching refactor** | Low. Extract shared `CachedTokenProvider` base class. Three copies today. Defer until fourth adapter added. |
| **Walmart deactivation full tests** | Low. 3 TODO tests blocked on SKU gap resolution. Will be unblocked when the interface design is resolved. |

---

## The Walmart Interface Design Decision

This is the primary prerequisite gate for M38.1. The other deactivation items cannot be designed until it is resolved.

### The Problem

`IMarketplaceAdapter.DeactivateListingAsync(string externalListingId, ...)` receives only the external listing ID. For Walmart, this is `wmrt-{feedId}` — the feed ID from the original `MP_ITEM` submission. However, the Walmart `RETIRE_ITEM` feed requires the item **SKU**, not the feed ID. The feed ID cannot be reverse-mapped to a SKU at the adapter level.

Amazon (`amzn-{sku}`) and eBay (`ebay-{offerId}`) do not have this problem — their external IDs encode the correct identifier for deactivation.

### Option (a) — Change the Walmart externalListingId format (recommended)

Change `WalmartMarketplaceAdapter` to store `wmrt-{sku}` as the `externalListingId` (not `wmrt-{feedId}`), using the feed ID only internally for polling.

**Advantages:**
- Makes the interface semantically consistent: Amazon uses `amzn-{sku}`, eBay uses `ebay-{offerId}` (sufficient for withdraw), Walmart would use `wmrt-{sku}` (sufficient for RETIRE_ITEM)
- No breaking change to `IMarketplaceAdapter` — the interface stays as-is
- The Walmart polling handler already has the feed ID separately in `CheckWalmartFeedStatus.ExternalFeedId`
- The SKU is available at submission time in the `ListingApproved` message

**Impact:**
- `WalmartMarketplaceAdapter.SubmitListingAsync` changes its `ExternalSubmissionId` return value from `wmrt-{feedId}` to `wmrt-{sku}`
- The `ListingApprovedHandler` Walmart path must pass the feed ID to `CheckWalmartFeedStatus` separately (already does this)
- Existing `MarketplaceListingActivated` events in the Listings event stream will carry `wmrt-{feedId}` from before this change — idempotency guards handle this gracefully

### Option (b) — Change the IMarketplaceAdapter interface

Add additional context to `DeactivateListingAsync` (e.g., a `string sku` parameter alongside `externalListingId`).

**Advantages:**
- Keeps the Walmart `externalListingId` unchanged

**Disadvantages:**
- Breaking interface change — all three adapter implementations need updating
- The `sku` parameter is only needed by Walmart; Amazon and eBay would ignore it
- Adds a parameter that violates the principle of interface segregation for adapter-specific concerns

**Recommendation:** Option (a) is cleaner. The SKU is the stable external identifier for a marketplace item, while the feed ID is an internal Walmart processing artifact. An ADR should document this decision in M38.1.

---

## Open Questions for the M38.1 Planning Session

1. **Which Walmart interface design option to adopt?** Recommend Option (a) — encode `wmrt-{sku}` as the `externalListingId` — and document the reasoning in an M38.1 ADR (0057).

2. **Should `CheckSubmissionStatusAsync` for Amazon and eBay be implemented in M38.1, or are they lower priority than deactivation?** The synchronous path is already correct for both. The GET verification is an enhancement that confirms BUYABLE (Amazon) or offer status (eBay) after the synchronous response. If M38.1 capacity is limited, deactivation should take priority.

3. **Should the orphaned eBay draft cleanup be a retry saga or a background sweep?** M38.0 Q5 deferred this. A background sweep (periodic `GET /sell/inventory/v1/offer?status=UNPUBLISHED`) is simpler than a retry saga. The planning session should make this choice and scope the implementation accordingly.

4. **Is `SemaphoreSlim` base class extraction in M38.1 scope or deferred until a fourth adapter is added?** M37.0 lesson 3 identified the pattern duplication. Three copies are manageable; a fourth would push this to mandatory. Unless M38.1 has excess capacity, deferring is acceptable.

---

## Codebase State at M38.1 Start

- **Solution:** 19 bounded contexts, `CritterSupply.slnx`
- **Integration tests:** 133 total (92 Marketplaces + 41 Listings); 0 failures
- **E2E scenarios:** 9 active on `@shard-3` (MarketplacesAdmin × 6, ListingsDetail × 3)
- **Production adapters:** Amazon, Walmart, eBay — behind `UseRealAdapters` flag; Polly resilience active
- **Walmart polling:** live for `WALMART_US` channel; Amazon and eBay synchronous
- **Bidirectional feedback:** fully wired — Listings BC receives and processes marketplace outcomes
- **Admin action buttons:** Approve/Pause/End live on `ListingDetail.razor`
- **Build:** 0 errors, 16 warnings (all pre-existing)
- **Next ADR:** 0057

---

## References

- [M38.0 Milestone Closure Retrospective](./m38-0-milestone-closure-retrospective.md) — Section 5 (debt table) and Section 6 (inherited state)
- [M38.0 Session 2 Retrospective](./m38-0-session-2-retrospective.md) — Section "Walmart Deactivation SKU Gap is an Interface Design Issue"
- [ADR 0055](../../decisions/0055-submission-status-polling-architecture.md) — Walmart polling architecture
- [ADR 0056](../../decisions/0056-marketplace-adapter-resilience-patterns.md) — Resilience patterns and Walmart SKU gap documentation
- [ADR 0054](../../decisions/0054-ebay-sell-api-authentication.md) — eBay orphaned draft note
