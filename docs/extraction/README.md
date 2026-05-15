# CritterSupply Business Architecture Extraction

> **Status:** 🟡 In progress (M48.0)
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
| [Listings](./bcs/listings.md) | S1 | S1 stub |
| [Marketplaces](./bcs/marketplaces.md) | S1 | S1 stub |
| [Vendor Identity](./bcs/vendor-identity.md) | S1 | S1 stub |
| [Vendor Portal](./bcs/vendor-portal.md) | S1 | S1 stub |
| [Pricing](./bcs/pricing.md) | S1 | S1 stub |
| [Correspondence](./bcs/correspondence.md) | S1 | S1 stub |
| [Backoffice Identity](./bcs/backoffice-identity.md) | S1 | S1 stub |
| [Backoffice](./bcs/backoffice.md) | S1 | S1 stub |
| [Promotions](./bcs/promotions.md) | S1 | S1 stub |
| Commerce-core deep dive (9 BCs) | S2 | S2 full (all 9 dossiers complete) |
| Channels / vendor / admin deep dive (9 BCs) | S3 | Pending S3 |
| Cross-BC workflow traces | S4 | Pending S4 |
| Structural observations | S5 | Pending S5 |
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

(Populated in S4.)

### Cross-cutting

- Structural observations (Pending S5)
- Synthesis brief (Pending S6)
