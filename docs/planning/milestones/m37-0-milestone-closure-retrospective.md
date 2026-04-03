# M37.0 Milestone Closure Retrospective

**Status:** ✅ Complete
**Date:** 2026-04-03
**Sessions:** 4 (Sessions 1–3 implementation; Session 4 closure)
**Duration:** 2026-04-01 → 2026-04-03

---

## 1. Goal Statement and Outcome

**Goal:** Deliver Phase 3 of the Catalog–Listings–Marketplaces cycle — replacing the three stub marketplace adapters with real, production-capable implementations for Amazon SP-API, Walmart Marketplace API, and eBay Sell API — while resolving the highest-priority debt inherited from M36.1 (message enrichment coupling, missing E2E shard tags, and ProductSummaryView ACL gaps).

**Outcome: M37.0 is complete.** All planned deliverables across four sessions were delivered:

- **Session 1** — Debt clearance: `@shard-3` E2E tags, Marketplaces `ProductSummaryView` ACL, ADR 0050 ✅
- **Session 2** — Production vault client + Amazon SP-API adapter + ADRs 0051–0052 ✅
- **Session 3** — Walmart + eBay adapters + ADRs 0053–0054 ✅
- **Session 4** — Milestone closure ✅

Final build state: 0 errors, 12 warnings (all pre-existing). 105 integration tests passing (35 Listings + 70 Marketplaces). 6 E2E scenarios present and shard-tagged for CI.

**Session 3 CI confirmation:**
- CI Run #872 — ✅ green on branch `copilot/m37-0-session-3-walmart-ebay-adapters`
- E2E Run #455 — ✅ green on branch `copilot/m37-0-session-3-walmart-ebay-adapters`

---

## 2. What Was Delivered — By Session

### Session 1 (2026-04-01): Debt Clearance + ProductSummaryView ACL

Session 1 focused entirely on clearing the three highest-priority debt items inherited from M36.1 before any new production code was written. The `@shard-3` tags added to `MarketplacesAdmin.feature`, `ListingsAdmin.feature`, and `ListingsDetail.feature` unblocked CI execution for all three files (previously silent-skipped by all shard runners). The Marketplaces `ProductSummaryView` ACL — subscribing to four Product Catalog integration events — replaced the fragile message-enrichment coupling in `ListingApprovedHandler`, which had been reading `ProductName`, `Category`, and `Price` directly from the `ListingApproved` message payload. ADR 0050 documents the ACL decision and its known gap (BasePrice not populated; Pricing BC integration deferred). Six new integration tests added; combined baseline: 68 tests.

### Session 2 (2026-04-03): Production IVaultClient + Amazon SP-API Adapter

Session 2 opened with a structured decision session that resolved four open architectural questions (adapter delivery scope, vault backend, polling scope, BasePrice gap) before writing a line of implementation code. This Phase A decision protocol prevented mid-implementation pivots. `EnvironmentVaultClient` — reading secrets from `VAULT__`-prefixed environment variables — was delivered as the production `IVaultClient` with 13 tests covering all path convention cases. `AmazonMarketplaceAdapter` delivered LWA OAuth 2.0 token exchange, in-memory token caching with `SemaphoreSlim`, and full `SubmitListingAsync` implementation against the SP-API Listings Items API. ADRs 0051 and 0052 documented the vault strategy and Amazon auth patterns respectively. Twenty new tests; combined baseline: 88 tests.

### Session 3 (2026-04-03): Walmart + eBay Production Adapters

Session 3 extracted the Session 2 test doubles (`FakeHttpMessageHandler`, `FakeVaultClient`, `FakeHttpClientFactory`) into a shared `MarketplaceAdapterTestHelpers` class before writing any new adapter code — the eBay two-step create+publish flow revealed that `FakeHttpMessageHandler` needed URL-keyed response support that a simple FIFO queue could not provide. `WalmartMarketplaceAdapter` implemented the client credentials OAuth 2.0 grant (the simplest of the three auth flows) with feed-based listing submission (`POST /v3/feeds?feedType=MP_ITEM`). `EbayMarketplaceAdapter` implemented the refresh token grant with explicit UTF-8 Base64 encoding per RFC 7617, plus the two-step offer create+publish flow. `Program.cs` updated so `UseRealAdapters: true` now activates all three real adapters. ADRs 0053 and 0054 document the Walmart and eBay auth patterns. Seventeen new tests; combined baseline: 105 tests.

