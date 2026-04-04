# M38.1 Session 1 — Decision Log

**Date:** 2026-04-03
**Milestone:** M38.1 — Marketplaces Phase 4b: Deactivation + Status Verification

---

## Q1: Which Walmart interface design option?

**Decision:** Option (a) — refined approach. Fix in `CheckWalmartFeedStatusHandler`, not in
`SubmitListingAsync`.

**Rationale:**

The `CheckWalmartFeedStatus` record already carries both `Sku` and `ExternalFeedId` as separate
fields. These two identifiers serve different purposes:

- `ExternalFeedId` — Walmart internal feed processing artifact, used only for polling
  (`CheckSubmissionStatusAsync`)
- `Sku` — stable marketplace listing identifier, used for deactivation (`DeactivateListingAsync`)

The handler currently publishes `MarketplaceListingActivated` with `wmrt-{ExternalFeedId}`. The
fix changes this to `wmrt-{Sku}`. This means:

- `SubmitListingAsync` is **unchanged** — still returns `wmrt-{feedId}` (needed for poll scheduling)
- `ListingApprovedHandler` is **unchanged** — still extracts `rawFeedId` and schedules `CheckWalmartFeedStatus`
- `CheckSubmissionStatusAsync` is **unchanged** — still receives and strips `wmrt-{feedId}` for the GET poll
- Only `CheckWalmartFeedStatusHandler` changes — publishes `wmrt-{sku}` as the external listing ID
- `DeactivateListingAsync` then receives `wmrt-{sku}`, strips the prefix → `{sku}`, and submits a
  RETIRE_ITEM feed

This is consistent with how Amazon (`amzn-{sku}`) and eBay (`ebay-{offerId}`) encode identifiers
sufficient for deactivation.

**Documented in:** ADR 0057

---

## Q2: Amazon/eBay `CheckSubmissionStatusAsync` priority?

**Decision:** Defer to Session 2.

**Rationale:** Both are enhancements (synchronous path is already correct), not correctness fixes.
Session 1 is fully committed to Walmart deactivation work (A-1 through A-5). Amazon and eBay
`CheckSubmissionStatusAsync` implementations are Session 2 scope if capacity allows.

---

## Q3: eBay orphaned draft cleanup — retry saga or background sweep?

**Decision:** Background sweep. Defer design to Session 2.

**Rationale:** A background sweep via periodic `GET /sell/inventory/v1/offer?filter=status:UNPUBLISHED`
is simpler than a per-submission retry saga. It allows discovery and cleanup without per-submission
state. Design deferred to Session 2 if time requires.

---

## Q4: `SemaphoreSlim` base class extraction — M38.1 or defer?

**Decision:** Defer until a fourth adapter is added.

**Rationale:** Three copies today (Amazon, Walmart, eBay). Not a correctness issue — each adapter
works correctly with its own `SemaphoreSlim` instance. Extracting a base class now would be
premature optimization. When a fourth adapter is added, the duplication becomes a maintenance
burden worth addressing.
