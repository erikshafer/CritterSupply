# M48.0 Session 1 Retrospective — BC Inventory + Scaffolding

**Date:** 2026-05-15
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 1 — Scaffold `docs/extraction/`, land 18 stub dossiers

## Baseline

- Build at session open: **0 errors, 359 warnings** (`dotnet build` at repo root).
- Build at session close: **0 errors, 359 warnings** — identical (no code changed).
- Test counts: not run; this session is documentation-only and the prompt does not request a test run.
- Structural starting state: `docs/extraction/` did not exist; `CONTEXTS.md` lists 18 implemented BCs and 5 planned BCs.

## Items Completed

| Item | Description |
|------|-------------|
| S1a | Folder scaffold: `docs/extraction/`, `docs/extraction/bcs/`, `docs/extraction/workflows/` |
| S1b | `docs/extraction/README.md` — overview, status table (18 BC rows + 5 cross-cutting placeholders), ground rules, index |
| S1c | 9 commerce-core BC stubs (Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Customer Identity, Customer Experience, Product Catalog) |
| S1d | 9 channels/vendor/admin BC stubs (Listings, Marketplaces, Vendor Identity, Vendor Portal, Pricing, Correspondence, Backoffice Identity, Backoffice, Promotions) |
| S1e | This retrospective + `CURRENT-CYCLE.md` open M48.0 / close M46.0+M47.0 |

## S1a–S1d: Stub Inventory

One row per BC in CONTEXTS.md order. Counts are S1-stub enumerations from the source folder and `Messages.Contracts/`; deeper accounting is deferred to S2/S3.

| BC | Stub file | Aggregates | Domain events | Commands | Prior EM artifact |
|---|---|---|---|---|---|
| Shopping | `bcs/shopping.md` | 1 (Cart) | 9 | 8 | N |
| Orders | `bcs/orders.md` | 2 (Checkout, Order) | 11 | 11 | Y (`saga-discovery-design-session.md`) |
| Payments | `bcs/payments.md` | 1 (Payment) | 5 | 4 | Y (`saga-discovery-design-session.md`) |
| Inventory | `bcs/inventory.md` | 2 (ProductInventory, InventoryTransfer) | 27 | 21 | Y (`inventory-remaster-slices.md`, `inventory-remaster-phase-3-storyboarding.md`) |
| Fulfillment | `bcs/fulfillment.md` | 2 (WorkOrder, Shipment) | 56 | 27 | Y (`fulfillment-remaster-slices.md`, `fulfillment-evolution-plan.md`) |
| Returns | `bcs/returns.md` | 1 (Return) | 21 | 10 | N |
| Customer Identity | `bcs/customer-identity.md` | 0 (EF Core entities: Customer, CustomerAddress) | 0 | 6 | N |
| Customer Experience | `bcs/customer-experience.md` | 0 (BFF, no aggregates) | 0 | n/a (BFF) | N |
| Product Catalog | `bcs/product-catalog.md` | 1 ES (CatalogProduct) + 1 legacy doc (Product) | 12 | (deferred to S2) | Y (catalog-listings-marketplaces-* set) |
| Listings | `bcs/listings.md` | 1 (Listing) | 9 | 7 | Y (catalog-listings-marketplaces-* set) |
| Marketplaces | `bcs/marketplaces.md` | 0 ES; 2 Marten docs (Marketplace, CategoryMapping) | 0 | (deferred to S3) | Y (catalog-listings-marketplaces-* set) |
| Vendor Identity | `bcs/vendor-identity.md` | 0 (EF Core entities: VendorTenant, VendorUser, VendorUserInvitation) | 0 | 10 | Y (`vendor-portal-event-modeling.md`) |
| Vendor Portal | `bcs/vendor-portal.md` | 0 ES; 9 Marten document types | 0 | 7 | Y (`vendor-portal-event-modeling.md`) |
| Pricing | `bcs/pricing.md` | 1 (ProductPrice) + Money VO | 10 | 7 | Y (`pricing-event-modeling.md`) |
| Correspondence | `bcs/correspondence.md` | 1 (Message) | 4 | 1 | Y (`correspondence-event-model.md`) |
| Backoffice Identity | `bcs/backoffice-identity.md` | 0 (EF Core entity: BackofficeUser) | 0 | 7 | N |
| Backoffice | `bcs/backoffice.md` | 1 (OrderNote ES; 5 BFF projections) | 3 | 4 + read surface | Y (backoffice-event-modeling-* set) |
| Promotions | `bcs/promotions.md` | 2 (Promotion, Coupon) | 12 | 8 | Y (`promotions-event-modeling.md`) |

## Confirmation Checks