### Session 4 (2026-04-03): Milestone Closure

Confirmed Session 3 CI green (CI Run #872, E2E Run #455). Authored this retrospective. Added Listings BC and Marketplaces BC entries to `CONTEXTS.md`. Authored M38.x pre-planning notes. Updated `CURRENT-CYCLE.md` to move M37.0 to Recent Completions. No implementation code written.

---

## 3. Architectural Decisions Made

| ADR | Title | Decision |
|-----|-------|----------|
| [0050](../../decisions/0050-marketplaces-product-summary-acl.md) | Marketplaces ProductSummaryView Anti-Corruption Layer | Marketplaces BC maintains its own `ProductSummaryView` by subscribing to 4 Product Catalog integration events; eliminates message enrichment coupling in `ListingApproved`. |
| [0051](../../decisions/0051-vault-implementation-strategy.md) | Vault Implementation Strategy | `EnvironmentVaultClient` reads secrets from `VAULT__`-prefixed environment variables; avoids cloud-specific SDK dependencies in a reference architecture. |
| [0052](../../decisions/0052-amazon-spapi-authentication.md) | Amazon SP-API Authentication Patterns | LWA OAuth 2.0 refresh token grant with in-memory token caching; AWS Signature V4 not required for Listings Items API; rate limiting deferred to M38.x. |
| [0053](../../decisions/0053-walmart-marketplace-api-authentication.md) | Walmart Marketplace API Authentication | Client credentials grant (simplest flow); token caching with 5-minute safety margin; feed-based submission returns feed ID as `ExternalSubmissionId`. |
| [0054](../../decisions/0054-ebay-sell-api-authentication.md) | eBay Sell API Authentication | Refresh token grant with UTF-8 Base64 encoding per RFC 7617; two-step offer create+publish submission; orphaned draft cleanup deferred to M38.x. |

---

## 4. Key Lessons Learned

**1. Front-load architectural decisions in a dedicated Phase A before implementation begins.**

Session 2 opened with four open questions that had been identified in M36.1 planning but never formally resolved. Rather than making those decisions implicitly while writing code, Session 2 explicitly sequenced a Phase A decision log before touching the implementation. This prevented two potential mid-session pivots: discovering mid-Amazon-adapter that Walmart was also in scope (D-1), and discovering mid-vault-implementation that a cloud SDK was the wrong approach (D-2). For any session that inherits unresolved architectural questions, a brief, written decision log at the start — with explicit rationale and confidence rating — is worth the time investment. It also produces a durable record in the session retrospective.

**2. Test double design should anticipate multi-step API flows from the start.**

`FakeHttpMessageHandler` was initially designed as a FIFO response queue, which worked for the single-request Amazon and Walmart flows. The eBay two-step create-offer/publish-offer flow exposed the limitation: you cannot deterministically route different responses to different URLs with a pure FIFO queue. The fix — URL-keyed response support via `EnqueueResponseForUrl` — had to be retrofitted before eBay tests could be written. Marketplace adapters commonly involve multi-step API interactions (auth + operation, create + publish, submit + confirm). When designing test doubles for this pattern, build URL-keyed response support into `FakeHttpMessageHandler` from day one rather than discovering the gap at the second or third adapter.

**3. The `SemaphoreSlim` token caching pattern is mature enough to extract into a shared base class.**

All three production adapters independently implement the same token caching pattern: `SemaphoreSlim(1,1)`, cached access token string, expiry timestamp with 5-minute safety margin, double-checked lock on token fetch. This is a copy-paste pattern today. It works — but three copies create three maintenance surfaces. If a fourth adapter is added in a future milestone, a `CachedTokenProvider` base class or helper method should consolidate this logic before the copy count grows further.

**4. The `UseRealAdapters` configuration flag correctly places real-adapter integration tests at the developer boundary, not CI.**

CI always runs against stubs (the flag defaults to `false`). This is the right default for a reference architecture: no CI environment has Amazon/Walmart/eBay API credentials. The consequence is that real-adapter code paths are not validated in automated CI — only the auth and request-building logic is tested in isolation via `FakeHttpMessageHandler`. Any operator deploying these adapters in a real environment must run the real-adapter tests manually against sandbox credentials. This constraint should be documented explicitly in the M38.x development guide so it is not re-discovered by the next engineer working on this code.

**5. Milestone scope that spans multiple OAuth flows benefits from per-flow ADRs rather than a single cross-adapter ADR.**

M36.1 deferred the production adapter work without authoring auth ADRs. M37.0 delivered one ADR per auth flow (0051–0054). This per-flow approach turned out to be correct: Amazon LWA, Walmart client credentials, and eBay refresh token are meaningfully different, and the implementation decisions (vault path conventions, token TTL, header requirements, encoding rules) differ enough that a combined ADR would have been unwieldy. When a milestone delivers multiple external API integrations with different auth protocols, plan for one ADR per protocol from the start of planning.

---

## 5. Known Technical Debt and Deferred Items

| Item | Documented In | Target |
|------|--------------|--------|
| Async submission status polling (`CheckSubmissionStatusAsync` saga using Wolverine `bus.ScheduleAsync()`) | Session 2 D-3 decision, ADR 0052 | M38.x |
| Rate limiting retry logic (Polly delegating handler on adapter `HttpClient` pipelines) | ADRs 0052/0053/0054 (log-and-fail documented) | M38.x |
| `DeactivateListingAsync` full implementation (all three adapters return skeleton `false`) | Session 2/3 retros (skeleton noted) | M38.x |
| Bidirectional marketplace feedback (Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`) | M37.0 planning notes | M38.x |
| Orphaned eBay draft offer cleanup (publish step fails after create succeeds) | Session 3 retro | M38.x |
| `BasePrice` gap — Pricing BC integration events not yet consumed by `ProductSummaryView` in either Listings or Marketplaces BC | ADR 0050 Decision 5 | Future (Pricing BC evolution) |
| Listings admin action buttons (approve/pause/end) — disabled stubs on detail page | M36.1 Session 4 retro | M38.x or later |
| `@wip` E2E scenarios in ListingsDetail.feature (3 scenarios for action button flows) | M36.1 Session 4 retro | M38.x or later |

---

## 6. What M38.x Inherits

**Codebase state at M37.0 close:**

- **Solution:** 19 bounded contexts, `CritterSupply.slnx`
- **Integration tests:** 105 total — 35 Listings + 70 Marketplaces; 0 failures
- **E2E scenarios:** 6 (MarketplacesAdmin.feature, shard-tagged `@shard-3` for CI) + Listings features (ListingsAdmin.feature, ListingsDetail.feature; also `@shard-3` tagged)
- **Production adapters:** Amazon, Walmart, eBay — all implemented, all gated behind `Marketplaces:UseRealAdapters` flag
- **Stub adapters:** `StubAmazonAdapter`, `StubWalmartAdapter`, `StubEbayAdapter` — all preserved for Development/CI use
- **Vault implementation:** `EnvironmentVaultClient` (production), `DevVaultClient` (development)
- **Build:** 0 errors, 12 warnings (all pre-existing: NU1504 duplicate package refs, CS0219 in Correspondence, CS8602 in Backoffice.Web)
- **Next ADR:** 0055
- **M38.x primary focus:** Async submission lifecycle (polling infrastructure), resilience patterns (rate limiting/retry/circuit breaker), and bidirectional marketplace feedback (Listings BC consuming marketplace outcome events)
