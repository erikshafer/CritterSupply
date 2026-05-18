# CritterSupply — Structural Observations

> **Source:** Aggregated from the 18 BC dossiers under `docs/extraction/bcs/` and the 15 cross-BC workflow traces under `docs/extraction/workflows/`. Cross-references the consolidated drift / declared-not-wired / declared-but-unemitted register from M48.0 sessions 2 through 4. Descriptive only.
> **Milestone:** [M48.0](../planning/milestones/m48-0-plan.md), Session 5.
> **Companion artifacts:** The dossiers and workflows are the load-bearing source; observations.md is a system-level pattern view above them. The S6 synthesis brief draws on observations.md and the underlying artifacts together.

This document is organized by category, not by bounded context. Each section opens with a pattern statement, then tabulates or enumerates the evidence with citations into the dossiers (`bcs/<bc>.md#<section>`) and workflow traces (`workflows/<workflow>.md`). Dossier sections cite code at line granularity; this document does not repeat those lower-level citations.

---

## Part I — System composition

### 1. BC type distribution

Five archetypes account for the 18 implemented bounded contexts. Event-sourced BCs predominate; three identity BCs use Entity Framework Core entity models; two BCs use the Marten document store as primary storage; and two BCs are pure backend-for-frontend composition layers (with one hybrid that owns a single event-sourced aggregate alongside its BFF role).

| Archetype | Count | BCs |
|---|---:|---|
| Event-sourced (Marten event store) | 11 | Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Product Catalog, Listings, Pricing, Promotions, Correspondence |
| EF Core entity-model | 3 | Customer Identity, Vendor Identity, Backoffice Identity |
| Marten document store (primary) | 2 | Marketplaces (Variant D), Vendor Portal |
| BFF (composition only) | 1 | Customer Experience |
| BFF + event-sourced hybrid | 1 | Backoffice (BFF composition over 7 upstream BCs + the `OrderNote` event-sourced aggregate per ADR 0037) |

Evidence: dossier headers (`bcs/*.md`, "Source folder" / "Dossier depth" front-matter). Specifically `bcs/customer-experience.md#purpose` (pure BFF), `bcs/backoffice.md#aggregates` (BFF + 1 ES aggregate), `bcs/marketplaces.md` ("Variant D — Marten document store BC"), `bcs/vendor-portal.md` (document store), `bcs/customer-identity.md`, `bcs/vendor-identity.md`, `bcs/backoffice-identity.md` (EF Core).

### 2. Per-BC structural counts

The per-BC count tables in the 18 dossiers cover aggregates, domain events, commands, projections (with lifecycle split), integration events (in / out), HTTP endpoints, and frontend pages where applicable. The table below aggregates the headline counts in one place; the dossier sections cited are the authoritative source.

| BC | Aggregates | Domain events | Commands | Projections | Integration: subscribes / publishes | HTTP endpoints | Frontend pages |
|---|---:|---:|---:|---:|---:|---:|---:|
| Shopping | 1 (`Cart`) | 9 (incl. `CartAbandoned` unemitted) | 6 | 1 inline snapshot | 0 / 1 | small | — |
| Orders | 2 (`Checkout`, `Order` saga) | Checkout: 5, Order: 16+ | Checkout: 4, Order saga: 12+ | Checkout snapshot (inline) | many in / 7 out | several | — |
| Payments | 1 (`Payment`) | extensive | several | inline | many in / several out | small | — |
| Inventory | 2 (`ProductInventory`, `Warehouse`) | extensive | several | 3 async + several inline | many in / several out | small | — |
| Fulfillment | 2 (`WorkOrder`, `Shipment`) | 55 in-domain (S2b reconciled) | 31 (S2b reconciliation) | inline | many in / several out (3 declared no instantiator) | small + webhook | — |
| Returns | 1 (`Return`) | extensive (cross-product exchange in M47.0) | extensive | inline | many in / many out | small | — |
| Customer Identity | 2 EF entities (`Customer`, `CustomerAddress`) | n/a (EF) | several | n/a | small / small | several | — |
| Customer Experience (BFF) | 0 | 0 | delegated | composition only | 8 subscribed (6 in CONTEXTS.md + 2 wired-but-undocumented) / 0 published | 20+ across delegate groups + 1 SignalR hub | 11 pages |
| Product Catalog | 1 (`CatalogProduct`) | extensive | several | inline | small in / several out | small | — |
| Listings | 1 (`Listing`) | extensive | several | inline (`ListingsActiveView`) | small in / several out | small | — |
| Marketplaces | 7 documents (S3a reconciled from 8) | n/a (doc store) | several | n/a | small in / several out | small | — |
| Vendor Identity | 3 EF entities (`VendorTenant`, `VendorUser`, `VendorUserInvitation`) | n/a | several | n/a | 0 in / 11 published (CONTEXTS.md omits) | several | — |
| Vendor Portal | 7 Marten documents (S3a reconciled from 9) | n/a | many | n/a | 9 vendor-identity in + 3 declared-not-wired + 7 declared-not-wired decision contracts / 3 declared-not-wired outbound + 1 declared-not-wired realtime | many | many (Blazor WASM) |
| Pricing | 1 (`ProductPrice`) | 8 (4 declared-unemitted) | 6 (S3b reconciled from S1 = 7; 2 misclassifications) | inline | small in / 3 outbound declared (0 wired) | small | — |
| Correspondence | 1 (`Message`) | several (incl. `MessageSkipped` declared-unemitted) | 1 + scheduled retries | inline | 12 in / 3 out | small | — |
| Backoffice Identity | 1 EF entity (`BackofficeUser`) | n/a | 7 (with 1 endpoint-resident DTO) | n/a | 0 in / 0 out | 3 anon auth + 5 user-management | several |
| Backoffice (BFF + hybrid) | 1 ES (`OrderNote`) + several read models | small | 4 + 1 HTTP-only proxy | 6 (1 snapshot + 5 BFF read models, 5 inline) | many in (7 upstream BCs) / 0 out + per-connection SignalR | many + 1 SignalR hub | many (Blazor WASM) |
| Promotions | 2 (`Promotion`, `Coupon`) | many (5 declared-unemitted) | 8 (S1=8, reconciled) | inline | several in / several out | small | — |

