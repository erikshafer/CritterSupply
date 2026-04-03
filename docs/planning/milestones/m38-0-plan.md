# M38.0 Plan: Async Lifecycle + Resilience

**Status:** 🚀 IN PROGRESS — Planning session complete; Session 1 ready  
**Authored:** 2026-04-03  
**Planning session CI baseline:** CI Run #872 (green), E2E Run #455 (green)  
**Build baseline at plan date:** 0 errors, 12 warnings (all pre-existing)  
**Test baseline at plan date:** 105 integration tests — 35 Listings + 70 Marketplaces — 0 failures  
**Next ADR:** 0055

---

## 1. Goal Statement

M37.0 delivered Phase 3: three production marketplace adapters (Amazon, Walmart, eBay) that can
authenticate and submit a listing to an external API. The submission call exists; what does not exist
yet is everything that happens after it. M38.0 delivers Phase 4: it completes the production adapter
lifecycle by adding async submission status polling for Walmart (the only adapter with a truly async
feed-based submission), rate limiting and retry resilience via Polly on all three adapter `HttpClient`
pipelines, bidirectional marketplace feedback so that the Listings BC knows when a listing is live or
rejected, full `DeactivateListingAsync` implementations on all three adapters, and the admin action
buttons that unblock the Backoffice UI and the three `@wip` E2E scenarios deferred since M36.1.

The arc from Phase 3 (M36.1 stubs → M37.0 real adapters) to Phase 4 (M38.0 complete lifecycle) closes
the loop: a listing created in the Backoffice can now be approved, submitted to Amazon/Walmart/eBay,
confirmed live by the marketplace, reflected as `Live` in the Listings aggregate, and later deactivated
via a platform-side call — with resilience against transient failures at every step.

