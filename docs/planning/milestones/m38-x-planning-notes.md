# M38.x Pre-Planning Notes

**Authored:** 2026-04-03
**Source:** M37.0 Milestone Closure Session (C-4)
**Purpose:** Prevent M38.x from starting cold. These are structured notes, not a plan. Implementation details belong in the M38.x plan document that will be authored in its own planning session.

---

## What Phase 4 Means

M37.0 delivered Phase 3: three production marketplace adapters (Amazon, Walmart, eBay) that can authenticate and submit a listing to an external API. Phase 4 — M38.x — makes those adapters production-reliable. The submission call exists; what doesn't exist yet is everything that happens after it:

- **Async status polling** — knowing whether the submission was accepted, rejected, or is still processing
- **Resilience** — handling rate limits, transient failures, and token edge cases without crashing or silently losing submissions
- **Bidirectional feedback** — propagating marketplace outcomes back to the Listings BC so listing state reflects reality

Phase 4 does not add new marketplace integrations. It completes the existing three.

---

## Known Scope (from M37.0 Debt Table)

The following items were explicitly deferred from M37.0 and are the primary candidates for M38.x:

| Item | Complexity note |
|------|----------------|
| **Async submission status polling** — `CheckSubmissionStatusAsync` saga using Wolverine `bus.ScheduleAsync()` | Medium-high. Walmart's feed-based submission requires polling; Amazon and eBay may not. Design before building (see Adapter Polling section below). |
| **Rate limiting / retry logic** — Polly delegating handler on `HttpClient` pipelines | Medium. Polly is the idiomatic .NET approach. Per-adapter rate limits differ; Polly policies should be named and registered per adapter. |
| **`DeactivateListingAsync` full implementation** — all three adapters return skeleton `false` | Medium. Depends on bidirectional feedback design: deactivation is the outbound side of the `MarketplaceListingActivated` → `Live` → `Paused`/`Ended` feedback loop. |
| **Bidirectional marketplace feedback** — Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected` | Medium. Integration message contracts need authoring; Listings aggregate needs new Apply methods for marketplace-driven state transitions. |
| **Orphaned eBay draft offer cleanup** — publish step fails after create succeeds | Low-medium. A compensation pattern (retry saga or background sweep) needed. Depends on whether M38.x builds a polling saga first. |
| **Listings admin action buttons** — approve/pause/end are disabled stubs on the Backoffice detail page | Low. UI wiring; depends on bidirectional feedback being in place so action outcomes can be reflected. |
| **`@wip` E2E scenarios in ListingsDetail.feature** — 3 scenarios for action button flows | Low. Unblock after admin buttons are enabled. |
| **`BasePrice` gap** — Pricing BC integration events not yet consumed by `ProductSummaryView` | Dependency on Pricing BC evolution; not an M38.x item unless Pricing publishes new events. |

---

## Adapter Polling Design Considerations

`CheckSubmissionStatusAsync` is a skeleton on all three adapters. Before building polling infrastructure, confirm which adapters actually require it:

**Walmart** — Feed-based submission is inherently async. `SubmitListingAsync` returns a feed ID (`wmrt-` prefix). The feed processing is asynchronous on Walmart's side — the submission is not confirmed until the feed is processed. Polling `GET /v3/feeds/{feedId}` is required to determine success or failure. **Polling is needed.**

**Amazon** — The Listings Items API (`PUT /listings/2021-08-01/items/{sellerId}/{sku}`) returns a synchronous response indicating whether the listing was accepted or rejected. The adapter currently treats a `2xx` response as success. Verify whether Amazon's synchronous response is the final status or whether a separate feed status poll is needed before building polling infrastructure for Amazon. **Confirm before building.**

**eBay** — The offer publish step (`POST /sell/inventory/v1/offer/{offerId}/publish`) also appears to be synchronous — success means the offer is live. Polling may not be needed. **Confirm before building.**

Design question for the planning session: if only Walmart requires polling, does the polling infrastructure belong in a Walmart-specific handler or in a generalized `CheckSubmissionStatusAsync` saga that all adapters participate in (with Amazon/eBay returning immediately)?

---

## Resilience Design Considerations

All three adapters currently log-and-fail on `429 Too Many Requests` and `5xx` responses. This is acceptable for a reference architecture at Phase 3 but not for a production-reliable system.

**Polly** is the conventional .NET approach. The `Microsoft.Extensions.Http.Resilience` package (wrapping Polly) integrates with named `HttpClient` pipelines and is the recommended starting point.

**Per-adapter rate limits differ:**
- Amazon SP-API (`putListingsItem`): 5 req/s, burst 10
- Walmart Marketplace API: 10 req/s (per their developer docs as of writing)
- eBay Sell API: varies by endpoint; check current limits before hardcoding

**Token refresh edge cases:**
- A `401` response should trigger a token refresh and a single retry — but a `401` on the token exchange endpoint itself should not be retried (invalid credentials, not a transient error)
- Token caching with `SemaphoreSlim` is already in place on all three adapters; the retry policy should re-enter the token acquisition path, not bypass it

**Circuit breaker:** Consider whether a circuit breaker makes sense at the adapter level (per-marketplace) or at the Marketplaces BC level (stops all submissions if the external system is down). The per-marketplace approach is more precise but more complex.

---

## Open Questions for the M38.x Planning Session

1. **Does polling belong in Marketplaces BC or Listings BC?** Polling is about whether a marketplace submission is live — which is a marketplace concern. But the outcome (listing is live on Amazon) updates Listing aggregate state — which is a Listings BC concern. The answer shapes which BC owns the polling saga and which publishes/receives the outcome events.

2. **Polling architecture: per-submission scheduled message or batch polling saga?** Wolverine `bus.ScheduleAsync(new CheckWalmartFeedStatus(feedId), delay)` per submission is simple but creates N messages in flight for N active submissions. A batch saga that polls all pending submissions on a schedule is more efficient but more complex. Which is right for the reference architecture?

3. **Which adapters actually need polling?** Confirm Amazon's Listings Items API is synchronous before building polling infrastructure for it. If only Walmart needs polling, a generalized polling saga may be over-engineering.

4. **Should `DeactivateListingAsync` be completed in M38.x as part of bidirectional feedback, or separately?** Deactivation is the outbound direction of the feedback loop. If M38.x delivers `MarketplaceListingActivated` → Listings BC, it naturally follows to deliver `DeactivateListing` as part of the same vertical slice (Listings pauses/ends → Marketplaces deactivates).

5. **Orphaned eBay draft offer cleanup — what triggers it?** Options: (a) a retry saga that detects create-without-publish after a timeout, (b) a background sweep that queries eBay for unpublished offers and publishes or deletes them, (c) treat as non-issue for reference architecture and document the edge case. The planning session should make this choice explicit.

---

## Codebase State at M38.x Start

- **Solution:** 19 bounded contexts, `CritterSupply.slnx`
- **Integration tests:** 105 total — 35 Listings + 70 Marketplaces; 0 failures
- **E2E scenarios:** `MarketplacesAdmin.feature` (6 scenarios, `@shard-3`), `ListingsAdmin.feature` (`@shard-3`), `ListingsDetail.feature` (`@shard-3`, 3 `@wip` scenarios)
- **Production adapters:** Amazon, Walmart, eBay — all behind `Marketplaces:UseRealAdapters` flag
- **Stub adapters:** Amazon, Walmart, eBay stubs — active in Development/CI
- **Build:** 0 errors, 12 warnings (all pre-existing)
- **Next ADR:** 0055

---

## References

- [M37.0 Milestone Closure Retrospective](./m37-0-milestone-closure-retrospective.md) — Section 5 (debt table) and Section 6 (inherited codebase state)
- [ADR 0052](../../decisions/0052-amazon-spapi-authentication.md) — Amazon SP-API; polling deferred note
- [ADR 0053](../../decisions/0053-walmart-marketplace-api-authentication.md) — Walmart; feed-based submission async nature
- [ADR 0054](../../decisions/0054-ebay-sell-api-authentication.md) — eBay; two-step create+publish; orphaned draft note
- [docs/planning/catalog-listings-marketplaces-cycle-plan.md](../catalog-listings-marketplaces-cycle-plan.md) — Phase 4 scope originally scoped here