Evidence: each row's underlying figures live in `bcs/<bc>.md#aggregates`, `#commands`, `#domain-events`, `#projections`, `#integration-events`, `#http-api-surface`, `#frontend-surface` and the count-reconciliation notes in the dossier headers (Fulfillment 31 / 55, Marketplaces 7, Vendor Portal 7, Pricing 6, Customer Experience 6 → 8 documented edges). Where a dossier records a deviation from the S1 stub count, the dossier's own reconciliation note is authoritative.

### 3. Stream-ID strategies

Two stream-ID strategies are in use for event-sourced aggregates: UUID v7 (sortable, generated at command time) and UUID v5 (deterministic, SHA-1 hash from a namespace + key). The split is per-aggregate, not per-BC, and the S1 stubs misclassified two BCs that S2 / S3 reverified against source. The recurring pattern is "S1 claim does not bind — verify per aggregate."

| Strategy | Where in use (sampled) |
|---|---|
| UUID v7 | Most aggregates across most ES BCs (default); explicitly recorded in `bcs/backoffice.md` (`OrderNote`) and `bcs/correspondence.md` (`Message`) |
| UUID v5 (deterministic SHA-1) | Pricing `ProductPrice` (namespace `pricing:{SKU}`, ADR 0016 — refuted S1 v7 claim, verified S3b); Fulfillment both aggregates (`WorkOrder`, `Shipment` — refuted S1 v7 claim, verified S2b); Product Catalog `CatalogProduct` (per ADR 0042 catalog namespace); Promotions tagged streams |

Evidence: `bcs/pricing.md` ("Stream-ID note vs. S1 stub"), `bcs/fulfillment.md` ("Stream-ID note vs. S1 stub"), `bcs/backoffice.md#aggregates` (OrderNote v7), `bcs/correspondence.md` (Message v7), `bcs/promotions.md`, M48 retrospectives `m48-0-session-2b-retrospective.md` (Fulfillment) and `m48-0-session-3b-retrospective.md` (Pricing).

### 4. DCB usage

Marten Dynamic Consistency Boundary (DCB) is BC-specific and must be verified per BC against source. One BC uses DCB end-to-end; one BC was implicitly assumed to use DCB but does not. The pattern: "DCB-or-not must be verified per BC; it is not inferable from stream-ID strategy or prose."

| BC | DCB? | Evidence |
|---|---|---|
| Promotions | Yes (canonical) | Tag types `PromotionStreamId` / `CouponStreamId`, `[BoundaryModel]` + `IEventBoundary<T>` in `RedeemCouponHandler`, `DcbConcurrencyException` retry policy — verified S3b. See `bcs/promotions.md` and `workflows/coupon-redemption-recording.md`. |
| Pricing | No | Plain `Guid` stream IDs (UUID v5 from `pricing:{SKU}`), no tag types, no DCB Marten config — refuted S3b. See `bcs/pricing.md`. |
| Inventory | No (UUID v5 streams without DCB) | `bcs/inventory.md`. |
| All other ES BCs | No (default) | Per dossier per-BC. |

Evidence: `bcs/promotions.md` (verified DCB end-to-end), `bcs/pricing.md` (refuted), `workflows/coupon-redemption-recording.md` (the one workflow where DCB is operationally relevant), `m48-0-session-3b-retrospective.md` (DCB verification method).

### 5. Projection lifecycle distribution

Projections register as `Inline` by default across the codebase; async registrations are concentrated in one BC and are operationally-required-async per the M42.3 alert-feed work.

| Lifecycle | Notable instances |
|---|---|
| Inline (snapshot or read model) | The majority of projection registrations across ES BCs — `Order` snapshot, `Checkout` snapshot, `Cart` snapshot, `ListingsActiveView`, `ProductSummaryView` (Listings, Marketplaces), `OrderNote` snapshot, `AdminDailyMetrics`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView` |
| Async | Inventory: `AlertFeedView`, `NetworkInventorySummaryView`, `BackorderImpactView` (per M42.3) |
| Live aggregation (no projection persisted) | `Promotion`/`Coupon` DCB boundary queries (`EventTagQuery` in `RedeemCouponHandler`) |

Evidence: `bcs/inventory.md#projections`, `bcs/orders.md#projections`, `bcs/backoffice.md#projections` (1 snapshot + 5 read models), `bcs/promotions.md`, `workflows/coupon-redemption-recording.md`. The system-wide "inline by default" pattern is documented in the project's research note on projection lifecycle (`docs/research/projection-lifecycle-audit-2026-05.md`, referenced from `bcs/inventory.md`).

---

## Part II — Integration topology

### 6. Cross-BC edge inventory (transport split)

Four transports carry cross-BC communication in CritterSupply. Most edges are RabbitMQ publish/subscribe; synchronous HTTP carries a small set of cart-time, snapshot, and adapter calls; in-process Wolverine carries intra-host choreography; and SignalR carries operator/customer/vendor push.

