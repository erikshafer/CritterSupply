# M38.0 Planning Session Retrospective

**Status:** ✅ Complete  
**Date:** 2026-04-03  
**Session type:** Planning (no implementation code written)  
**Output:** `docs/planning/milestones/m38-0-plan.md` committed

---

## 1. First-Act Readiness

### CI baseline confirmed

Before the planning session began, the following baseline was verified:

- **CI Run #872** (M37.0 Session 3 branch) — ✅ green
- **E2E Run #455** (M37.0 Session 3 branch) — ✅ green
- **`dotnet build`** on current branch — ✅ 0 errors, 12 warnings (all pre-existing)
- **`dotnet test` — Listings:** 35/35 passing
- **`dotnet test` — Marketplaces:** 70/70 passing
- **Combined:** 105/105 passing

This baseline is recorded in `m38-0-plan.md` Section 8.

---

## 2. Phase A Research Summary

### @PSA — Adapter Polling Behavior Audit

**Amazon SP-API findings:**
The Listings Items API `PUT /listings/2021-08-01/items/{sellerId}/{sku}` returns a synchronous
response with a `status` field (`ACCEPTED` or `INVALID`). The current `AmazonMarketplaceAdapter`
treats 2xx as success and publishes `MarketplaceListingActivated` immediately. This is correct
for the reference architecture: ACCEPTED means the listing was received and processed; the
synchronous response is the final API confirmation for the `putListingsItem` operation. A
subsequent `GET /listings/2021-08-01/items/{sellerId}/{sku}` exists to verify BUYABLE status,
but this is an additional verification step, not a required confirmation. **Amazon does not
require polling for M38.0.**

The adapter's own skeleton comment acknowledges a `GET` verification would be the "full
implementation" — this is accurate and is captured as an M38.1 enhancement (ADR 0055 will
document it as a known limitation).

**eBay findings:**
The offer publish step `POST /sell/inventory/v1/offer/{offerId}/publish` is synchronous — a 200
response means the offer is live. This is confirmed by both the eBay Sell API documentation
and the existing `EbayMarketplaceAdapter.SubmitListingAsync` implementation. The skeleton
`CheckSubmissionStatusAsync` note ("GET /sell/inventory/v1/offer/{offerId}") is for optional
status verification, not for required activation confirmation. **eBay does not require polling
for M38.0.**

**Existing skeleton review:**
- Amazon: `CheckSubmissionStatusAsync` has no feed ID — just the SKU prefixed `amzn-{sku}`. A
  real GET verification would need the seller ID from vault, making it a vault-reading operation.
- Walmart: `CheckSubmissionStatusAsync` receives `wmrt-{feedId}`. Stripping the prefix gives the
  feed ID directly. The `GET /v3/feeds/{feedId}` endpoint returns `feedStatus` with values
  RECEIVED, INPROGRESS, PROCESSED, ERROR — all the information needed for the poll.
- eBay: `CheckSubmissionStatusAsync` receives `ebay-{offerId}`. Stripping the prefix gives the
  offer ID for `GET /sell/inventory/v1/offer/{offerId}`.

**Finding that surprised the panel:** Amazon's synchronous ACCEPTED response is often misunderstood
as "pending confirmation" because the SP-API documentation also mentions the Feeds API for bulk
operations. The Listings Items API (single-item PUT) is genuinely synchronous, and the reference
architecture's current behavior is already correct. This finding reinforced the Q3 decision
(Walmart only) and avoided over-engineering.

### @QAE — Test Coverage Gap Inventory

**Skeleton-dependent tests that will need updating:**
- `WalmartMarketplaceAdapterTests.CheckSubmissionStatus_ReturnsPendingStatus` — asserts skeleton
  behavior (`IsLive: false, IsFailed: false`). Must be replaced with real polling scenario tests
  in Session 1.
- `EbayMarketplaceAdapterTests.CheckSubmissionStatus_ReturnsPendingStatus` — same skeleton
  assertion. For eBay, the decision is to defer real implementation to M38.1; this test should
  be left as-is until then (or updated with a clear `// Deferred to M38.1` comment).
- `AmazonMarketplaceAdapterTests.CheckSubmissionStatus_ReturnsPendingStatus` — same. Deferred
  to M38.1.
- All three `DeactivateListing_ReturnsFalse` tests across adapter test classes — must be
  replaced with real deactivation scenario tests in Session 2.

**Missing tests for bidirectional feedback:**
No tests exist in the Listings integration test suite for consuming `MarketplaceListingActivated`
or `MarketplaceSubmissionRejected`. The Listings BC has no handler files for these messages.
Two new integration test classes are needed in Session 1:
- `MarketplaceListingActivatedHandlerTests`
- `MarketplaceSubmissionRejectedHandlerTests`

