# M38.0 Milestone Closure Retrospective

**Status:** ✅ Complete
**Date:** 2026-04-03
**Sessions:** 4 (Sessions 1–3 implementation; Session 4 closure)
**Duration:** 2026-04-03 (single day, four sequential sessions)

---

## 1. Goal Statement and Outcome

**Goal:** Complete the production adapter lifecycle — Phase 4 of the Catalog–Listings–Marketplaces arc. M37.0 delivered adapters that can authenticate and submit a listing. M38.0 closes the loop: async submission status polling for Walmart, Polly resilience on all three adapter pipelines, bidirectional marketplace feedback so the Listings BC reflects marketplace outcomes, `DeactivateListingAsync` implementations, and admin action buttons in the Backoffice UI.

**Outcome: M38.0 is complete.** All 14 plan items (P-1 through P-14) were delivered across three implementation sessions. Two Definition of Done criteria are amber — documented gaps with clear M38.1 paths, not blocking issues.

**Amber DoD items:**

- **Criterion 5 (DeactivateListingAsync fully implemented on all three adapters):** Amazon (PATCH delete purchasable_offer) and eBay (POST offer withdraw) are fully implemented. Walmart returns `false` with a logged warning due to a documented interface design limitation: the `externalListingId` encodes the feed ID (`wmrt-{feedId}`), but the RETIRE_ITEM feed requires the item SKU. The feed ID cannot be reverse-mapped to the SKU at the adapter level. This is an interface design gap (ADR 0056), not an adapter implementation bug. Resolution is the primary M38.1 design decision.

- **Criterion 8 (integration test count ≥ 135):** Final count is 133 (92 Marketplaces + 41 Listings). The shortfall of 2 tests traces directly to the Walmart deactivation gap — three TODO tests in `WalmartMarketplaceAdapterTests.cs` cannot be written until the SKU delivery mechanism is designed. One skeleton was replaced with the gap-documenting test, netting +0 instead of the planned +3.

**Rationale for closure despite amber items:** Both gaps are well-documented (ADR 0056, session retrospectives), have clear M38.1 targets, and do not affect the correctness of any shipped functionality. The Walmart adapter correctly returns `false` for deactivation rather than silently failing. Closing M38.0 with honest amber reporting is preferable to holding the milestone open for a design decision that belongs in M38.1 planning.

**Session 3 CI confirmation:**
- CI Run #878 — ✅ green on branch `copilot/add-listings-admin-action-buttons-e2e-unblock`
- E2E Run #461 — ✅ green on branch `copilot/add-listings-admin-action-buttons-e2e-unblock`
- Post-merge main: CI Run #879 ✅, E2E Run #462 ✅
- Local build at session 4 open: 0 errors, 16 warnings (all pre-existing)

---

## 2. What Was Delivered — By Session

### Session 1: Walmart Polling + Bidirectional Feedback

Session 1 closed the critical gap that had left Walmart listings stranded in `Submitted` status. `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` now polls `GET /v3/feeds/{feedId}` with four feed status mappings (PROCESSED → live, ERROR → failed, RECEIVED/INPROGRESS → pending). `ListingApprovedHandler` branches on channel code: Walmart schedules a `CheckWalmartFeedStatus` message via `bus.ScheduleAsync()` with escalating delays (2 → 5 → 10 → 20 → 30 min, max 10 attempts); Amazon and eBay paths remain synchronous. The bidirectional side was completed in the same session: Listings BC now subscribes to `marketplaces-listing-activated` and `marketplaces-submission-rejected` exchanges, with `MarketplaceListingActivatedHandler` (`Submitted → Live`) and `MarketplaceSubmissionRejectedHandler` (`Submitted → Ended`) both guarded by idempotency checks. ADR 0055 documented the polling architecture before any code was written. Tests: 105 → 120 (+15). Warnings: 12.

### Session 2: Polly Resilience + DeactivateListingAsync

Session 2 completed the adapter contract on the resilience and deactivation axes. `Microsoft.Extensions.Http.Resilience` was registered on all three named `HttpClient` pipelines with shared retry (3 attempts, exponential backoff) and circuit breaker (50% failure ratio, 5 minimum throughput, 30s sampling/break) options. The 401 exclusion from retry was verified as a Polly v8 default — no custom predicate needed, eliminating unnecessary complexity. `DeactivateListingAsync` was implemented for Amazon (PATCH delete purchasable_offer) and eBay (POST offer withdraw); Walmart was documented as Option A (log gap, return false) per the interface design limitation. ADR 0056 captured both the resilience configuration and the Walmart SKU gap. Tests: 120 → 133 (+13). Warnings: 16.

### Session 3: Admin UI + E2E Unblock