| Transport | Where in use (representative edges) |
|---|---|
| RabbitMQ publish/subscribe | Order saga ↔ Payments / Inventory / Fulfillment / Customer Experience / Backoffice / Correspondence; Returns ↔ Inventory / Payments; Listings ↔ Marketplaces; Product Catalog → Listings (recall); Vendor Identity → Vendor Portal (9 events); Correspondence ← 12 upstream events |
| Synchronous HTTP (BC → BC) | Shopping → Pricing (cart-time price), Shopping → Promotions (coupon validation + discount calc), Customer Identity → Orders address-snapshot (`GetAddressSnapshot` per ADR 0002), Backoffice composition → upstream typed clients (`ICustomerIdentityClient` / `IOrdersClient` / `IReturnsClient` / `ICorrespondenceClient`), Customer Experience BFF → upstream typed clients |
| Synchronous HTTP (BC → external) | Marketplaces adapter set → Amazon SP-API / eBay / Walmart; Payments → card-network capture (per dossier) |
| In-process Wolverine (intra-host bus) | Promotions `RedeemCoupon` → `RecordPromotionRedemption` (post-supersession in ADR 0058); Customer Experience notification handlers → SignalR hub; Backoffice notification handlers → SignalR hub; Vendor Portal change-request submission round-trip on the same host |
| SignalR push | Customer Experience `StorefrontHub` (5 typed channels); Backoffice hub (`IBackofficeWebSocketMessage`); Vendor Portal hub |

Evidence: `workflows/order-saga.md`, `workflows/cart-to-checkout.md`, `workflows/coupon-and-discount-application.md`, `workflows/coupon-redemption-recording.md`, `workflows/marketplace-listing-submission.md`, `workflows/standard-return-refund.md`, `workflows/cross-product-exchange.md`, `workflows/vendor-onboarding.md`, `workflows/transactional-communication.md`, `workflows/storefront-real-time-updates.md`, `workflows/backoffice-customer-service.md`, `workflows/backoffice-fan-in-dashboards.md`, `workflows/backoffice-operations-health.md`.

### 7. Workflow participation (hub vs leaf BCs)

Per the S4 inventory of 15 workflows, BCs distribute into a small number of hubs (participating in many workflows) and a tail of leaf BCs (participating in one or two). Orders and Customer Experience are the highest-participation BCs; the identity BCs (Customer Identity, Vendor Identity, Backoffice Identity) are leaf-positioned because their cross-BC reach is single-purpose.

| BC | Workflows in which the BC participates (count + names) |
|---|---|
| Orders | 5 — `cart-to-checkout`, `order-saga`, `standard-return-refund`, `cross-product-exchange`, `backoffice-customer-service` |
| Customer Experience | 4 — `cart-to-checkout`, `standard-return-refund`, `cross-product-exchange`, `storefront-real-time-updates` |
| Backoffice | 5 — `backoffice-customer-service`, `backoffice-fan-in-dashboards`, `backoffice-operations-health`, `cross-product-exchange` (observer), `standard-return-refund` (observer) |
| Payments | 4 — `order-saga`, `standard-return-refund`, `cross-product-exchange`, `transactional-communication` |
| Inventory | 4 — `order-saga`, `standard-return-refund`, `cross-product-exchange`, `recall-cascade` |
| Fulfillment | 4 — `order-saga`, `standard-return-refund`, `recall-cascade`, `transactional-communication` |
| Returns | 3 — `standard-return-refund`, `cross-product-exchange`, `transactional-communication` |
| Correspondence | 1 hub — `transactional-communication` (fan-in from 12 events) |
| Promotions | 2 — `coupon-and-discount-application`, `coupon-redemption-recording` |
| Pricing | 1 — `coupon-and-discount-application` |
| Shopping | 3 — `cart-to-checkout`, `coupon-and-discount-application`, `coupon-redemption-recording` |
| Product Catalog | 1 — `recall-cascade` |
| Listings | 2 — `recall-cascade`, `marketplace-listing-submission` |
| Marketplaces | 1 — `marketplace-listing-submission` |
| Vendor Identity | 1 — `vendor-onboarding` |
| Vendor Portal | 2 — `vendor-onboarding`, `vendor-change-request` |
| Customer Identity | 2 — `cart-to-checkout` (address snapshot), `backoffice-customer-service` (composition source) |
| Backoffice Identity | 0 in cross-BC workflow traces (single-purpose auth issuance) |

Evidence: each workflow's `BCs involved` header; the per-BC table is the aggregation of those headers across `workflows/*.md`.

### 8. Fan-out patterns

A small number of integration events have many subscribers; a larger set of declared contracts have zero subscribers. Both shapes are observable across the system.

**High fan-out events (many subscribers):** `Orders.OrderPlaced` (subscribed by Customer Experience, Correspondence, Backoffice, and the saga loop itself); `Fulfillment.ShipmentDelivered` and `Fulfillment.ShipmentHandedToCarrier` (Customer Experience + Correspondence + Backoffice + Returns post-delivery window); the cross-product exchange event family (Returns + Inventory + Payments + Customer Experience + Backoffice). Evidence: `workflows/order-saga.md`, `workflows/storefront-real-time-updates.md`, `workflows/transactional-communication.md`, `workflows/cross-product-exchange.md`.

**Zero-subscriber declared contracts.** A consistent pattern across BCs is to define an outbound integration contract surface as records on the publishing side, with no consumer in any other BC under `src/`:

| BC | Declared outbound contracts with zero subscribers in `src/` |
|---|---|
| Pricing | `PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted` (S3b) |
| Vendor Portal | 3 change-request submission contracts (`DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested`) (S3a / S4) |
| Vendor Identity | `VendorUserActivated` (declared; subscribed by Vendor Portal; no producer in code) |

Evidence: `bcs/pricing.md#integration-events`, `bcs/vendor-portal.md#integration-events`, `workflows/vendor-change-request.md`, `workflows/vendor-onboarding.md`, `m48-0-session-3b-retrospective.md`.

### 9. Anticorruption layer patterns

Three shapes appear for cross-BC data access that does not rely on synchronous query:

