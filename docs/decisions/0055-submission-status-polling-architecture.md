# ADR 0055 — Submission Status Polling Architecture

**Status:** Accepted  
**Date:** 2026-04-03  
**Milestone:** M38.0 Session 1

---

## Context

After M37.0 delivered production adapters for Amazon, Walmart, and eBay, every listing that
progresses through the `ListingApproved` → `SubmitListingAsync` path ends in `Submitted` status
permanently. The Marketplaces BC publishes `MarketplaceListingActivated` or
`MarketplaceSubmissionRejected`, but the Listings BC had no handler for either message. The
feedback loop from Marketplaces BC back to Listings BC was never wired.

The root complexity is that Walmart uses a **feed-based async submission model**. When
`WalmartMarketplaceAdapter.SubmitListingAsync` succeeds, Walmart returns a `feedId`; the actual
item processing happens asynchronously. The final PROCESSED/ERROR status only becomes available
by polling `GET /v3/feeds/{feedId}`. Amazon and eBay, by contrast, confirm activation
synchronously in their submission responses, so they publish `MarketplaceListingActivated`
immediately (no polling required).

Five questions were resolved in the M38.0 planning session:

- **Q1** — Which BC owns polling? → Marketplaces BC
- **Q2** — How to schedule polling? → Per-submission `bus.ScheduleAsync()`
- **Q3** — Which channels need polling? → Walmart only
- **Q4** — What comes first: bidirectional feedback or deactivation? → Bidirectional feedback
- **Q5** — Orphaned eBay draft handling? → Deferred to M38.1

---

## Decisions

### Q1: Marketplaces BC owns polling

Polling is adapter-internal knowledge. The concept of a Walmart feed ID, the `wmrt-` prefix
convention, and the `RECEIVED → INPROGRESS → PROCESSED / ERROR` lifecycle all belong to the
Marketplace adapter layer — not to the Listings BC. Listings BC should only be told _outcomes_
(listing activated, submission rejected), not _how_ those outcomes were determined.

Moving polling to Marketplaces BC preserves the clean integration boundary: Listings BC
subscribes to outcome exchanges, Marketplaces BC owns the adapter mechanics entirely.

### Q2: Per-submission scheduled message via `bus.ScheduleAsync()`

The alternative was a polling saga or a background timer. A saga adds state, compensations, and
complexity that are not warranted for a single-adapter use case. A background timer requires an
external scheduler or hosted service.

`bus.ScheduleAsync()` with Wolverine's durable local queue delivers exactly what is needed:
per-submission tracking, escalating retry delay, and max-attempt termination — all without saga
overhead. The `AttemptCount` field on the scheduled message provides the termination boundary.

### Q3: Walmart only; Amazon and eBay are synchronous

Amazon SP-API (Listings Items API) returns an ACCEPTED status synchronously in the submission
response — `AmazonMarketplaceAdapter.SubmitListingAsync` uses this to publish
`MarketplaceListingActivated` immediately (ADR 0052 confirmed this approach).

eBay Sell API uses a two-step create-offer / publish-offer flow, but `publishOffer` returns the
listing ID synchronously on success (ADR 0054). No feed polling is required.

Walmart's Item Feed API (`POST /v3/feeds?feedType=MP_ITEM`) is the only path that requires
async polling.

### Q4: Bidirectional feedback before deactivation

Without bidirectional feedback, Listings BC listings that go through the marketplace path stay
in `Submitted` status indefinitely. Deactivation (P-8 through P-12) requires a `Live` listing
to deactivate. The feedback loop must be closed first.

### Q5: Orphaned eBay draft deferred