Session 3 delivered the visible operator-facing surface. The three disabled action buttons on `ListingDetail.razor` were wired to their respective Listings.Api endpoints (approve, pause, end) with conditional `Disabled` guards reflecting the state machine. A `PauseListingDialog.razor` was created using `IMudDialogInstance` (the correct MudBlazor v9 cascading parameter type). An `_isActioning` flag prevents double-clicks across all three actions. The three `@wip` E2E scenarios in `ListingsDetail.feature` were unblocked by implementing step definitions, extending `StubListingsApiHost` with mutable POST action endpoints, and adding `ReadyForReviewListing` to `WellKnownTestData`. The existing "navigates to listing detail" scenario was corrected — it had stale assertions claiming Pause and End should be disabled for a Live listing. Tests: 133 (unchanged — no integration test work). E2E: 6 → 9 active scenarios. Warnings: 16 (15 per Session 3 retro count; 16 per Session 4 build — NU1504 counting may vary by restore context).

### Session 4: Milestone Closure

Confirmed CI green on main post-merge (CI #879, E2E #462). Verified Definition of Done with honest amber reporting. Authored this retrospective. Updated CONTEXTS.md. Authored M38.1 planning notes. Updated CURRENT-CYCLE.md. No implementation code written.

**Test count progression:**

| Session | Marketplaces | Listings | Combined | E2E Active |
|---------|-------------|----------|----------|------------|
| Start | 70 | 35 | 105 | 6 |
| S1 close | 79 | 41 | 120 | 6 |
| S2 close | 92 | 41 | 133 | 6 |
| S3 close | 92 | 41 | 133 | 9 |
| Final | 92 | 41 | **133** | **9** |

---

## 3. Architectural Decisions Made

| ADR | Title | Decision |
|-----|-------|----------|
| [0055](../../decisions/0055-submission-status-polling-architecture.md) | Submission Status Polling Architecture | Walmart-only per-submission polling via `bus.ScheduleAsync()`. Marketplaces BC owns polling. Amazon and eBay confirmed synchronous — no polling infrastructure needed. Escalating delay schedule with 10-attempt timeout. |
| [0056](../../decisions/0056-marketplace-adapter-resilience-patterns.md) | Marketplace Adapter Resilience Patterns | Polly v8 ratio-based circuit breaker via `Microsoft.Extensions.Http.Resilience`. `HttpRetryStrategyOptions` default covers 429/5xx and excludes 401. Per-adapter pipeline isolation (Amazon outage does not trip Walmart breaker). 30s explicit timeout on named clients. Walmart deactivation SKU gap documented as interface design limitation. |

---

## 4. Key Lessons Learned

**1. Polly v7 → v8 API drift is a concrete risk when referencing older documentation or problem statements.**

The M38.0 plan referenced `HandledEventsAllowedBeforeBreaking`, which is the Polly v7 API. The repo uses Polly.Core 8.6.5 (via `Microsoft.Extensions.Http.Resilience` 10.4.0). The v8 circuit breaker is ratio-based (`FailureRatio` + `MinimumThroughput` + `SamplingDuration`), not count-based. Session 2 caught this during implementation, not during code review — meaning the plan document itself contained an incorrect API reference. Future milestones that touch Polly or any library with a major-version migration should verify the installed package version in `Directory.Packages.props` before writing configuration code or referencing API surfaces in plans.

**2. Verify library defaults before adding custom predicates — the simplest code is no code.**

The M38.0 guard rail "401 must not be retried by Polly" was satisfied by the `HttpRetryStrategyOptions` defaults, which cover 408, 429, and 500+ but not 401. This eliminated a custom `ShouldHandle` lambda that would have added complexity without value. The lesson generalizes: before writing custom configuration for a library's behavior, read the defaults. When the defaults already satisfy the requirement, the correct implementation is to not write the code at all.

**3. Resilience integration tests require the real DI pipeline — `FakeHttpClientFactory` bypasses middleware.**

`FakeHttpClientFactory` returns a plain `HttpClient` wrapping `FakeHttpMessageHandler`, which means the Polly retry and circuit breaker middleware is never invoked. Tests that must verify retry behavior need to build a `ServiceCollection` with `AddHttpClient(...).ConfigurePrimaryHttpMessageHandler(() => fakeHandler).AddResilienceHandler(...)` and resolve `IHttpClientFactory` from the built container. This pattern is now established in `AdapterResilienceTests.cs` and should be the canonical reference for any future resilience testing in the codebase.

**4. Interface signatures constrain adapter implementations in non-obvious ways — design the identifier format before building the interface.**

`IMarketplaceAdapter.DeactivateListingAsync` accepts only `externalListingId`. For Amazon (`amzn-{sku}`) and eBay (`ebay-{offerId}`), the encoded identifier is sufficient for deactivation. For Walmart, the `wmrt-{feedId}` format encodes the feed ID — an internal processing artifact — not the SKU required by the RETIRE_ITEM feed. This is not a bug in the adapter; it is a design limitation in what information the `externalListingId` carries. The fix is an M38.1 design decision: encode `wmrt-{sku}` instead (the polling handler already has the feed ID separately). The lesson: when designing an identifier that must support multiple operations across its lifetime (submission, polling, deactivation), ensure the encoded value is the one needed by the most operations, not the one returned by the first operation.

**5. MudBlazor v9's `IMudDialogInstance` cascading parameter is the correct type — `MudDialogInstance` compiles but produces a runtime null.**

Session 3 used `IMudDialogInstance` (the interface, not the concrete class) for the `PauseListingDialog.razor` cascading parameter. This was verified against the established reference (`PreflightDiscontinuationModal.razor`). Using the wrong type is a subtle error: the code compiles, the dialog renders, but the cascading parameter is never populated because MudBlazor v9 injects the interface type. Always verify dialog parameter types against an existing working dialog component before writing new ones.

---

## 5. Known Technical Debt and Deferred Items

| Item | Documented In | Target |
|------|--------------|--------|
| Walmart `DeactivateListingAsync` — SKU gap; returns false; 3 TODO tests | ADR 0056, Session 2 retro | M38.1 |
| Walmart deactivation interface design — encode SKU in externalListingId vs. pass separately | Session 2 retro Section "Walmart Deactivation SKU Gap is an Interface Design Issue" | M38.1 |
| Amazon `CheckSubmissionStatusAsync` real implementation (GET to verify BUYABLE status) | ADR 0052 skeleton comment, Session 2 retro | M38.1 |
| eBay `CheckSubmissionStatusAsync` real implementation (GET offer status) | ADR 0054 skeleton comment, Session 2 retro | M38.1 |
| Orphaned eBay draft offer cleanup (publish fails after create succeeds) | Q5 decision, ADR 0054, M38.0 plan Section 3 | M38.1 |
| `SemaphoreSlim` token caching refactor to shared base class | M37.0 lesson 3, M38.0 plan out-of-scope table | M38.1 (when fourth adapter added) |
| Walmart deactivation full tests (3 TODO tests in `WalmartMarketplaceAdapterTests.cs`) | Session 2 retro | M38.1 |

---

## 6. What M38.1 Inherits

**Codebase state at M38.0 close:**

- **Solution:** 19 bounded contexts, `CritterSupply.slnx`
- **Integration tests:** 133 total (92 Marketplaces + 41 Listings); 0 failures
- **E2E scenarios:** 9 active on `@shard-3` (MarketplacesAdmin × 6, ListingsDetail × 3)
- **Production adapters:** Amazon, Walmart, eBay — behind `UseRealAdapters` flag; Polly resilience active on all three pipelines
- **Walmart polling:** live for `WALMART_US` channel; Amazon and eBay synchronous
- **Bidirectional feedback:** fully wired — Listings BC receives and processes marketplace outcomes (`MarketplaceListingActivated` → Live, `MarketplaceSubmissionRejected` → Ended)
- **Admin action buttons:** Approve/Pause/End live on `ListingDetail.razor` with state machine guards
- **Build:** 0 errors, 16 warnings (all pre-existing)
- **Next ADR:** 0057
- **M38.1 primary focus:** Walmart deactivation interface design + Walmart/Amazon/eBay `CheckSubmissionStatusAsync` real implementations + eBay orphaned draft cleanup

---

## Definition of Done Verification

| # | Criterion | Status | Notes |
|---|-----------|--------|-------|
| 1 | Walmart polling implemented | ✅ Green | Four feed statuses mapped; tests cover all four |
| 2 | Walmart async submission loop closed | ✅ Green | `ListingApprovedHandler` schedules poll for WALMART_US; Amazon/eBay synchronous |
| 3 | Listings BC bidirectional feedback wired | ✅ Green | `MarketplaceListingActivatedHandler` + `MarketplaceSubmissionRejectedHandler` with idempotency guards |
| 4 | Polly retry policies registered | ✅ Green | All three named clients; 429/5xx retried, 401 excluded by default; 30s timeout |
| 5 | `DeactivateListingAsync` fully implemented | ⚠️ Amber | Amazon + eBay complete; Walmart returns false due to SKU gap (ADR 0056) |
| 6 | Listings admin action buttons enabled | ✅ Green | Approve/Pause/End wired with state machine guards |
| 7 | `@wip` E2E scenarios active | ✅ Green | All three pass on `@shard-3` (CI Run #878, E2E Run #461) |
| 8 | Integration test count ≥ 135; 0 errors | ⚠️ Amber | 133 tests (2 short); 0 errors; gap is the 3 Walmart TODO tests |

**6 green, 2 amber. M38.0 is closed with documented gaps carrying forward to M38.1.**