1. **Subscription-driven local projection (ACL).** Listings owns a `ProductSummaryView` projection populated from Product Catalog integration events; Marketplaces also owns a `ProductSummaryView` projection populated from the same upstream — each maintained independently in its owning BC. Evidence: `bcs/listings.md#projections`, `bcs/marketplaces.md#projections`.
2. **BFF composition map (live HTTP composition).** Customer Experience composes views by calling 6 typed clients across upstream BCs at request time (`bcs/customer-experience.md#composition-map`). Backoffice composes its customer-service surface the same way across 4 typed clients (`bcs/backoffice.md`; `workflows/backoffice-customer-service.md`). These are not ACLs in the strict DDD sense but occupy the adjacent role of an inbound translation surface.
3. **BFF read-side projection set.** Backoffice maintains 5 BFF read-model projections fed from 7 upstream BCs (`AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`) — a hybrid of pattern 1 and pattern 2. Evidence: `bcs/backoffice.md#projections`, `workflows/backoffice-fan-in-dashboards.md`.

### 10. Saga / orchestration topology

Two saga orchestrators exist in the codebase. Every other BC participates in cross-BC behaviour through choreography. BFFs own no orchestration state.

| Orchestrator BC | Orchestration surface | Evidence |
|---|---|---|
| Orders | `Order` saga with 16+ states; Decider pattern per ADR 0029; drives Payments / Inventory / Fulfillment / Returns / Customer Experience | `bcs/orders.md#sagas-orchestration`, `workflows/order-saga.md` |
| Returns | `Return` aggregate state machine + cross-product exchange orchestration (M47.0) | `bcs/returns.md#sagas-orchestration`, `workflows/standard-return-refund.md`, `workflows/cross-product-exchange.md` |

All other BCs operate by choreography. The Order saga's relationship to Returns in the cross-product exchange workflow is that Orders acts as **acknowledger**, not orchestrator — the orchestrator is Returns. Evidence: `workflows/cross-product-exchange.md`.

### 11. Workflow type distribution

Per the S4 workflow inventory, the 15 workflows distribute across five type categories:

| Type | Count | Workflows |
|---|---:|---|
| Orchestration (saga) | 2 | `order-saga`, `cross-product-exchange` (Returns orchestrator) |
| Hybrid (orchestration + choreography) | 1 | `standard-return-refund` (Return aggregate state machine + cross-BC choreography) |
| Choreography (RabbitMQ-only) | 5 | `recall-cascade`, `coupon-redemption-recording`, `transactional-communication`, `vendor-onboarding`, `vendor-change-request` (declared-not-wired) |
| Choreography (sync HTTP) / query-only | 2 | `cart-to-checkout` (with an HTTP handoff), `coupon-and-discount-application` (synchronous HTTP only) |
| Read-only / read fan-in (subscription + projection or SignalR fan-out) | 4 | `backoffice-fan-in-dashboards`, `backoffice-customer-service`, `backoffice-operations-health`, `storefront-real-time-updates` |
| Hybrid (RabbitMQ + outbound HTTP) | 1 | `marketplace-listing-submission` |

Evidence: each workflow's `Type` header.

---

## Part III — Identity and authorization

### 12. Identity scheme distribution

Three identity schemes operate in CritterSupply, issued by three separate identity BCs and consumed across multiple application BCs:

| Scheme | Issuer BC | Consumer BCs | Token vehicle |
|---|---|---|---|
| Cookie session (`CritterSupply.Auth`, 7-day sliding) | Customer Identity (per ADR 0012) | Customer Experience (server-side); propagated to `StorefrontHub` via `?customerId=` query string on SignalR connection | HTTP cookie |
| JWT bearer (Vendor) | Vendor Identity (HMAC-SHA256 / `Jwt:SigningKey`, 15-min access + 7-day refresh cookie; refresh tokens not server-persisted) | Vendor Portal API (primary consumer); registered as a scheme on Storefront.Api and several other hosts as a hosting prerequisite (no endpoint uses it on those hosts) | `Authorization: Bearer` |
| JWT bearer (Backoffice) | Backoffice Identity (HMAC-SHA256 / `Jwt:SecretKey`, 15-min access + 7-day refresh; refresh tokens **persisted server-side** on `BackofficeUser.RefreshToken` — divergence from Vendor Identity) | Backoffice API; Customer Identity for some backoffice-side reads; registered as a hosting prerequisite on Storefront.Api | `Authorization: Bearer` + HttpOnly refresh cookie |

Cross-issuer JWT registration on multi-scheme hosts is governed by ADR 0032. Evidence: `bcs/customer-identity.md#identity-auth-posture`, `bcs/vendor-identity.md#identity-auth-posture`, `bcs/backoffice-identity.md#identity-auth-posture`, `bcs/customer-experience.md#identity-auth-posture`, `bcs/orders.md` ("Auth schemes registered (but not currently applied to endpoints)").

### 13. JWT issuance and validation map

The two JWT-issuing BCs diverge in refresh-token handling:

| Property | Vendor Identity | Backoffice Identity |
|---|---|---|
| Issuance algorithm | HMAC-SHA256 | HMAC-SHA256 |
| Symmetric key config | `Jwt:SigningKey` | `Jwt:SecretKey` |
| Access token lifetime | 15 minutes | 15 minutes |
| Refresh token lifetime | 7 days (cookie-borne) | 7 days |
| Refresh token persistence | Not persisted server-side; validity governed by cookie contents | Persisted on `BackofficeUser.RefreshToken`; validity gated on a server-side row lookup (immediate invalidation on Logout / Reset / Deactivate) |
| Password hashing | PBKDF2 (prior EM prescribed Argon2id) | PBKDF2 (prior EM prescribed Argon2id) |