- **CritterBids / CritterCab / "successor" references in stubs:** `grep` over `docs/extraction/` returns matches only inside `README.md`'s ground-rules block (which restates the prohibition by name); no stub references any sibling project or successor framing.
- **Evaluative language in stubs:** `grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over `docs/extraction/bcs/` returns no matches.
- **Source citations:** Every stub source-cites at least its `src/<BC>/` folder and the BC's section of `CONTEXTS.md`. Every stub that names ADRs cites the ADR file in the citations block.
- **Stub template adherence:** Every stub contains the mandatory header block (Source folder / Status / Most recent material milestone / Stub depth), Purpose, Aggregates, Commands, Domain events, Projections, Integration events, HTTP / API surface line, Frontend surface line (or "Not applicable"), Identity / auth posture line (or "Not applicable"), Prior event modeling, ADRs, Source citations.

## Things Worth Noting About the BC Roster

- **Fulfillment is the largest event surface in the system.** 56 distinct `public sealed record` events in `Shipments/` + `WorkOrders/` (legacy `ShipmentDispatched` and `ShipmentDeliveryFailed` retained alongside the M41.0 successors `ShipmentHandedToCarrier` and `ReturnToSenderInitiated`). The S3 dossier will need a more structured grouping (Routing / Pick / Pack / Carrier / Lost / Returns) than a flat enumeration.
- **Inventory is the second-largest event surface** at 27 domain events across reservation, transfer, quarantine, cycle-count, backorder, and replenishment lifecycles.
- **Three BCs use EF Core, not Marten event sourcing:** Customer Identity, Vendor Identity, Backoffice Identity. Stubs note this explicitly in the Aggregates section.
- **Two BCs use Marten document store rather than event sourcing:** Marketplaces (Marketplace, CategoryMapping) and Vendor Portal (9 document types covering ChangeRequest, VendorAccount, Team, VendorProductCatalog, Analytics, NotificationPreferences). Stubs note this explicitly.
- **Two BFFs:** Customer Experience owns no aggregates and is pure composition + relay; Backoffice is a BFF but owns one event-sourced aggregate (`OrderNote`, per ADR 0037).
- **Promotions has two aggregates with different stream-ID strategies:** `Promotion` (UUID v7, registered as Marten DCB tag `PromotionStreamId`) and `Coupon` (UUID v5 from coupon code, registered as Marten DCB tag `CouponStreamId`).
- **Pricing surfaces both an aggregate and a value object** (`ProductPrice` and `Money`). The stub lists both per the milestone-plan special case.
- **`Promotions.Api/Program.cs` does not register `JwtBearer`** at S1 stub depth; the Identity/auth posture line records this. Authorization for promotion-administration endpoints will need explicit confirmation in S3.

## Cross-Reference Summary (Prior Event-Model Artifacts)

Of 18 BCs, **12 have at least one prior event-model artifact on file** in `docs/planning/`:

- Orders, Payments — `saga-discovery-design-session.md`
- Inventory — `inventory-remaster-slices.md`, `inventory-remaster-phase-3-storyboarding.md`
- Fulfillment — `fulfillment-remaster-slices.md`, `fulfillment-evolution-plan.md`
- Product Catalog, Listings, Marketplaces — the four `catalog-listings-marketplaces-*` artifacts
- Vendor Identity, Vendor Portal — `vendor-portal-event-modeling.md`
- Pricing — `pricing-event-modeling.md`, `pricing-ux-review.md`
- Correspondence — `correspondence-event-model.md`, `correspondence-risk-analysis-roadmap.md`
- Backoffice — `backoffice-event-modeling.md`, `backoffice-event-model-critique.md`, `backoffice-event-modeling-revised.md`
- Promotions — `promotions-event-modeling.md`

The **6 BCs with no prior event-model artifact on file** are: **Shopping, Returns, Customer Identity, Customer Experience, Backoffice Identity, and (effectively) Marketplaces** (Marketplaces' coverage is via the joint catalog-listings-marketplaces planning set rather than a Marketplaces-specific artifact). S2 will need to acknowledge that the Returns deep-dive will be the first formal modeling artifact for that BC, and that Shopping's deep-dive will likewise be the first dedicated modeling pass.

## Build State at Session Close

- Errors: 0 (delta from baseline: 0)
- Warnings: 359 (delta from baseline: 0; warning set unchanged)
- Files changed: 21 — `docs/extraction/README.md`, 18 BC stub files under `docs/extraction/bcs/`, this retrospective, and the `CURRENT-CYCLE.md` update. No code changes; no project file changes.

## Verification Checklist

- [x] `docs/extraction/`, `docs/extraction/bcs/`, and `docs/extraction/workflows/` all exist.
- [x] `docs/extraction/README.md` exists, lists every BC stub, and shows the status table.
- [x] All 18 BC stubs exist under `docs/extraction/bcs/` and follow the template.
- [x] Every stub has every required section (header / purpose / aggregates / commands / domain events / projections / integration events / HTTP-API surface line / frontend surface line or N/A / identity-auth posture line or N/A / prior event modeling / ADRs / source citations).
- [x] No stub contains evaluative language (verified by grep over `docs/extraction/bcs/`).
- [x] No stub references CritterBids, CritterCab, or any successor project (verified by grep).
- [x] No stub frames itself as preparation for a downstream operation.
- [x] This retrospective committed.
- [x] `CURRENT-CYCLE.md` updated.
- [x] `dotnet build` baseline recorded at session open and close (identical).

## What Remains / Next Session Should Verify

- **S2 — Commerce-core deep dive (9 BCs).** Deepen each commerce-core stub to dossier depth: behavioral description per command, projection key/lifecycle/source events, handler entry points, integration topology with cited message contracts, identity boundary detail.
- **S3 — Channels/vendor/admin deep dive (9 BCs).** Same shape, applied to Listings through Promotions.
- **Items deferred from this session:** None — the stub template was met for every BC. Where source-cited specifics could not be enumerated at stub depth (e.g. Product Catalog and Marketplaces command lists), the stub explicitly notes "deferred to S2/S3" rather than guessing.
- **Out-of-scope for the milestone:** the 5 Planned BCs in `CONTEXTS.md` (Search, Recommendations, Store Credit, Analytics, Operations Dashboard) — explicitly excluded per `m48-0-plan.md`.

The next session is **S2 — Commerce-core deep dive**.
