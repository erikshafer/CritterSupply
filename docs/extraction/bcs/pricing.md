# Pricing

> **Source folder:** `src/Pricing/`
> **Status:** Implemented
> **Most recent material milestone:** M30.1 — Coupon-aware Shopping/Pricing integration (Shopping consumes published prices)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Pricing owns price rules per SKU — the base price, the floor (the lowest sellable price), the ceiling, the MAP (Minimum Advertised Price), scheduled price changes, and corrections. Pricing reacts to Product Catalog lifecycle events to set up price rules for new products, publishes prices that Shopping reads at add-to-cart time, and accepts vendor-originated price-suggestion submissions and MAP updates from Vendor Portal. Money is represented uniformly through a `Money` value object.

## Top-level structure

### Aggregates

- `ProductPrice` — stream ID: UUID v7.
- `Money` — value object (not an aggregate; canonical representation of monetary amounts).

### Commands

- `SetInitialPrice`
- `SetBasePrice`
- `ChangePrice`
- `SchedulePriceChange`
- `CancelScheduledPriceChange`
- `ActivateScheduledPriceChange`
- `FloorPriceSet`, `CeilingPriceSet` (price-bound commands surfaced as records)

### Domain events

- `ProductRegistered`
- `InitialPriceSet`
- `PriceChanged`
- `PriceCorrected`
- `FloorPriceSet`
- `CeilingPriceSet`
- `PriceChangeScheduled`
- `ScheduledPriceActivated`
- `ScheduledPriceChangeCancelled`
- `PriceDiscontinued`

### Projections

- `ProductPrice` snapshot — inline, keyed by stream id.
- `CurrentPriceView` — inline, keyed by SKU.

### Integration events

- `Pricing.PricePublished` — publishes
- `Pricing.PriceUpdated` — publishes
- `Pricing.VendorPriceSuggestionSubmitted` — bidirectional helper contract
- Subscribes to `ProductCatalog.ProductAdded` (price-rule bootstrap)

### HTTP / API surface (one line)

`Pricing.Api` exposes the price-management commands and current-price / price-history reads consumed by Shopping, Vendor Portal, and Backoffice.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (default + Backoffice schemes).

## Prior event modeling

- `docs/planning/pricing-event-modeling.md`
- `docs/planning/pricing-ux-review.md`

## ADRs

- ADR 0018 — Money Value Object as Canonical Monetary Representation
- ADR 0019 — Bulk Pricing Job Audit Trail via Event Sourcing
- ADR 0020 — MAP vs Floor Price Distinction

## Source citations (S1 stub)

- `src/Pricing/`
- `src/Shared/Messages.Contracts/Pricing/`
- `CONTEXTS.md` (section: `Pricing`)
- `docs/decisions/0018-money-value-object-canonical-currency.md`
- `docs/decisions/0019-bulk-pricing-job-audit-trail.md`
- `docs/decisions/0020-map-vs-floor-price-distinction.md`
- `docs/planning/pricing-event-modeling.md`