Validation: every BC host that accepts JWT registers both issuers via multi-issuer scheme registration per ADR 0032; some hosts (e.g. Storefront.Api, Orders.Api) register the schemes without applying them to any endpoint.

Evidence: `bcs/vendor-identity.md#identity-auth-posture`, `bcs/backoffice-identity.md` ("Refresh-token persistence — divergence from Vendor Identity"), `bcs/orders.md` ("Auth schemes registered (but not currently applied to endpoints)").

### 14. RBAC and role distribution

The three identity BCs apply RBAC differently. Backoffice Identity defines a closed role enum; Vendor Identity uses tenant-scoped roles enforced through per-handler claim-check code; Customer Identity carries no RBAC.

| Identity BC | Role surface | Enforcement model |
|---|---|---|
| Backoffice Identity | 7 roles: `SystemAdmin`, `Executive`, `OperationsManager`, `WarehouseClerk`, `CustomerService`, `PricingManager`, `CopyWriter` (per ADR 0031, one role per user) | Per-endpoint `[Authorize(Policy = "...")]` policies registered in `Backoffice.Api/Program.cs`; case-sensitive against role claims |
| Vendor Identity | Tenant-scoped roles (Admin / CatalogManager / etc., enumerated in `bcs/vendor-identity.md`) | Per-handler claim-check code; no named policies in Vendor Portal API (`bcs/vendor-portal.md` records zero registered authorization policies) |
| Customer Identity | None | n/a |

Two role-claim / policy-string mismatches surfaced on the Backoffice side (covered in detail in section 21 below as the declared-vs-implemented pattern); the high-level observation is that Backoffice Identity emits role claims in **kebab-case** while Backoffice API registers policies in **PascalCase**, with four additional `CustomerService` endpoint policy strings mis-spelled at the call site.

Evidence: `bcs/backoffice-identity.md#identity-auth-posture` and "Drift note for S5"; `bcs/backoffice.md#identity-auth-posture`; `bcs/vendor-portal.md#identity-auth-posture`; `workflows/backoffice-customer-service.md` (auth status banner); `m48-0-session-3-retrospective.md` (mismatch first surfaced); `m48-0-session-3b-retrospective.md` (compound mismatches consolidated).

---

## Part IV — Shared concepts

### 15. Shared concept inventory

Several concepts appear in more than one BC, with one BC as primary owner and one or more BCs holding a replica, snapshot, or reference. The list below enumerates the recurring concepts and their owners.

| Concept | Owner | Referenced in |
|---|---|---|
| `Money` value object | Pricing | Orders (`Order` totals + line items), Payments (capture / refund amounts), Promotions (discount calculations), Returns (refund amounts) |
| `Sku` (catalog identifier) | Product Catalog | Listings (per-listing SKU), Marketplaces (per-listing SKU), Inventory (per-SKU `ProductInventory`), Pricing (per-SKU `ProductPrice` keyed by `pricing:{SKU}`), Shopping (`Cart` items), Orders (per-line SKU), Returns (per-return SKU) |
| `Address` | Customer Identity (`CustomerAddress` EF entity) | Orders captures a `ShippingAddressSnapshot` at checkout completion via `GetAddressSnapshot` per ADR 0002 |
| `CustomerId` | Customer Identity (no aggregate; an identity used cross-BC) | Orders, Returns, Correspondence, Customer Experience, Shopping (anonymous and authenticated), Backoffice (composition) |
| Status enums | Per BC | `OrderStatus` (Orders, 16 values across the saga state machine); `PaymentStatus` (Payments); `ReturnStatus` (Returns — 12 declared, 10 active; `LabelGenerated` + `InTransit` Phase-2 placeholders per S2b; M47.0 added `Cancelled`); `BackofficeUserStatus`; `VendorTenantStatus` |
| `OrderNote` | Backoffice (ES aggregate per ADR 0037 — note storage decision recorded as the only ES aggregate Backoffice owns) | Backoffice operator surface only |

Evidence: `bcs/pricing.md` (`Money`), `bcs/product-catalog.md` (`Sku` owner), `bcs/customer-identity.md` (`Address` owner) + `bcs/orders.md` (snapshot consumer; ADR 0002), `bcs/orders.md#sagas-orchestration` (`OrderStatus`), `bcs/returns.md` (`ReturnStatus` + `LabelGenerated`/`InTransit` declared-unused), `bcs/backoffice.md` (`OrderNote` per ADR 0037).

### 16. Snapshot vs live read patterns

Cross-BC reads divide into two patterns: data snapshotted at boundary crossing and data fetched live each time.

| Pattern | Instances |
|---|---|
| Snapshot at boundary | Orders requests `GetAddressSnapshot` from Customer Identity at checkout completion (`bcs/orders.md`, ADR 0002); the customer's address as of order time is preserved on the `Order` even if the customer subsequently edits the source `CustomerAddress`. Shopping freezes `UnitPrice` on cart items at the time the item is added (`bcs/shopping.md`) — the Pricing edge is queried at cart-mutation time, not at checkout. |
| Live HTTP query | Customer Experience composes cart, checkout, order, and customer-history views by calling typed clients on every request (`bcs/customer-experience.md#composition-map`); Backoffice composes its customer-service surface live on every operator action (`workflows/backoffice-customer-service.md`); Shopping fetches the authoritative price from Pricing each time the cart is mutated (`workflows/coupon-and-discount-application.md`); Shopping validates the coupon with Promotions live on each cart mutation. |

---

## Part V — Declared vs implemented patterns

### 17. Routes-without-instantiator

Across the system, integration contracts exist as records and route on a host's `Program.cs` but no handler in the owning BC instantiates them. Total instance count across all categorisations exceeds 25.

