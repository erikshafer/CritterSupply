# Marketplaces

> **Source folder:** `src/Marketplaces/`
> **Status:** Implemented
> **Most recent material milestone:** M40.0 — Real marketplace adapters (Amazon SP-API / Walmart / eBay) with vault-backed credentials and resilience policies
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Marketplaces owns the channel configuration and adapter-orchestration layer that submits Listings BC outputs to external marketplace APIs (Amazon, Walmart, eBay). It holds the channel registry, the internal-to-channel category map, and the per-channel `IMarketplaceAdapter` implementations that authenticate, submit, poll, and deactivate listings on the external systems. A local `ProductSummaryView` ACL isolates the BC from Listings' message payloads.

## Top-level structure

### Aggregates

- `Marketplace` — Marten document-store entity (not event-sourced); `ChannelCode` is the natural document Id.
- `CategoryMapping` — Marten document-store entity (not event-sourced); composite key `{ChannelCode}:{InternalCategory}`.

### Commands

- Marketplace registration / activation / deactivation commands and category-mapping commands live in `src/Marketplaces/Marketplaces/`; at S1 stub depth the enumeration is deferred to the S3 dossier.

### Domain events

Not applicable — Marketplaces is implemented on Marten document store and does not emit domain events at S1 stub depth.

### Projections

- `ProductSummaryView` (ACL) — inline; populated from 4 granular Product Catalog integration events.

### Integration events

- `Marketplaces.MarketplaceRegistered` — publishes
- `Marketplaces.MarketplaceDeactivated` — publishes
- `Marketplaces.MarketplaceListingActivated` — publishes (back to Listings)
- `Marketplaces.MarketplaceSubmissionRejected` — publishes (back to Listings)
- `Listings.ListingApproved` — subscribes (triggers adapter submission)
- 4 `ProductCatalog.*` events — subscribes (populate `ProductSummaryView`)

### HTTP / API surface (one line)

`Marketplaces.Api` exposes marketplace and category-mapping management endpoints and an admin trigger surface for adapter operations.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (single default scheme).

## Prior event modeling

- `docs/planning/catalog-listings-marketplaces-discovery.md`
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-listings-marketplaces-glossary.md`

## ADRs

- ADR 0048 — Marketplace Document Entity Design
- ADR 0049 — Category Mapping Ownership
- ADR 0050 — Marketplaces ProductSummaryView Anti-Corruption Layer
- ADR 0051 — Vault Implementation Strategy
- ADR 0052 — Amazon SP-API Authentication
- ADR 0053 — Walmart Marketplace API Authentication
- ADR 0054 — eBay Sell API Authentication
- ADR 0055 — Submission Status Polling Architecture
- ADR 0056 — Marketplace Adapter Resilience Patterns
- ADR 0057 — Walmart Deactivation Identifier Design

## Source citations (S1 stub)

- `src/Marketplaces/`
- `src/Marketplaces/Marketplaces/Adapters/`
- `src/Shared/Messages.Contracts/Marketplaces/`
- `CONTEXTS.md` (section: `Marketplaces`)
- `docs/decisions/0048-marketplace-document-entity-design.md`
- `docs/decisions/0049-category-mapping-ownership.md`
- `docs/decisions/0050-marketplaces-product-summary-acl.md`
- `docs/decisions/0051-vault-implementation-strategy.md`
- `docs/decisions/0052-amazon-spapi-authentication.md`
- `docs/decisions/0053-walmart-marketplace-api-authentication.md`
- `docs/decisions/0054-ebay-sell-api-authentication.md`
- `docs/decisions/0055-submission-status-polling-architecture.md`
- `docs/decisions/0056-marketplace-adapter-resilience-patterns.md`
- `docs/decisions/0057-walmart-deactivation-identifier-design.md`