**E2E coverage:**
Three `@wip` scenarios in `ListingsDetail.feature` exist. They cover action button flows
(approve, pause, end). They cannot pass until Session 3 enables the admin action buttons.
No E2E coverage exists for the submission→activation flow — this is correct given CI cannot
hit real marketplace APIs. Integration tests cover this path.

**No existing tests assert `MarketplaceListingActivated` or `MarketplaceSubmissionRejected`
is consumed by Listings BC** — this is the primary new test coverage gap for M38.0 and the
main test work for Session 1.

### @PO — Scope Prioritization

**Must ship (minimum viable M38.0):**
1. Walmart `CheckSubmissionStatusAsync` + polling loop (P-1 through P-3) — without this,
   Walmart listings stay in `Submitted` status forever
2. Bidirectional feedback — Listings BC consuming marketplace outcome events (P-4 through P-6) —
   without this, no marketplace listing ever reaches `Live` status in the Listings aggregate
3. Polly resilience (P-8) — the existing log-and-fail on 429 is not production-acceptable;
   this is the primary M38.0 resilience deliverable

**Should ship if capacity allows:**
4. `DeactivateListingAsync` all three adapters (P-9 through P-11) — completes the adapter
   contract; high value for reference architecture completeness
5. Admin UI buttons (P-13) + E2E unblock (P-14) — visible to operators; directly improves
   the Backoffice experience

**Deferred (not in M38.0):**
- Amazon/eBay `CheckSubmissionStatusAsync` real implementations — synchronous path is correct
- Orphaned eBay draft offer cleanup — documented edge case; M38.1 target
- `BasePrice` gap — blocked on Pricing BC; not an M38.0 dependency
- `SemaphoreSlim` base class refactor — technical debt; fine to defer one more milestone

---

## 3. The Five Decisions

| Q# | Decision | Confidence |
|----|----------|------------|
| Q1 | Marketplaces BC owns polling (option a) | High — BC ownership is clear; adapter concepts stay in adapter BC |
| Q2 | Per-submission scheduled message (option a) | High — Walmart only; batch saga would over-engineer for one adapter |
| Q3 | Walmart only; Amazon and eBay synchronous | High — confirmed by code audit; Amazon ACCEPTED is final for submission |
| Q4 | Sequence: bidirectional feedback (S1) then deactivation (S2) | High — natural dependency order; S1 establishes Live state |
| Q5 | Document and defer eBay orphaned draft cleanup (option c) | Medium-high — edge case well-documented in ADR 0054; M38.1 is the right venue |

No decision required the "take the recommended option and move on" fallback. All five were
resolved by the panel with supporting rationale from the research findings.

---

## 4. Items Considered and Explicitly Deferred

| Item | Considered for | Deferred because |
|------|---------------|-----------------|
| Amazon `GET /listings/...` BUYABLE verification | M38.0 Session 1 | Synchronous ACCEPTED response is correct; GET verification is enhancement, not a correctness fix. Deferred to M38.1. |
| eBay `GET /sell/inventory/v1/offer/{offerId}` status check | M38.0 Session 1 | Publish is synchronous; no polling needed for activation path. Deferred to M38.1. |
| Orphaned eBay draft retry saga | M38.0 | Edge case; meaningful complexity for a rare failure. Background sweep (M38.1) is a better approach than a retry saga. |
| Generalized multi-adapter polling saga | M38.0 | Only Walmart needs polling. Generic infrastructure for one adapter is over-engineering. Re-evaluate when a second async adapter is added. |
| `SemaphoreSlim` token cache base class | M38.0 | Three copies today; still acceptable. A fourth adapter would push this to mandatory. Captured as M37.0 lesson 3. |

---

## 5. Session Count and Sequencing

**M38.0 is sequenced as 4 sessions (3 implementation + 1 closure):**

- **Session 1:** Walmart polling + Bidirectional feedback — P-1 through P-7 — ~15 to 20 new tests
- **Session 2:** Polly resilience + DeactivateListingAsync — P-8 through P-12 — ~15 to 20 new tests
- **Session 3:** Admin UI + E2E unblock — P-13, P-14 — 3 E2E scenarios active
- **Session 4:** Milestone closure — no implementation code

The sequencing was driven by Q4's decision: bidirectional feedback (inbound) before deactivation
(outbound). Polly resilience pairs naturally with deactivation in Session 2 because both are
"complete the adapter contract" work and share similar test infrastructure (429 simulation).

---

## 6. Session 1 First-Act Verification

Before Session 1 implementation begins, the agent should verify:

1. `dotnet build` returns 0 errors
2. `dotnet test` on Listings and Marketplaces integration test projects: 105 tests, 0 failures
3. Check that no implementation code was written in the planning session (grep for any new `.cs`
   files in `src/` or `tests/` with today's timestamp)
4. Read `docs/planning/milestones/m38-0-plan.md` from the top — specifically Sections 2 (Decision
   Log), 3 (Scope Table), and 4 (Session 1 scope detail)
5. Author ADR 0055 **before** writing any polling code