| BC | Routes without instantiator | Category |
|---|---|---|
| Fulfillment | `DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked` (3) | RabbitMQ contract |
| Vendor Identity | `VendorUserActivated` (1) | RabbitMQ contract |
| Vendor Portal | 3 outbound change-request submission contracts + 7 inbound decision contracts + 1 realtime `ForceLogout` (11) | RabbitMQ contracts + SignalR type |
| Backoffice | `IFulfillmentClient`, `IBackofficeIdentityClient`, `IPricingClient` typed clients registered without consumers (3); plus 3 SignalR types — see section 19 (3) | DI-registered typed client + SignalR type |
| Pricing | `PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted` (3 outbound contracts wired to zero subscribers anywhere in `src/`) | RabbitMQ contract |
| Promotions | `RecordPromotionRedemption` command record + validator retained for back-compat per ADR 0058 (the active path is `CouponRedeemed` choreography) | Command record |

Evidence: `bcs/fulfillment.md#integration-events`, `bcs/vendor-identity.md#integration-events`, `bcs/vendor-portal.md#routes-without-instantiator`, `bcs/backoffice.md` (typed-client registration), `bcs/pricing.md#integration-events`, `bcs/promotions.md` (ADR 0058 reference), `m48-0-session-2b-retrospective.md`, `m48-0-session-3-retrospective.md`, `m48-0-session-3b-retrospective.md`.

### 18. Declared-but-unemitted domain events

Across multiple BCs, domain events are declared as records with `Apply` branches on their aggregate, but no command, scheduled-message, or integration handler emits them in production code.

| BC | Declared-but-unemitted events |
|---|---|
| Pricing | `FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued` (4) |
| Promotions | `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired` (5) |
| Correspondence | `MessageSkipped` (1 — only emitted from a unit test) |
| Returns | `ReturnStatus` enum values `LabelGenerated`, `InTransit` (2 — Phase-2 placeholders) |
| Shopping | `CartAbandoned` (1 — no production emitter; Shopping.Api README documents an unimplemented background-job emitter) |

The recurring pattern is that aggregates declare future-state event surface ahead of the emitter being wired. Evidence: `bcs/pricing.md#domain-events`, `bcs/promotions.md#domain-events`, `bcs/correspondence.md#domain-events`, `bcs/returns.md#sagas-orchestration` (`ReturnStatus`), `bcs/shopping.md#aggregates` (`CartAbandoned`).

### 19. Declared SignalR / hub message types with no instantiator

Across the three SignalR hubs (Storefront, Backoffice, Vendor Portal), there are declared message types that have no producer in code.

| Hub | Declared message types with no producer |
|---|---|
| Backoffice (`IBackofficeWebSocketMessage`) | `ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented` (3 of 5 declared types; the 2 wired are `LiveMetricUpdated` and `AlertCreated`) |
| Vendor Portal | `ForceLogout` (declared with no producer and no client `ReceiveMessage` branch) |
| Customer Experience (`IStorefrontWebSocketMessage`) | None — all 5 SignalR channels are wired end-to-end |

Evidence: `bcs/backoffice.md` (declared-not-wired SignalR types), `bcs/vendor-portal.md#routes-without-instantiator`, `bcs/customer-experience.md#signalr-channels`, `workflows/backoffice-fan-in-dashboards.md`, `workflows/storefront-real-time-updates.md`.

### 20. Declared-not-wired cross-BC choreographies

The largest single instance is the vendor change-request workflow: 10 cross-BC routes (3 outbound submission contracts + 7 inbound decision contracts) declared on Vendor Portal with no counter-side producer / consumer anywhere in `src/`. The submission contracts round-trip the local Wolverine bus on the Vendor Portal API host only; the 7 inbound decision queues are silent in production.

A second instance is the vendor activation flow: `VendorUserActivated` has a Vendor Identity contract and a Vendor Portal subscriber, but no producer in code — the user-acceptance step that would publish it is not yet implemented (login → JWT issuance happens directly via `POST /api/vendor-identity/auth/login` regardless).

Evidence: `workflows/vendor-change-request.md`, `workflows/vendor-onboarding.md`, `bcs/vendor-portal.md#routes-without-instantiator`, `bcs/vendor-identity.md#integration-events`.

### 21. Auth-policy and role-claim case mismatches

Four compound mismatches surfaced across S3 / S3b / S4 on the Backoffice surface:

1. Backoffice Identity emits the `role` claim in **kebab-case** (e.g. `customer-service`) via `JwtTokenGenerator.ToRoleString()`; Backoffice API registers policies in **PascalCase** (`RequireRole("SystemAdmin")` style). The same mismatch surfaces in `LoginResponse.User.Role` (kebab) vs `RefreshTokenResponse.User.Role` and user-management responses (Pascal).
2. Four customer-service endpoints carry `[Authorize(Policy = "CustomerService")]` strings that the policy registry maps to the wrong role: `CustomerService` policy is registered against the role string `cs-agent`, while Backoffice Identity emits `customer-service`. The four endpoints affected: `GET /api/backoffice/customers/{customerId}`, `GET /api/backoffice/customers/{customerId}/correspondence`, `GET /api/backoffice/customers`, `GET /api/backoffice/orders/search`. `CustomerService`-role operators cannot access these endpoints.
3. The `ProductManager` policy maps to a role string `product-manager` — the `BackofficeRole` enum has no such value; the affected endpoints are reachable only by `SystemAdmin`.
4. The `Auditor` role is emitted by Backoffice Identity but admitted by no API policy except the catch-all `Backoffice` policy.

