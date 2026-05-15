# Listings

> **Source folder:** `src/Listings/`
> **Status:** Implemented
> **Most recent material milestone:** M36.1 — Listings BC closure (event-sourced lifecycle + recall cascade)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Listings owns the channel-listing lifecycle — the state machine that decides when and how a product SKU appears on a given marketplace channel. A listing moves through Draft → ReadyForReview → Submitted → Live → Paused → Ended, with branch states for force-down (e.g. recall cascade triggered by an upstream `ProductDiscontinued` event with `IsRecall=true`). Listings consumes Product Catalog events through a local `ProductSummaryView` anti-corruption layer so that internal Catalog status values never leak into the listing aggregate.

## Top-level structure

### Aggregates

- `Listing` — stream ID: UUID v5 from `listing:{sku}:{channelCode}` (`ListingStreamId.Compute`).

### Commands

- `CreateListing`
- `SubmitListingForReview`
- `ApproveListing`
- `ActivateListing`
- `PauseListing`
- `ResumeListing`
- `EndListing`

### Domain events

- `ListingDraftCreated`
- `ListingSubmittedForReview`
- `ListingApproved`
- `ListingActivated`
- `ListingPaused`
- `ListingResumed`
- `ListingEnded`
- `ListingForcedDown`
- `ListingContentUpdated`

### Projections

- `Listing` snapshot — inline, keyed by stream id.
- `ListingsActiveView` — inline.
- `ProductSummaryView` (ACL) — inline; populated by Product Catalog integration events (`Listings/ProductSummary/`).

### Integration events

- `Listings.ListingCreated` — publishes
- `Listings.ListingApproved` — publishes
- `Listings.ListingActivated` — publishes
- `Listings.ListingEnded` — publishes
- `Listings.ListingForcedDown` — publishes
- `Listings.ListingsCascadeCompleted` — publishes
- 9 `ProductCatalog.*` events — subscribes (populate `ProductSummaryView`)
- `Marketplaces.MarketplaceListingActivated` — subscribes (→ Live)
- `Marketplaces.MarketplaceSubmissionRejected` — subscribes (→ Ended)

### HTTP / API surface (one line)

`Listings.Api` exposes the listing-lifecycle commands and listing read endpoints consumed by Backoffice.

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

- ADR 0042 — `catalog:` Namespace UUID v5 Convention

## Source citations (S1 stub)

- `src/Listings/`
- `src/Shared/Messages.Contracts/Listings/`
- `src/Listings/Listings.Api/Program.cs` (projection registrations)
- `CONTEXTS.md` (section: `Listings`)
- `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