**Note on divergence from the original cycle plan:** `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
originally scoped Phase 4 (Cycles 34–35) as variants + compliance + real API calls. The actual roadmap
diverged: M37.0 delivered real API adapters as Phase 3, and M38.0 is the resilience and lifecycle
completion layer. Future readers should treat the cycle plan as historical context, not as a contract.

---

## 2. Decision Log

All five open questions from `m38-x-planning-notes.md` were resolved in Phase B of this planning
session. Decisions are recorded here verbatim and are not to be re-litigated in implementation
sessions.

### Q1: Does polling belong in Marketplaces BC or Listings BC?

**Decision: (a) Marketplaces BC owns polling.**

Rationale: The marketplace adapter (and the adapter-specific concepts like Walmart feed IDs, eBay offer
IDs, and the `GET /v3/feeds/{feedId}` endpoint) are owned by the Marketplaces BC. The polling outcome is
naturally modeled as the same integration messages already defined — `MarketplaceListingActivated` and
`MarketplaceSubmissionRejected` — which are the Marketplaces BC's public contract with the Listings BC.
Listings BC does not need to know which adapter called which endpoint; it only needs to know the outcome.
This preserves bounded context ownership: Marketplaces BC owns the adapter; Listings BC owns the
aggregate state machine. Moving polling to Listings BC would require Listings BC to understand feed IDs,
adapter-specific poll endpoints, and adapter selection by channel code — all of which violate its
boundary.

### Q2: Polling architecture — per-submission scheduled message or batch polling saga?

**Decision: (a) Per-submission scheduled message via Wolverine `bus.ScheduleAsync()`.**

Rationale: Only Walmart requires polling (see Q3). A batch polling saga would require a
`PendingSubmissions` tracking document or projection query and a recurring background message — adding
meaningful complexity for a single adapter's needs. Per-submission maps cleanly to Wolverine's
`bus.ScheduleAsync()` pattern already established in the Orders saga, keeps each poll message
self-contained (the feed ID and listing ID are in the message), and naturally handles timeouts by
counting retry attempts in the scheduled message payload. If a fourth adapter requiring async polling
were added in a future milestone, the pattern is already established and the same mechanism applies
without a new saga design.

### Q3: Which adapters actually need polling?

**Decision: Walmart only. Amazon and eBay do not require polling in M38.0.**

Rationale:
- **Walmart (polling required):** Feed-based submission is explicitly async. `SubmitListingAsync`
  returns a feed ID. Processing occurs asynchronously on Walmart's side. `GET /v3/feeds/{feedId}`
  is required to determine PROCESSED vs ERROR status before publishing `MarketplaceListingActivated`.
  Polling is mandatory for correctness.
- **Amazon (not required for M38.0):** The Listings Items API `PUT .../items/{sku}` returns a
  synchronous ACCEPTED response. The current adapter treats 2xx as success. A subsequent
  `GET /listings/2021-08-01/items/{sellerId}/{sku}` could verify BUYABLE status, but this is a
  "belt and suspenders" confirmation for a case where the synchronous 2xx is the dominant and
  correct outcome. Implementing this would require a per-SKU scheduled GET after submission.
  The adapter code note ("a full implementation would call GET to verify the listing is active")
  is correct but deferred to M38.1 as an enhancement, not a correctness requirement.
- **eBay (not required):** The offer publish step
  (`POST /sell/inventory/v1/offer/{offerId}/publish`) is synchronous — a 200 response means the
  offer is live. No async confirmation is needed. `CheckSubmissionStatusAsync` for eBay could
  serve as a "verify existing offer status" operation but is not needed for the activation path.
  Deferred to M38.1.

This decision shapes the architecture: the polling infrastructure is Walmart-specific, not a
generalized multi-adapter system. This is intentional — YAGNI applies until a second async adapter
is added.

### Q4: Should `DeactivateListingAsync` be completed alongside bidirectional feedback, or separately?

**Decision: Sequence them. Session 1 delivers bidirectional feedback (inbound); Session 2 delivers
deactivation (outbound).**

Rationale: Bidirectional feedback (Listings BC consuming `MarketplaceListingActivated` and
`MarketplaceSubmissionRejected`) is the higher-priority item — without it, the Listings aggregate
never transitions to `Live` for marketplace channels. Delivering both bidirectional feedback and
deactivation in a single session would make that session very large (it would also include Walmart
polling, the `ListingApprovedHandler` modification, and the Listings BC subscription wiring).
Sequencing them allows Session 1 to be focused and deliverable end-to-end. Deactivation depends
on having the `Live` state established (which bidirectional feedback delivers), so it naturally
follows in Session 2 alongside Polly resilience.

### Q5: Orphaned eBay draft offer cleanup — what triggers it?

**Decision: (c) Document and defer. Not in M38.0 scope.**

Rationale: An orphaned draft offer (create succeeds, publish fails) is a genuine edge case but not
a frequent operational concern. It is already documented in ADR 0054. For the reference architecture,
operators can handle orphaned drafts via the eBay Seller Hub. A retry saga or background sweep adds
meaningful complexity (new saga state, eBay query endpoint, delete/republish logic) for an edge case
that does not affect the dominant submission path. This will be captured in ADR 0055 as a known
limitation with a recommended future approach (option b: background sweep) and targeted at M38.1.

---

## 3. Scope Table

### In Scope for M38.0

| ID | BC | Description | Depends On | Session |
|----|-----|-------------|------------|---------|
| P-1 | Marketplaces | `CheckSubmissionStatusAsync` for Walmart: poll `GET /v3/feeds/{feedId}`, map PROCESSED → `IsLive: true`, ERROR → `IsFailed: true`, RECEIVED/INPROGRESS → pending | — | S1 |
| P-2 | Marketplaces | Modify `ListingApprovedHandler` Walmart path: on feed submission success, schedule `CheckWalmartFeedStatus` message via `bus.ScheduleAsync()` instead of publishing `MarketplaceListingActivated` | P-1 | S1 |
| P-3 | Marketplaces | `CheckWalmartFeedStatus` scheduled message handler: polls adapter, reschedules if pending, publishes `MarketplaceListingActivated` or `MarketplaceSubmissionRejected` on resolution; max retry count | P-1, P-2 | S1 |
| P-4 | Listings | Subscribe to `marketplaces-listing-outcome-events` queue in `Listings.Api/Program.cs` | — | S1 |
| P-5 | Listings | `MarketplaceListingActivatedHandler`: consume `MarketplaceListingActivated` from Marketplaces BC, dispatch `ActivateListing` (or directly append `ListingActivated` event), transition listing to `Live` | P-4 | S1 |
| P-6 | Listings | `MarketplaceSubmissionRejectedHandler`: consume `MarketplaceSubmissionRejected`, append `ListingEnded` event with `EndedCause.SubmissionRejected`, publish `ListingEnded` integration message | P-4 | S1 |
| P-7 | ADR | ADR 0055: Submission Status Polling Architecture (answers Q1 + Q2 + Q3) | P-1, P-2, P-3 | S1 |
| P-8 | Marketplaces | Polly retry policies via `Microsoft.Extensions.Http.Resilience` on `AmazonSpApi`, `WalmartApi`, `EbayApi` named `HttpClient` pipelines; per-adapter rate limit and retry configuration | — | S2 |
| P-9 | Marketplaces | `DeactivateListingAsync` for Walmart: retire item via lifecycle status feed (`lifecycleStatus: "RETIRED"`) or Retire Item API | — | S2 |
| P-10 | Marketplaces | `DeactivateListingAsync` for Amazon: `PATCH /listings/2021-08-01/items/{sellerId}/{sku}` with `op: delete` on `purchasable_offer` attribute | — | S2 |
| P-11 | Marketplaces | `DeactivateListingAsync` for eBay: `POST /sell/inventory/v1/offer/{offerId}/withdraw` | — | S2 |
| P-12 | ADR | ADR 0056: Marketplace Adapter Resilience Patterns (rate limiting, retry, circuit breaker) | P-8 | S2 |
| P-13 | Backoffice | Listings admin action buttons (approve/pause/end) on Backoffice detail page — currently disabled stubs | P-5, P-6 | S3 |
| P-14 | E2E | Unblock `@wip` E2E scenarios in `ListingsDetail.feature` (3 scenarios for action button flows) | P-13 | S3 |

### Out of Scope for M38.0

| Item | Deferral Target | Reason |
|------|----------------|--------|
| Amazon `CheckSubmissionStatusAsync` real implementation (GET to verify BUYABLE status) | M38.1 | Synchronous acceptance sufficient; enhancement not a correctness requirement |
| eBay `CheckSubmissionStatusAsync` real implementation (GET offer status verification) | M38.1 | Publish is synchronous; status check is optional verification |
| Orphaned eBay draft offer cleanup (retry saga or background sweep) | M38.1 | Edge case; documented in ADR 0055 with recommended future approach |
| `BasePrice` gap in Marketplaces and Listings `ProductSummaryView` | Future (Pricing BC evolution) | Pricing BC must publish price events first; not an M38.0 dependency |
| `SemaphoreSlim` token cache refactor to shared base class | M38.1 | Captured as M37.0 lesson 3; three copies today; extract when adding fourth adapter |

---

## 4. Session Plan

### Session 1 — Walmart Polling + Bidirectional Feedback

**Title:** Walmart polling infrastructure + Listings BC marketplace feedback handlers  
**Deliverables:** P-1, P-2, P-3, P-4, P-5, P-6, P-7  
**Agent assignments:** @PSA (polling architecture, handler modifications), @QAE (test coverage for all new handlers and polling scenarios)  
**Entry gate:** Build 0 errors; 105 tests passing; no implementation code written in the planning session

**Session scope detail:**

1. Author ADR 0055 before writing any polling code.
2. Implement `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync`: call `GET /v3/feeds/{feedId}`,
   parse `feedStatus` field, return appropriate `SubmissionStatus`. Feed ID is extracted from the
   `wmrt-{feedId}` prefix convention.
3. Define `CheckWalmartFeedStatus` scheduled message record (internal to Marketplaces BC, not an
   integration message — does not belong in `Messages.Contracts`).
4. Modify `ListingApprovedHandler`: for the Walmart channel (`WALMART_US`), after a successful feed
   submission, do **not** publish `MarketplaceListingActivated` — instead use `IMessageContext` or
   `IMessageBus` to schedule a `CheckWalmartFeedStatus` message with an initial 2-minute delay.
   Amazon and eBay paths remain unchanged (synchronous publish on success). The handler signature
   may need to be updated to accept `IMessageContext` for scheduling.
5. Implement `CheckWalmartFeedStatusHandler`: calls adapter `CheckSubmissionStatusAsync`, publishes
   `MarketplaceListingActivated` or `MarketplaceSubmissionRejected` on resolution, reschedules
   with backoff if still pending. Include a max-attempt guard (e.g., 10 retries × escalating
   delay = ~1 hour max wait) after which publish `MarketplaceSubmissionRejected` with a timeout
   reason.
6. In `Listings.Api/Program.cs`, add `opts.ListenToRabbitQueue("marketplaces-listing-outcome-events")`.
7. Implement `MarketplaceListingActivatedHandler` in Listings BC (domain assembly): load listing by
   stream ID (compute from `ListingId`), dispatch `ActivateListing`, or directly append `ListingActivated`
   if the state machine allows. Transitions listing to `Live`.
8. Implement `MarketplaceSubmissionRejectedHandler` in Listings BC (domain assembly): load listing,
   append `ListingEnded(EndedCause.SubmissionRejected)`, publish `ListingEnded` integration message.

**Tests to add in Session 1 (target: +15 to +20):**
- `WalmartMarketplaceAdapterTests`: replace `CheckSubmissionStatus_ReturnsPendingStatus` with real
  tests: PROCESSED → `IsLive: true`; ERROR → `IsFailed: true`; INPROGRESS → pending (both false)
- `WalmartPollingHandlerTests` (new file in Marketplaces integration tests): `CheckWalmartFeedStatus`
  handler publishes activated on PROCESSED; publishes rejected on ERROR; reschedules on INPROGRESS;
  publishes rejected after max retries
- New test file in Listings integration tests: `MarketplaceListingActivatedHandlerTests` —
  Submitted listing → activated event → Live status; already-Live listing is a no-op
- New test file in Listings integration tests: `MarketplaceSubmissionRejectedHandlerTests` —
  Submitted listing → ended with SubmissionRejected cause; integration message published

### Session 2 — Polly Resilience + DeactivateListingAsync

**Title:** Polly retry policies on all three adapters + `DeactivateListingAsync` full implementation  
**Deliverables:** P-8, P-9, P-10, P-11, P-12  
**Agent assignments:** @PSA (Polly wiring, deactivation API endpoints), @QAE (retry simulation tests, deactivation tests)  
**Entry gate:** Session 1 complete; P-1 through P-7 delivered and passing

**Session scope detail:**

1. Author ADR 0056 before writing any Polly code.
2. Add `Microsoft.Extensions.Http.Resilience` (wrapping Polly) to `Marketplaces` project.
3. Register per-adapter resilience pipelines in `Marketplaces.Api/Program.cs` using
   `AddResilienceHandler()` on named `HttpClient` registrations. Per-adapter rate limits:
   Amazon 5 req/s (burst 10), Walmart 10 req/s, eBay varies by endpoint.
4. 401 special case: a 401 on the token endpoint should NOT be retried (invalid credentials);
   a 401 on the API endpoint SHOULD trigger a token cache clear and single retry. Token
   caching with `SemaphoreSlim` already handles this correctly — the retry policy must not
   bypass token acquisition.
5. Circuit breaker: per-adapter, opens after 5 consecutive failures within 30 seconds. Decision
   on scope (per-adapter vs global) is ADR 0056 — recommend per-adapter for precision.
6. `FakeHttpMessageHandler` needs 429 simulation support (enqueue a 429 response, verify the retry
   pipeline fires; the second response is 200). Add `EnqueueResponseForStatusCode` or enrich
   existing `EnqueueResponse` to support this.
7. Implement `DeactivateListingAsync` for Walmart: determine correct API (Retire Item API vs feed
   with `lifecycleStatus: "RETIRED"`). Feed-based retirement is consistent with the submission
   model. POST a retire feed. Return `true` on success.
8. Implement `DeactivateListingAsync` for Amazon: `PATCH /listings/2021-08-01/items/{sellerId}/{sku}`
   with `patches: [{ "op": "delete", "path": "/attributes/purchasable_offer" }]`. The `externalListingId`
   carries `amzn-{sku}` — strip the prefix to get the SKU. Return `true` on success.
9. Implement `DeactivateListingAsync` for eBay: `POST /sell/inventory/v1/offer/{offerId}/withdraw`.
   The `externalListingId` carries `ebay-{offerId}` — strip the prefix. Return `true` on success.

**Tests to add in Session 2 (target: +15 to +20):**
- Per-adapter deactivation tests (3 tests × 3 adapters = 9):
  success path, failure path, and HTTP request verification
- Polly retry tests: 429 → retry → success returns success; 429 × max retries returns failure
- Token refresh on 401: API 401 clears cache and retries once; token endpoint 401 propagates failure
- Deactivation tests: update `DeactivateListing_ReturnsFalse` in each adapter test class to
  assert `true` and verify the correct HTTP request was sent

### Session 3 — Admin UI Buttons + E2E Unblock

**Title:** Listings Backoffice action buttons + E2E scenario activation  
**Deliverables:** P-13, P-14  
**Agent assignments:** @PSA or @frontend-platform-engineer (Backoffice UI), @QAE (E2E scenario verification)  
**Entry gate:** Session 2 complete; P-5 and P-6 delivered so action outcomes can be reflected in UI

**Session scope detail:**

1. Enable the approve/pause/end action buttons on the Listings Backoffice detail page. Currently
   these are rendered as disabled stubs (from M36.1 Session 4). Wire them to the existing
   `ApproveListing`, `PauseListing`, and `EndListing` endpoints in `Listings.Api`.
2. Remove `@wip` tags from the three `ListingsDetail.feature` E2E scenarios and implement/fix
   any Gherkin step definitions needed to make them pass.
3. Confirm all three scenarios pass on the E2E shard (`@shard-3`).

**Tests:** 3 previously `@wip` E2E scenarios become active (net +3 to E2E count)

### Session 4 — Milestone Closure

**Title:** M38.0 milestone closure  
**Deliverables:** Retrospective, CONTEXTS.md update (if needed), M38.1 pre-planning notes, CURRENT-CYCLE.md update  
**Agent assignments:** @PSA + @PO (collaborative)  
**Entry gate:** Session 3 complete; all P-1 through P-14 delivered and CI green

No implementation code is written in Session 4. The session confirms the final CI run, authors the
milestone closure retrospective, updates `CURRENT-CYCLE.md`, and authors M38.1 pre-planning notes.

---

## 5. Test Plan

**Starting baseline:** 105 integration tests (35 Listings + 70 Marketplaces), 0 failures

### Session 1 targets

| Project | New test classes | Estimated new tests |
|---------|-----------------|---------------------|
| Marketplaces integration tests | `WalmartMarketplaceAdapterTests` (extend), `WalmartPollingHandlerTests` (new) | +8 to +10 |
| Listings integration tests | `MarketplaceListingActivatedHandlerTests` (new), `MarketplaceSubmissionRejectedHandlerTests` (new) | +6 to +8 |

Post-S1 estimate: **~120 tests**

### Session 2 targets

| Project | New test classes | Estimated new tests |
|---------|-----------------|---------------------|
| Marketplaces integration tests | Deactivation tests in all 3 adapter test classes, Polly retry tests | +12 to +18 |

Post-S2 estimate: **~135 tests**

### Session 3 targets

| Project | New test classes | Estimated new tests |
|---------|-----------------|---------------------|
| Backoffice E2E | `ListingsDetail.feature` @wip removal | +3 E2E scenarios active |

Post-S3 (M38.0 close) estimate: **≥ 135 integration tests**, **3 additional E2E scenarios active**

### E2E coverage

M38.0 does not add new E2E feature files. It unblocks the three existing `@wip` scenarios in
`ListingsDetail.feature`. The submission→activation flow (Listings BC consumes `MarketplaceListingActivated`
→ listing status becomes Live) is tested via integration tests in Sessions 1 and 2, not via E2E.
Adding an E2E scenario for the full submission→Walmart-polling→activation flow would require a
real Walmart sandbox, which is outside the reference architecture's CI constraints. This is not
in M38.0 scope.

---

## 6. ADR Schedule

| ADR | Title | Session | Author before... |
|-----|-------|---------|-----------------|
| 0055 | Submission Status Polling Architecture | S1 | Any polling code (P-1, P-2, P-3) |
| 0056 | Marketplace Adapter Resilience Patterns | S2 | Any Polly code (P-8) |

**ADR 0055 key content:** Q1 decision (Marketplaces BC owns polling), Q2 decision (per-submission
scheduled message), Q3 decision (Walmart only; Amazon/eBay synchronous), `CheckWalmartFeedStatus`
message design, retry count and delay strategy, orphaned eBay draft known limitation with recommended
M38.1 approach (background sweep).

**ADR 0056 key content:** Per-adapter Polly pipeline configuration, rate limit values per ADRs
0052/0053/0054, 401 retry special case (token refresh), circuit breaker scope decision (per-adapter),
`Microsoft.Extensions.Http.Resilience` package rationale.

---

## 7. Definition of Done

M38.0 is complete when **all** of the following are true:

1. **Walmart polling implemented:** `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` polls
   `GET /v3/feeds/{feedId}`, maps PROCESSED → `IsLive: true`, ERROR → `IsFailed: true`, and
   RECEIVED/INPROGRESS → pending. Tests cover all four status values.

2. **Walmart async submission loop closed:** `ListingApprovedHandler` for Walmart channel schedules
   `CheckWalmartFeedStatus` via `bus.ScheduleAsync()` on feed submission success instead of
   immediately publishing `MarketplaceListingActivated`. The poll handler resolves and publishes
   the outcome event. Amazon and eBay paths remain synchronous and unaffected.

3. **Listings BC bidirectional feedback wired:** Listings BC subscribes to marketplace outcome events
   queue; `MarketplaceListingActivatedHandler` transitions listing from `Submitted` to `Live`;
   `MarketplaceSubmissionRejectedHandler` transitions listing to `Ended` (cause: `SubmissionRejected`).
   Integration tests verify both handlers end-to-end.

4. **Polly retry policies registered:** `Microsoft.Extensions.Http.Resilience` configured on all three
   real adapter `HttpClient` pipelines (`AmazonSpApi`, `WalmartApi`, `EbayApi`) with per-adapter
   rate limits. 429 responses trigger exponential backoff with jitter. The 401 token refresh
   special case is handled correctly (API 401 retries once; token endpoint 401 propagates failure).

5. **`DeactivateListingAsync` fully implemented on all three adapters:** Walmart retire (feed-based),
   Amazon PATCH delete purchasable_offer, eBay offer withdraw. Each returns `true` on success.
   Deactivation tests verify the correct HTTP request is built and sent.

6. **Listings admin action buttons enabled:** Approve/Pause/End buttons on the Backoffice Listings
   detail page are wired to their respective API endpoints and no longer rendered as disabled stubs.

7. **`@wip` E2E scenarios active:** All three `@wip` scenarios in `ListingsDetail.feature` pass
   on `@shard-3` in CI.

8. **Integration test count ≥ 135;** build: 0 errors; `dotnet test` on Listings and Marketplaces
   projects: 0 failures.

---

## 8. Codebase State at M38.0 Start

- **Solution:** 19 bounded contexts, `CritterSupply.slnx`
- **Integration tests:** 105 total — 35 Listings + 70 Marketplaces; 0 failures
- **E2E scenarios:** 6 active (MarketplacesAdmin.feature `@shard-3`, ListingsAdmin.feature `@shard-3`) + 3 `@wip` in ListingsDetail.feature
- **Production adapters:** Amazon, Walmart, eBay — all behind `Marketplaces:UseRealAdapters` flag
- **`CheckSubmissionStatusAsync`:** Skeleton on all 3 adapters — returns `IsLive: false, IsFailed: false`
- **`DeactivateListingAsync`:** Skeleton on all 3 adapters — returns `false`
- **Bidirectional feedback:** Listings BC does not subscribe to Marketplaces BC queue; no handlers for `MarketplaceListingActivated` or `MarketplaceSubmissionRejected`
- **Build:** 0 errors, 12 warnings (pre-existing: NU1504 duplicate package refs, CS0219 in Correspondence, CS8602 in Backoffice.Web)
- **Next ADR:** 0055

---

## References

- [M38.x Pre-Planning Notes](./m38-x-planning-notes.md)
- [M37.0 Milestone Closure Retrospective](./m37-0-milestone-closure-retrospective.md)
- [ADR 0052](../../decisions/0052-amazon-spapi-authentication.md) — Amazon SP-API; polling deferred note
- [ADR 0053](../../decisions/0053-walmart-marketplace-api-authentication.md) — Walmart; feed-based async
- [ADR 0054](../../decisions/0054-ebay-sell-api-authentication.md) — eBay; two-step submit; orphaned draft note
- [Catalog-Listings-Marketplaces Cycle Plan](../catalog-listings-marketplaces-cycle-plan.md) — Phase 4 original scope (treat as context only)