Evidence: `bcs/backoffice-identity.md` ("Drift note for S5"), `bcs/backoffice.md#identity-auth-posture`, `workflows/backoffice-customer-service.md` (status banner: "broken auth (4 endpoints with mis-spelled policy strings — operator access fails)"), `m48-0-session-3-retrospective.md`, `m48-0-session-3b-retrospective.md`, `m48-0-session-4-retrospective.md` (item 4 of "Cross-cutting observations surfaced for S5").

### 22. Stub-only provider abstractions

Correspondence registers `StubEmailProvider` and `StubSmsProvider` as singletons unconditionally; no production `IEmailProvider` / `ISmsProvider` implementations exist in tree; no `IPushProvider` interface is defined despite a `PushMessage` record existing; no feature-flag gating exists to switch between stub and production providers.

Evidence: `bcs/correspondence.md#identity-auth-posture` (provider section) and `bcs/correspondence.md#integration-events`; `workflows/transactional-communication.md` (Status header records "SMS channel infrastructure stubbed"); `m48-0-session-3b-retrospective.md` (Correspondence forward-note).

---

## Part VI — Documentation drift

### 23. CONTEXTS.md drift inventory

S2 → S4 retros consolidated 12+ drift items in `CONTEXTS.md` vs code. Items classify into four categories.

| Category | Items |
|---|---|
| Direction inversion | Vendor Portal `InventoryAdjusted` / `LowStockDetected` / `StockReplenished` listed as publishes when they are subscriptions |
| Omission | Customer Experience integration table omits Inventory and Returns edges; Vendor Identity entry omits 11 published events, `VendorTenantStatus` machine, and ADR 0032; Vendor Portal CONTEXTS.md neighbour list omits Inventory + Product Catalog; Backoffice CONTEXTS.md omits Payments subscription edge; Marketplaces CONTEXTS.md undercounts document types (`OrphanedEbayDraft` unmentioned); Customer Experience CONTEXTS.md likely omits SignalR hub coverage (see `bcs/customer-experience.md`) |
| Stale narrative | Product Catalog "sole remaining doc-store write path" line (`AssignProductToVendor`) — the handler is event-sourced in code today; Pricing line 237 claim "Bulk pricing uses a saga with approval workflow" — no saga exists, only a `BulkPricingJobId` correlation slot; Promotions most-recent-milestone listed as M30.1, actual is M40.0 (DCB); Correspondence line 250 references retired `ShipmentDispatched`; Fulfillment narrative still references retired `ShipmentDispatched` / `ShipmentDeliveryFailed`; Listings recall scope described as "all Live and Paused" while code force-downs all non-terminal listings (Draft / ReadyForReview / Submitted / Live / Paused) |
| Misclassification | Marketplaces `ProductSummaryView` misclassified in CONTEXTS.md; Backoffice's Fulfillment relationship documented as "queries" but is in fact subscription-driven with an unused typed client; Backoffice's Pricing typed client registered with no consumer (WASM `PriceEdit.razor` calls `/api/pricing/...` directly against the wrong base URL) |

Evidence: `m48-0-session-2-retrospective.md`, `m48-0-session-2b-retrospective.md`, `m48-0-session-3-retrospective.md` (Listings recall scope), `m48-0-session-3b-retrospective.md` (Pricing / Promotions / Backoffice / Correspondence), `m48-0-session-4-retrospective.md` (workflow-level drift); per-BC dossier "Drift note" sections.

### 24. Event-model vs code divergences

Event-model (EM) artifacts in `docs/planning/` describe intended designs that diverge from current code in several BCs.

| BC | EM-vs-code divergences |
|---|---|
| Backoffice | EM-revised divergences per S3b: `OrderNote` storage choice (resolved via ADR 0037); `AlertAcknowledgment` aggregate vs fields; `EscalationTicket` Phase-2; retired Fulfillment event names; EM-tabled-but-missing `RefundCompleted` and `StockReplenished` handlers; code-but-not-EM-tabled `BackorderCreatedHandler`, `GhostShipmentDetectedHandler`, `ShipmentLostInTransitHandler` |
| Vendor Portal | EM prescribed Argon2id for password hashing; code uses PBKDF2 |
| Backoffice Identity | EM prescribed Argon2id for password hashing; code uses PBKDF2 |
| Fulfillment | Prior-EM 49-event target predates the M41.0-S4 successor rename and the M41.0-S5 SLA / hazmat / claims expansion — current dossier reconciliation lists 55 in-domain events with the slice table preserved as the structural spine |

Evidence: `bcs/backoffice.md` ("EM reconciliation"), `bcs/vendor-portal.md`, `bcs/backoffice-identity.md`, `bcs/fulfillment.md` ("Reconciliation against current code"), `docs/planning/milestones/backoffice-event-modeling-revised.md` (referenced from `bcs/backoffice.md`).

### 25. Inline narrative drift in API READMEs

`Fulfillment.Api/README.md` narrative diagrams still reference the retired `ShipmentDispatched` and `ShipmentDeliveryFailed` events that have no domain-event counterpart in `Shipments/ShipmentEvents.cs` and are emitted by no handler in `src/Fulfillment/` — the M41.0 successors `ShipmentHandedToCarrier` and `ReturnToSenderInitiated` are emitted instead. The drift is in the README narrative diagrams, not in the contract surface. `Shopping.Api/README.md` documents an unimplemented background-job emitter for `CartAbandoned`. Evidence: `bcs/fulfillment.md` ("M41.0 legacy / successor pair"), `bcs/shopping.md#aggregates` (Abandoned state narrative).

---

## Part VII — ADR coverage

### 26. Cross-BC ADRs

ADRs that govern more than one BC:

