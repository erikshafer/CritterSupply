# M37.0 Planning Notes

**Status:** 📋 Pre-planning notes (not a formal plan)
**Date:** 2026-03-31
**Purpose:** Capture what is known about M37.0 scope before a formal planning session begins. This prevents the M37.0 planning session from starting cold.

---

## What Phase 3 Means

The [Catalog-Listings-Marketplaces Cycle Plan](../catalog-listings-marketplaces-cycle-plan.md) defines Phase 3 as the delivery of real marketplace adapter implementations. M36.1 delivered the full adapter interface (`IMarketplaceAdapter`) and three stub implementations (Amazon, Walmart, eBay). Phase 3 replaces those stubs with real API clients.

**Phase 3 scope from execution plan:**
- Real Amazon SP-API adapter (product listing creation, status polling, deactivation)
- Real Walmart Marketplace API adapter (same operations)
- Real eBay Sell API adapter (same operations)
- Production `IVaultClient` implementation (secure credential retrieval)
- Submission status polling or webhook infrastructure for asynchronous marketplace review queues
- Rate limiting, retry policies, and circuit breaker patterns for external API calls

**Phase 3 prerequisites (all met in M36.1):**
- ✅ `IMarketplaceAdapter` interface defined and tested via stubs
- ✅ `ListingApproved` consumer routes to correct adapter by ChannelCode
- ✅ `IVaultClient` interface defined with DevVaultClient for development
- ✅ `SubmissionResult` and `SubmissionStatus` types established
- ✅ Integration tests verify end-to-end submission flow via stubs

---

## Debt Items Inherited from M36.1

These must be resolved in M37.0, ideally in Session 1 before new feature work begins:

| Priority | Item | Source | Notes |
|----------|------|--------|-------|
| **P1** | Add `@shard-3` to MarketplacesAdmin.feature | Session 10 CI verification | 6 scenarios not discovered by CI. One-line fix. |
| **P1** | Add `@shard-3` to ListingsAdmin.feature and ListingsDetail.feature | Session 10 CI verification | Same issue. Features have no shard tag. |
| **P2** | Replace `ListingApproved` message enrichment with `ProductSummaryView` ACL in Marketplaces BC | Session 7 retro, ADR 0049 | Currently `ListingApproved` carries `ProductName`, `Category`, `Price` directly. Marketplaces should query its own `ProductSummaryView` for this data. Resolves coupling to Listings message payload. |
| **P2** | Address category taxonomy coupling | ADR 0049 risk section | Marketplaces BC will silently break if Product Catalog renames categories. Mitigation: `ProductSummaryView` ACL in Marketplaces (same as above). |
| **P3** | Listings admin action buttons (approve/pause/end) | Session 4 retro | Disabled stubs on detail page. Not blocking Phase 3. |
| **P3** | ListingsDetail.feature `@wip` scenarios | Session 4 retro | 3 scenarios tagged @wip for action button flows. |
| **P3** | Bidirectional marketplace feedback | Phase 2 plan deferred scope | Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`. |

---

## Port and Database Allocations

**Already consumed:**
- Listings: port 5246, database `listings`, schema `listings`
- Marketplaces: port 5247, database `marketplaces`, schema `marketplaces`

**Not yet consumed (available for M37.0 if needed):**
- No new BCs expected in M37.0 — Phase 3 work is within the existing Marketplaces BC

---

## Next ADR Number

**0050** — the next available ADR number.

Candidate ADRs for M37.0:
- ADR 0050: Production Vault Integration Strategy (how real credentials are stored and retrieved)
- ADR 0051: Marketplace Adapter Resilience Patterns (retry, circuit breaker, rate limiting)
- ADR 0052: Submission Status Polling vs Webhooks (how async marketplace reviews are handled)

---

## Open Questions for the Planning Session

1. **Adapter priority order:** Should all 3 adapters (Amazon, Walmart, eBay) be delivered in M37.0, or should we start with one and iterate? Amazon SP-API is the most complex; eBay may be simplest.

2. **Vault implementation:** Should M37.0 use a real vault (HashiCorp Vault, AWS Secrets Manager, Azure Key Vault) or a simpler encrypted-file approach for the reference architecture? The `IVaultClient` abstraction supports any backend.

3. **Submission status polling:** The `CheckSubmissionStatusAsync` method exists on `IMarketplaceAdapter` but no polling infrastructure exists. Should M37.0 include a Wolverine scheduled message pattern for polling, or should this wait for M38.x?

4. **ProductSummaryView in Marketplaces:** This debt item from M36.1 requires Marketplaces to consume the same 9 Product Catalog integration events that Listings already consumes. Should Marketplaces have its own `ProductSummaryView` document, or should it subscribe to a subset of events (e.g., only `ProductAdded` and `ProductCategoryChanged`)?

5. **E2E scope:** M37.0 will add real API interactions. Should E2E tests use the stub adapters (testing UI only) or should we build test doubles for real marketplace APIs?

---

## Codebase State at M37.0 Start

- **Solution:** `CritterSupply.slnx` with 19 BCs
- **Build:** 0 errors, 33 warnings (all pre-existing)
- **Integration tests:** 62 (35 Listings + 27 Marketplaces), all passing
- **E2E scenarios:** 6 (MarketplacesAdmin.feature) — not yet executing in CI
- **CI:** CI Run #856 (green), E2E Run #432 (green), CodeQL Run #413 (green)
- **Key files:**
  - `src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs` — adapter interface
  - `src/Marketplaces/Marketplaces/Credentials/IVaultClient.cs` — vault interface
  - `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs` — consumer
  - `tests/Marketplaces/Marketplaces.Api.IntegrationTests/` — test project