eBay requires an inventory item record before `createOffer` can succeed. If the inventory item
is missing, `SubmitListingAsync` fails and `MarketplaceSubmissionRejected` is published — the
Listings BC correctly transitions to `Ended (SubmissionRejected)`. The "orphaned draft" scenario
(where the eBay offer is created but publish fails, leaving a stale draft offer on eBay's side)
requires an eBay offer cleanup API call (`DELETE /sell/inventory/v1/offer/{offerId}`). This is
a non-trivial resilience enhancement deferred to M38.1.

---

## Implementation — Internal Message Shape

`CheckWalmartFeedStatus` is an **internal Marketplaces BC scheduled message**. It must NOT be
added to `Messages.Contracts` (it is not a cross-BC integration contract).

```csharp
// Location: src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs
public sealed record CheckWalmartFeedStatus(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ExternalFeedId,   // raw feed ID, without the wmrt- prefix
    int AttemptCount);
```

`AttemptCount` starts at **1** (first poll, scheduled 2 minutes after feed submission).
`CheckWalmartFeedStatusHandler` increments it on reschedule.

---

## Retry Delay Schedule

| Attempt | Delay Before Next Poll |
|---------|------------------------|
| 1       | 2 minutes              |
| 2       | 5 minutes              |
| 3       | 10 minutes             |
| 4       | 20 minutes             |
| 5+      | 30 minutes             |

Cumulative maximum wait (10 attempts): approximately 2 + 5 + 10 + 20 + 30×5 = 187 minutes
(~3 hours 7 minutes) before timeout.

---

## Max-Attempt Behaviour

`MaxAttempts = 10`. When `AttemptCount >= MaxAttempts` and the feed status is still pending,
`CheckWalmartFeedStatusHandler` publishes:

```
MarketplaceSubmissionRejected(
    ListingId, Sku, ChannelCode,
    Reason: "Walmart feed processing timed out after N attempts",
    OccurredAt)
```

This terminates the polling chain. The Listings BC `MarketplaceSubmissionRejectedHandler`
transitions the listing to `Ended (SubmissionRejected)`.

---

## Idempotency

`MarketplaceListingActivatedHandler` in the Listings BC guards:

- If `listing is null` → silent no-op (listing not found; message already processed or orphaned)
- If `listing.Status == ListingStatus.Live` → silent no-op (duplicate activation message)
- If `listing.Status != ListingStatus.Submitted` → silent no-op (unexpected state; do not throw)

`MarketplaceSubmissionRejectedHandler` guards:

- If `listing is null` → silent no-op
- If `listing.Status == ListingStatus.Ended` → silent no-op (already ended)
- If `listing.Status != ListingStatus.Submitted` → silent no-op

---

## Known Limitations

1. **Amazon and eBay `CheckSubmissionStatusAsync` remain skeleton implementations.** Both
   adapters return `IsLive: false, IsFailed: false` from that method. Because neither channel
   routes through `CheckWalmartFeedStatusHandler`, the skeletons are never invoked. A future
   milestone may add real implementations if those APIs ever add async patterns.

2. **Polly resilience on `CheckSubmissionStatusAsync` is deferred to Session 2 (P-8).** The
   real Walmart HTTP call in `CheckSubmissionStatusAsync` has no retry/circuit-breaker policy.
   Transient HTTP errors return `IsFailed: true`, which causes the handler to publish
   `MarketplaceSubmissionRejected` prematurely. Session 2 wraps the call with Polly.

3. **Walmart feed-level errors vs item-level errors.** The Walmart feed API returns a feed-level
   `feedStatus`. Item-level errors (partial acceptance) require a separate call to
   `GET /v3/feeds/{feedId}?includeDetails=true`. This level of granularity is out of scope for
   M38.0. A feed-level `"ERROR"` status is treated as a full submission failure.

---

## Consequences

- Listings BC listings submitted via Walmart will no longer stay in `Submitted` state
  permanently. The first successful poll after feed processing completes will publish
  `MarketplaceListingActivated` and the Listings BC transitions to `Live`.
- Listings submitted via Amazon or eBay already activate immediately (unchanged — they publish
  `MarketplaceListingActivated` directly from `ListingApprovedHandler`).
- The Walmart polling chain terminates after at most 10 attempts (~3 hours). Timed-out
  submissions are ended with `EndedCause.SubmissionRejected`.
- `CheckWalmartFeedStatus` is a durable local Wolverine message (inbox/outbox applies) —
  it survives process restart.

---

## Alternatives Considered

| Alternative | Reason Not Chosen |
|-------------|-------------------|
| Polling saga | Saga state overhead for single-adapter need; `bus.ScheduleAsync()` is sufficient |
| Background hosted service with timer | Requires external scheduler or IHostedService; doesn't benefit from Wolverine durability |
| Listings BC polls Marketplaces BC via HTTP | Cross-BC coupling; breaks integration boundary; Marketplaces BC owns adapter mechanics |
| Fixed-interval polling (no backoff) | Wastes API quota on long-running feeds; escalating delay is friendlier to the Walmart API |