| ADR | Scope | Touches BCs |
|---|---|---|
| ADR 0002 | Address snapshot at checkout | Customer Identity, Orders |
| ADR 0012 | Storefront cookie authentication | Customer Identity, Customer Experience |
| ADR 0016 | Pricing UUID v5 stream ID | Pricing |
| ADR 0029 | Decider pattern (canonical: Order saga) | Orders (canonical); pattern reused across BCs |
| ADR 0031 | Backoffice Phase-1 one-role-per-user constraint | Backoffice Identity, Backoffice |
| ADR 0032 | Multi-issuer JWT registration | Vendor Identity, Backoffice Identity, every BC that registers them as schemes |
| ADR 0037 | `OrderNote` as event-sourced aggregate in Backoffice | Backoffice |
| ADR 0042 | Catalog UUID v5 namespace | Product Catalog, Listings, Marketplaces |
| ADR 0048 / 0049 / 0050 | Marketplaces governance | Listings, Marketplaces, Product Catalog |
| ADR 0052 / 0053 / 0054 | Per-marketplace authentication (Amazon SP-API / eBay / Walmart) | Marketplaces |
| ADR 0055 | Orphaned eBay draft sweep / status polling | Marketplaces |
| ADR 0056 | Marketplaces resilience | Marketplaces |
| ADR 0058 | DCB on Promotions coupon redemption (supersedes the in-process `RecordPromotionRedemption` command path) | Promotions, Orders |
| ADR 0061 | Cross-product exchange replacement reservation | Returns, Inventory |
| ADR 0062 | Cross-product exchange Payments choreography | Returns, Payments |

Evidence: per-dossier `#adrs` sections; `workflows/cross-product-exchange.md`, `workflows/coupon-redemption-recording.md`, `workflows/marketplace-listing-submission.md` for the workflow-level cross-BC ADR usage.

### 27. ADR concentration per BC

Aggregated from the dossier `#adrs` sections, the ADR concentration is uneven across BCs.

| BC | Approximate ADR count cited in dossier | Notes |
|---|---:|---|
| Marketplaces | 8+ | Densest BC — per-marketplace auth (0052 / 0053 / 0054), governance (0048 / 0049 / 0050), orphaned-draft sweep (0055), resilience (0056) |
| Returns | 2+ | Cross-product exchange (0061, 0062); plus the cross-product workflow citations |
| Orders | 2+ | Decider pattern (0029); address snapshot (0002) |
| Backoffice + Backoffice Identity | 2+ | ADR 0031 (one-role-per-user), ADR 0037 (OrderNote ES) |
| Pricing | 1+ | ADR 0016 (UUID v5 stream ID) |
| Promotions | 1 | ADR 0058 (DCB) |
| Vendor Identity | 1+ | ADR 0032 (multi-issuer registration) |
| Customer Identity / Customer Experience | 1+ | ADR 0012 (cookie auth), ADR 0002 (address snapshot — Customer Identity side) |
| Product Catalog / Listings | 1+ | ADR 0042 (UUID v5 namespace) |
| Customer Experience (BFF) | low | Few ADRs concentrated on BFF — the BFF inherits ADRs from upstream BCs |
| Backoffice Identity, Vendor Portal, Shopping, Inventory, Fulfillment, Payments, Correspondence | low to mid | Per dossier sections |

Evidence: `bcs/*.md#adrs` sections per BC.

---

## Part VIII — Test coverage patterns

### 28. Gherkin / Reqnroll coverage

Gherkin feature counts per BC vary widely. The largest coverage sits on Customer Experience and Backoffice; many BCs have one or more Gherkin features tied to their dossier "Tests as behavioral evidence" sections.

| BC | Gherkin / Reqnroll surface |
|---|---|
| Customer Experience | ~65 scenarios across 4 feature files + Reqnroll bindings |
| Backoffice | 137 E2E + 47 BDD scenarios (S3b) |
| Returns | Cross-product exchange + standard return + refund features (`docs/features/returns/`) |
| Promotions | Promotion + coupon feature files |
| Pricing | Pricing feature file |
| Marketplaces | Marketplaces feature file(s) |
| Orders | Order placement + saga feature files |
| Shopping | Shopping / cart features |
| Vendor Portal | Vendor portal features |
| Customer Identity, Vendor Identity, Backoffice Identity, Correspondence, Listings, Product Catalog, Inventory, Fulfillment, Payments | Per dossier; mix of unit + integration; some without dedicated Gherkin |

Evidence: each `bcs/<bc>.md#tests-as-behavioral-evidence` section.

### 29. `@pending` / `@wip` / `@future` scenarios

Documented-but-unimplemented behaviours surfaced through scenario tags in the dossiers:

| BC | Tagged scenarios |
|---|---|
| Customer Experience | `@future` on stock-availability scenarios in product-browsing (S2c) |
| Returns | M47.0 closeout: the 5 `@pending` Gherkin scenarios in `cross-product-exchange.feature` were resolved during M47.0 — no `@pending` remains in this feature file as of M47.0 close (see `docs/planning/milestones/m47-0-closeout.md`) |

Evidence: `bcs/customer-experience.md#tests-as-behavioral-evidence`, `docs/features/returns/cross-product-exchange.feature` (M47.0 closeout note).

### 30. Test-suite gaps

BCs whose dossier "Tests as behavioral evidence" sections record no Gherkin / Reqnroll / E2E coverage:

| BC | Status |
|---|---|
| Backoffice Identity | No Gherkin / Reqnroll / E2E coverage — first formal modeling artifact was the S3a dossier itself |
| Vendor Identity | Worth confirming at synthesis depth — the dossier "Tests as behavioral evidence" section is the source |

Evidence: `bcs/backoffice-identity.md#tests-as-behavioral-evidence`, `bcs/vendor-identity.md#tests-as-behavioral-evidence`.

---

*End of structural observations. The synthesis brief (S6, `docs/extraction/synthesis.md`) draws on this document, the 18 BC dossiers, and the 15 workflow traces together to form the unified descriptive picture for M48.0.*
