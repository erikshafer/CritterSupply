# Marketplace listing submission round-trip

> **Status:** Active
> **Type:** Hybrid (RabbitMQ choreography + outbound HTTP via adapter set)
> **Initiating actor:** Operator (approving a listing) or Vendor (via Vendor Portal listing approval flow)
> **BCs involved:** Listings, Marketplaces (with outbound HTTP to Amazon SP-API, eBay, Walmart)
> **Most recent material milestone:** M38.1 — Orphaned eBay draft sweep + ADR 0055

## Purpose

When an operator or vendor approves a listing for a sales channel, Marketplaces submits the listing to the external channel via the configured adapter, and the outcome (success or rejection) flows back to Listings as an integration event that drives the listing's state machine to `Live` or terminal `Ended`. The workflow is one of two outbound external-system integration points in CritterSupply (the other being Payments' card-network capture).

## Actors and triggers

- **Initiating actor:** Operator (Backoffice listings admin) or vendor (Vendor Portal listing approval)
- **Trigger:** `Listings.ListingApproved` integration event (published by `ApproveListingHandler` on Listings' `Submitted → Submitted` transition; the source `ListingStatus` rendering of the approval is per-channel)
- **Prerequisite state:** A `Listing` aggregate exists in Listings in `Submitted` state; the target `Marketplace` document is registered in Marketplaces and active; an `IMarketplaceAdapter` for the channel code is registered in DI (or the `OWN_WEBSITE` channel code, which skips submission entirely); a `CategoryMapping` exists for `{ChannelCode, productSummary.Category}`; the local `ProductSummaryView` ACL for the SKU has been populated by prior `ProductCatalog.ProductAdded` / `ProductContentUpdated` events

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Listings | `ApproveListing` command → `ListingApproved` stream event + `Messages.Contracts.Listings.ListingApproved(ListingId, Sku, ChannelCode, ProductName, Category?, Price?, OccurredAt)` published | Listing status transitions to `Submitted`; enriched payload (per ADR 0050) | `bcs/listings.md#outbound-published` |
| 2 | Marketplaces | `ListingApprovedHandler` consumes from queue `marketplaces-listings-events`. Skips channel `OWN_WEBSITE`. Loads local `ProductSummaryView` (ACL) and `CategoryMapping` for `{ChannelCode, Category}`; checks marketplace active flag; resolves adapter from channel-code dictionary | If any guard fails: `MarketplaceSubmissionRejected` published (5 guard branches) | `bcs/marketplaces.md#inbound-subscribed`, `bcs/marketplaces.md#di-registration-and-channel-code-dispatch` |
| 3a | Marketplaces (Amazon adapter) | `AmazonMarketplaceAdapter.SubmitListingAsync` — authenticates via Login with Amazon (LWA) OAuth 2.0; calls SP-API `PUT /listings/2021-08-01/items/{sellerId}/{sku}` against `https://sellingpartnerapi-na.amazon.com` (token cached under `SemaphoreSlim`) | Synchronous: `SubmissionResult` returned to handler | `bcs/marketplaces.md#implementations` (Amazon) |
| 3b | Marketplaces (eBay adapter) | `EbayMarketplaceAdapter.SubmitListingAsync` — OAuth refresh-token grant; two-step submission (`POST /sell/inventory/v1/offer` then `POST /sell/inventory/v1/offer/{offerId}/publish`) | Synchronous: `SubmissionResult` with `OrphanedExternalSubmissionId?` populated when create succeeded but publish failed | `bcs/marketplaces.md#implementations` (eBay) |
| 3c | Marketplaces (Walmart adapter) | `WalmartMarketplaceAdapter.SubmitListingAsync` — OAuth client-credentials; submits `MP_ITEM` feed to `https://marketplace.walmartapis.com/v3/feeds?feedType=MP_ITEM` | Synchronous: feed id returned in `SubmissionResult`; outcome **not yet known** (async feed processing) | `bcs/marketplaces.md#implementations` (Walmart) |
| 4 | Marketplaces | On adapter success (Amazon/eBay synchronous): `MarketplaceListingActivated(ListingId, Sku, ChannelCode, ExternalListingId)` published to exchange `marketplaces-listing-activated` | Synchronous outbound integration message | `bcs/marketplaces.md#outbound-published` |
| 4a | Marketplaces (eBay orphan path) | If `OrphanedExternalSubmissionId` populated: `OrphanedEbayDraft` document persisted via Marten; `SweepOrphanedEbayDraftsHandler` reschedules every 24h to call `DeleteOrphanedDraftAsync` | Draft eventually deleted from eBay; document removed | `bcs/marketplaces.md#document-types`, `bcs/marketplaces.md#outbox-and-sequence-numbering` |
| 4b | Marketplaces (Walmart poll path) | `CheckWalmartFeedStatusHandler` reschedules itself with escalating delays (10-attempt cap); on `PROCESSED`: publishes `MarketplaceListingActivated` with `externalListingId = "wmrt-{sku}"` per ADR 0057; on `ERROR` or attempt-cap exhaustion: publishes `MarketplaceSubmissionRejected` | Eventual asynchronous outcome | `bcs/marketplaces.md#outbox-and-sequence-numbering` |
| 5 | Listings | `MarketplaceListingActivatedHandler` consumes from `listings-marketplace-outcome-events`; transitions `Submitted → Live`; appends `ListingActivated` stream event; publishes `Messages.Contracts.Listings.ListingActivated` | Idempotent — no-op if listing missing, already `Live`, or in any state other than `Submitted` | `bcs/listings.md#inbound-subscribed` (item 3) |
| 5a | Listings (rejection path) | `MarketplaceSubmissionRejectedHandler` consumes from `listings-marketplace-outcome-events`; transitions `Submitted → Ended` with cause `SubmissionRejected`; publishes `ListingEnded(Cause=SubmissionRejected)` | Terminal listing state | `bcs/listings.md#inbound-subscribed` (item 3) |

## Projections and views

- `Marketplace` Marten document (Marketplaces) — registry of registered channels and their active state. Dossier: `bcs/marketplaces.md#document-types`.
- `CategoryMapping` Marten document (Marketplaces) — per-channel internal-category to marketplace-category translation. Dossier: `bcs/marketplaces.md#document-types`.
- `ProductSummaryView` Marten document (Marketplaces; ACL over Product Catalog) — used by `ListingApprovedHandler` to build the submission payload. Per ADR 0050. Dossier: `bcs/marketplaces.md#document-types`.
- `OrphanedEbayDraft` Marten document (Marketplaces) — per-orphan record of incomplete eBay submissions; swept by `SweepOrphanedEbayDraftsHandler` (M38.1). Dossier: `bcs/marketplaces.md#document-types`.
- `Listing` snapshot (Listings) — terminal Live or Ended state after the round-trip. Dossier: `bcs/listings.md#aggregates`.

## Compensation paths

### Failure: ACL `ProductSummaryView` missing, category missing, category mapping missing, marketplace inactive, or no adapter registered
- **Compensating action:** `ListingApprovedHandler` publishes `MarketplaceSubmissionRejected` with the failure reason; Listings transitions the listing to `Ended` with cause `SubmissionRejected`.
- **Resulting state:** Listing in terminal `Ended` state; no external submission attempted.
- **BCs involved in compensation:** Marketplaces, Listings.

### Failure: Adapter call fails synchronously (Amazon / eBay non-orphan)
- **Compensating action:** Polly resilience pipeline retries per ADR 0056 (3 attempts, exponential backoff on HTTP 408/429/5xx); circuit breaker may also open (`FailureRatio = 0.5`, `MinimumThroughput = 5`, `SamplingDuration = 30 s`, `BreakDuration = 30 s`). On terminal failure the adapter returns `SubmissionResult { IsSuccess = false, ErrorMessage }`; handler publishes `MarketplaceSubmissionRejected`.
- **Resulting state:** Listing terminal `Ended`.
- **BCs involved in compensation:** Marketplaces, Listings.

### Failure: eBay publish step fails after offer create succeeded (orphan)
- **Compensating action:** Per ADR 0055, the adapter returns `OrphanedExternalSubmissionId` on the `SubmissionResult`; the handler persists an `OrphanedEbayDraft` document; `SweepOrphanedEbayDraftsHandler` runs every 24 h to call `DeleteOrphanedDraftAsync` on the adapter, processing orphans oldest-first.
- **Resulting state:** Eventually consistent; orphan draft removed from eBay's side; local document removed.
- **BCs involved in compensation:** Marketplaces only.

### Failure: Walmart feed processing exhausts 10-attempt cap or returns `ERROR`
- **Compensating action:** `CheckWalmartFeedStatusHandler` publishes `MarketplaceSubmissionRejected`; Listings transitions to `Ended`.
- **Resulting state:** Listing terminal `Ended`.
- **BCs involved in compensation:** Marketplaces, Listings.

## Variants and edge cases

### `OWN_WEBSITE` channel
The `ListingApprovedHandler` short-circuits on channel code `OWN_WEBSITE` — no submission to any external adapter. Listings' integration tests cover the `OWN_WEBSITE` Draft → Live fast-path. Dossier: `bcs/marketplaces.md#inbound-subscribed`.

### Amazon variant (synchronous)
LWA OAuth + SP-API `PUT /listings/2021-08-01/items`. Single round-trip. ADR 0052. The outbound `externalListingId` is the Amazon-returned SKU URI.

### eBay variant (synchronous + orphan compensation)
OAuth refresh-token + two-step (`offer` then `offer/{offerId}/publish`). Partial success populates `OrphanedExternalSubmissionId`. M38.1 added the orphan sweep. ADR 0054.

### Walmart variant (asynchronous via feed processing)
OAuth client-credentials + `MP_ITEM` feed POST. Status-poll loop via `CheckWalmartFeedStatusHandler`. Outbound `externalListingId = "wmrt-{sku}"` per ADR 0057 — encoded as the SKU, not the feed id. ADR 0053.

### Stub variant (Development / CI)
When `Marketplaces:UseRealAdapters == false`, `StubAmazonAdapter` / `StubWalmartAdapter` / `StubEbayAdapter` return immediate success with synthetic correlation IDs (`amzn-{guid}`, `wmrt-{sku}`, `ebay-{guid}`) after a 100 ms `Task.Delay`. `DeleteOrphanedDraftAsync` returns `true` as a no-op. Dossier: `bcs/marketplaces.md#implementations`.

## BCs and roles

- **Listings** — Publishes `ListingApproved` (initiation); consumes outcome events from `listings-marketplace-outcome-events`; runs the per-listing state machine (`Submitted → Live` or `Submitted → Ended`). Dossier: `bcs/listings.md`.
- **Marketplaces** — Owns the adapter set; performs synchronous adapter calls; runs the Walmart status-poll and eBay orphan-sweep background loops; publishes activation / rejection outcomes. Dossier: `bcs/marketplaces.md`.
- **External channels (Amazon SP-API, eBay, Walmart)** — Outbound HTTP destinations; their async status determines the outcome event published back. Credentials sourced from `IVaultClient` (`DevVaultClient` in Development, `EnvironmentVaultClient` otherwise per ADR 0051).

## Tests as behavioral evidence

- **Gherkin features:** none under `docs/features/marketplaces/`; Backoffice-level Gherkin (`tests/Backoffice/Backoffice.E2ETests/Features/MarketplacesAdmin.feature` + `MarketplacesAdminSteps.cs`) exercises the admin UI workflows.
- **Integration tests (Alba + Testcontainers):**
  - `tests/Listings/Listings.Api.IntegrationTests/MarketplaceListingActivatedHandlerTests.cs` — 3 tests (Submitted → Live; idempotent on already-Live; no-op on missing/unsubmitted).
  - `tests/Listings/Listings.Api.IntegrationTests/MarketplaceSubmissionRejectedHandlerTests.cs` — 3 tests (Submitted → Ended; idempotent on already-Ended; no-op on missing).
  - Marketplaces-side integration tests under `tests/Marketplaces/` cover the adapter dispatch, ACL handlers, Walmart poll loop, and eBay orphan sweep.

## ADRs

- **ADR 0048** — Marketplace Document Entity Design. Marketplaces uses Marten document store (not event-sourced). File: `docs/decisions/0048-marketplace-document-entity-design.md`.
- **ADR 0049** — Category Mapping Ownership (Marketplaces, not Listings). File: `docs/decisions/0049-category-mapping-ownership.md`.
- **ADR 0050** — Marketplaces ProductSummaryView ACL (Phase-2 enrichment of `ListingApproved`). File: `docs/decisions/0050-marketplaces-product-summary-acl.md`.
- **ADR 0051** — Vault abstraction (`DevVaultClient` / `EnvironmentVaultClient`). File: `docs/decisions/0051-*.md`.
- **ADR 0052** — Amazon SP-API adapter design. File: `docs/decisions/0052-*.md`.
- **ADR 0053** — Walmart Marketplace API adapter design. File: `docs/decisions/0053-*.md`.
- **ADR 0054** — eBay adapter design. File: `docs/decisions/0054-*.md`.
- **ADR 0055** — Orphaned eBay draft sweep (M38.1). File: `docs/decisions/0055-*.md`.
- **ADR 0056** — Marketplaces resilience pipeline (Polly retry + circuit breaker). File: `docs/decisions/0056-*.md`.
- **ADR 0057** — Walmart `externalListingId` encoded as `wmrt-{sku}` (not feed id). File: `docs/decisions/0057-*.md`.

## Declared vs. implemented

- **Declared shape (CONTEXTS.md Marketplaces communicates-with table):** Two arrows — Listings ← receives and Product Catalog ← receives.
- **Implemented shape:** Marketplaces also emits outbound to Listings (`MarketplaceListingActivated`, `MarketplaceSubmissionRejected`) — confirmed in `bcs/listings.md#inbound-subscribed` (item 3 — `listings-marketplace-outcome-events` queue).
- **Gap:** Presentation drift in CONTEXTS.md — the Listings row in CONTEXTS.md describes the relationship as "← receives" with the inline note covering both directions, but the Marketplaces row omits the outbound arrow. Dossier source: `bcs/marketplaces.md#contextsmd-drift`.

- **Declared shape (CONTEXTS.md):** `OrphanedEbayDraft` document collection is unmentioned in CONTEXTS.md.
- **Implemented shape:** `OrphanedEbayDraft` ships with M38.1 (ADR 0055) and is one of four registered Marketplaces document types. `ProductSummaryView` is misclassified as a projection in CONTEXTS.md; in code it is a Marten document mutated by handlers.
- **Gap:** Documentation undercount; functional gap is absent.

## Source citations

- Dossier sections referenced: `bcs/listings.md#outbound-published`, `bcs/listings.md#inbound-subscribed`, `bcs/listings.md#aggregates`, `bcs/marketplaces.md#inbound-subscribed`, `bcs/marketplaces.md#outbound-published`, `bcs/marketplaces.md#implementations`, `bcs/marketplaces.md#document-types`, `bcs/marketplaces.md#outbox-and-sequence-numbering`, `bcs/marketplaces.md#di-registration-and-channel-code-dispatch`, `bcs/marketplaces.md#contextsmd-drift`.
- ADRs: 0048–0057 (per dossier index).
- Tests: cited per row above.
