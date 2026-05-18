# CritterSupply Business Architecture Extraction

> **Status:** 🟡 In progress (M48.0 — all 18 BC dossiers at S2-full depth; 15 cross-BC workflows landed in S4; observations brief landed in S5; S6 ahead)
> **Milestone:** [M48.0](../planning/milestones/m48-0-plan.md)

## What this is

`docs/extraction/` is a descriptive record of CritterSupply's business architecture. It describes what exists in the system today — the bounded contexts, the aggregates each one owns, the events they emit, the workflows that span them — source-cited to specific files in the repository. It is a reference, not an evaluation: there are no judgments about whether any element is well-shaped, only descriptions of what is there.

## How it is organized

- `bcs/` — one dossier per implemented bounded context (18 total).
- `workflows/` — one trace per cross-BC business workflow (populated in S4).
- `observations.md` — structural observations across the system (populated in S5).
- `synthesis.md` — unified descriptive picture (populated in S6).

## Status

| Artifact | Session | Status |
|---|---|---|
| [Shopping](./bcs/shopping.md) | S2 | S2 full |
| [Orders](./bcs/orders.md) | S2 | S2 full |
| [Payments](./bcs/payments.md) | S2 | S2 full |
| [Inventory](./bcs/inventory.md) | S2 | S2 full |
| [Fulfillment](./bcs/fulfillment.md) | S2 | S2 full |
| [Returns](./bcs/returns.md) | S2 | S2 full |
| [Customer Identity](./bcs/customer-identity.md) | S2 | S2 full |
| [Customer Experience](./bcs/customer-experience.md) | S2 | S2 full |
| [Product Catalog](./bcs/product-catalog.md) | S2 | S2 full |
| [Listings](./bcs/listings.md) | S3 | S2 full |
| [Marketplaces](./bcs/marketplaces.md) | S3 | S2 full |
| [Vendor Identity](./bcs/vendor-identity.md) | S3 | S2 full |
| [Vendor Portal](./bcs/vendor-portal.md) | S3 | S2 full |
| [Backoffice Identity](./bcs/backoffice-identity.md) | S3 | S2 full |
| [Backoffice](./bcs/backoffice.md) | S3b | S2 full |
| [Pricing](./bcs/pricing.md) | S3b | S2 full |
| [Promotions](./bcs/promotions.md) | S3b | S2 full |
| [Correspondence](./bcs/correspondence.md) | S3b | S2 full |
| Commerce-core deep dive (9 BCs) | S2 | S2 full (all 9 dossiers complete) |
| Channels / vendor / admin deep dive (9 BCs) | S3 + S3b | S2 full (all 9 dossiers complete across S3 + S3b) |
| Cross-BC workflow traces | S4 | 15 of 15 workflows landed |
| Structural observations | S5 | S5 complete — see [`observations.md`](./observations.md) |
| Synthesis brief | S6 | Pending S6 |

## Ground rules

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should." Every sentence describes what is present, not how it ought to be.
2. **Source-cite specific files.** Every non-trivial claim points to a file path in the repository.
3. **No sibling-project references.** No mention of other reference architectures, successors, or sibling codebases.
4. **No successor framing.** Each artifact describes CritterSupply as a system that exists.
5. **Ubiquitous language per BC.** Each BC's events, commands, aggregates, and projections use that BC's own terms. Cross-BC translations only appear in workflow traces (S4).
6. **Purpose statement is one paragraph.** Stub-depth purpose paragraphs cover what the BC owns and why; deeper detail belongs in the S2 / S3 dossiers.

## Index

### Bounded contexts

- [Shopping](./bcs/shopping.md)
- [Orders](./bcs/orders.md)
- [Payments](./bcs/payments.md)
- [Inventory](./bcs/inventory.md)
- [Fulfillment](./bcs/fulfillment.md)
- [Returns](./bcs/returns.md)
- [Customer Identity](./bcs/customer-identity.md)
- [Customer Experience](./bcs/customer-experience.md)
- [Product Catalog](./bcs/product-catalog.md)
- [Listings](./bcs/listings.md)
- [Marketplaces](./bcs/marketplaces.md)
- [Vendor Identity](./bcs/vendor-identity.md)
- [Vendor Portal](./bcs/vendor-portal.md)
- [Pricing](./bcs/pricing.md)
- [Correspondence](./bcs/correspondence.md)
- [Backoffice Identity](./bcs/backoffice-identity.md)
- [Backoffice](./bcs/backoffice.md)
- [Promotions](./bcs/promotions.md)

### Workflows

Cross-BC business workflows synthesised from the 18 S2-full BC dossiers. Each file describes one workflow — the actors, the trace across BCs, the projections, the compensation paths, the variants, the tests as behavioural evidence, the ADRs, and the declared-vs-implemented gaps.

**Customer purchase & promotions**

- [Cart to checkout](./workflows/cart-to-checkout.md) — Customer adds to cart through Shopping; checkout triggers Orders saga.
- [Coupon and discount application](./workflows/coupon-and-discount-application.md) — Promotions evaluates coupons + automatic promotions; Pricing computes effective price.
- [Coupon redemption recording](./workflows/coupon-redemption-recording.md) — Order placement records redemption with DCB-protected tagged streams on Promotions.

**Order fulfillment**

- [Order saga](./workflows/order-saga.md) — Orders orchestrates Payments → Inventory → Fulfillment → Customer Experience.
- [Recall cascade](./workflows/recall-cascade.md) — Product Catalog recall fans out to Inventory holds, Orders pauses, Fulfillment intercepts.

**Returns**

- [Standard return + refund](./workflows/standard-return-refund.md) — Returns coordinates with Payments + Inventory for refund and restock.
- [Cross-product exchange](./workflows/cross-product-exchange.md) — Returns choreographs Inventory reservation + Payments delta capture/refund for cross-product exchanges (M47.0).

**Vendor lifecycle**

- [Vendor onboarding](./workflows/vendor-onboarding.md) — Vendor Identity tenant + user lifecycle; Vendor Portal subscribes to 9 events.
- [Vendor change request](./workflows/vendor-change-request.md) — Vendor Portal change-request state machine; cross-BC review is declared-not-wired.

**Marketplace & channel**

- [Marketplace listing submission](./workflows/marketplace-listing-submission.md) — Listings + Marketplaces submission to channel partners.

**Operator (Backoffice)**

- [Backoffice fan-in dashboards](./workflows/backoffice-fan-in-dashboards.md) — 7 upstream BCs feed 5 projections + 1 SignalR hub.
- [Backoffice customer service](./workflows/backoffice-customer-service.md) — BFF composition + HTTP proxies; 4 endpoints currently blocked by mis-spelled policy strings.
- [Backoffice operations health](./workflows/backoffice-operations-health.md) — Cross-schema dead-letter aggregator (M46.0/D).

**Cross-cutting**

- [Transactional communication](./workflows/transactional-communication.md) — Correspondence subscribes to 12 lifecycle events; queues `Message` streams; retries on `5 min` / `30 min` / `2 hr` schedule.
- [Storefront real-time updates](./workflows/storefront-real-time-updates.md) — Customer Experience BFF pushes 5 typed message families over a single SignalR hub.

### Cross-cutting

- [Structural observations](./observations.md) (S5 complete)
- Synthesis brief (Pending S6)
