# CritterSupply Bounded Contexts

A single-file, at-a-glance reference for every bounded context in the system. Each entry describes what the BC owns, which adjacent BCs it communicates with, and any non-obvious constraints worth knowing.

**Code is the source of truth** for events, commands, handlers, and message contracts. This file answers *"what does this BC own and who does it talk to?"* — nothing more. If something requires ongoing updates to stay accurate, it does not belong here.

---

## Implemented

### Shopping

**Folder:** `src/Shopping/`

Owns the customer's pre-purchase cart lifecycle — adding items, changing quantities, clearing, applying coupons, and handing off to checkout.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | → publishes | Checkout handoff when the customer commits to purchase |
| Promotions | → queries | ValidateCoupon for eligibility checks; CalculateDiscount for cart display |

**Key decisions:** Checkout was moved from Shopping to Orders in Cycle 8 ([ADR 0001](docs/decisions/0001-checkout-migration-to-orders.md)). Prices are frozen at add-to-cart time ([ADR 0017](docs/decisions/0017-price-freeze-at-add-to-cart.md)). Coupon integration completed in M30.1 with cart-level coupon application and discount calculation.

---

### Orders

**Folder:** `src/Orders/`

Owns commercial commitment — the checkout aggregate and the order lifecycle saga that orchestrates Payments, Inventory, and Fulfillment.

| Communicates with | Direction | Notes |
|---|---|---|
| Shopping | ← receives | Checkout handoff |
| Payments | ↔ bidirectional | Requests payment; receives capture/failure/refund results |
| Inventory | ↔ bidirectional | Requests reservation; receives confirmation/failure |
| Fulfillment | ↔ bidirectional | Requests fulfillment; receives shipment/delivery updates |
| Returns | ← receives | Return approval, completion, and rejection outcomes |
| Customer Identity | → queries | Address snapshots at checkout completion |
| Correspondence | → publishes | Order lifecycle events consumed for transactional emails |

**Key decisions:** Saga uses the Decider pattern with pure-function business logic ([ADR 0029](docs/decisions/0029-order-saga-design-decisions.md)). Address snapshots preserve temporal consistency ([ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md)).

---

### Payments

**Folder:** `src/Payments/`

Owns financial transaction lifecycle — authorization, capture, failure handling, and refunds.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ↔ bidirectional | Receives payment/refund requests; publishes results |

**Key decisions:** Strategy pattern (`IPaymentGateway`) supports multiple providers. Stripe is implemented; PayPal researched ([ADR 0010](docs/decisions/0010-stripe-payment-gateway-integration.md)).

---

### Inventory

**Folder:** `src/Inventory/`

Tracks stock levels and manages soft reservations (holds) until commitment or release. Entirely message-driven — no HTTP endpoints.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ↔ bidirectional | Receives reservation requests; publishes confirmation/failure |
| Fulfillment | ← receives | Shipment dispatch triggers stock adjustment |

**Constraint:** No HTTP layer exists today. Any UI integration (e.g., Backoffice) requires adding endpoints first.

---

### Fulfillment

**Folder:** `src/Fulfillment/`

Manages physical order fulfillment — pick, pack, ship, and delivery tracking.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ↔ bidirectional | Receives fulfillment requests; publishes shipment/delivery updates |
| Inventory | → publishes | Shipment dispatch adjusts stock |
| Returns | ↔ bidirectional | Receives approved returns; publishes when return shipment arrives |

**Constraint:** Currently a single fulfillment center (stub). Multi-warehouse routing is planned — see `docs/planning/fulfillment-evolution-plan.md`.

---

### Returns

**Folder:** `src/Returns/`

Manages return and exchange workflows — eligibility checks, inspection, refunds, and replacements.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | → publishes | Approval, denial, completion, and expiry outcomes |
| Fulfillment | ↔ bidirectional | Expects return receipt; publishes approval for reverse logistics |
| Payments | ↔ bidirectional | Requests refunds; receives completion confirmation |

**Constraint:** Cross-product exchanges supported (M35.0) with price difference handling — additional payment for more expensive replacements, partial refund for cheaper ones. Same-SKU exchanges also supported. 30-day eligibility window post-delivery.

---

### Customer Identity

**Folder:** `src/Customer Identity/`

Manages customer accounts, authentication, profile data, and saved addresses.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ← queried by | Address snapshot at checkout completion |
| Customer Experience | ← queried by | Profile and address data for storefront |

**Key decisions:** Uses EF Core (not Marten) for relational fit ([ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md)). Session-based cookie authentication ([ADR 0012](docs/decisions/0012-simple-session-based-authentication.md)).

---

### Customer Experience

**Folder:** `src/Customer Experience/`

Backend-for-Frontend (BFF) for the customer-facing Blazor storefront. Stateless composition of data from multiple BCs plus real-time push via SignalR.

| Communicates with | Direction | Notes |
|---|---|---|
| Shopping | ← receives + queries | Cart events via RabbitMQ; cart data via HTTP |
| Orders | ← receives + queries | Order events via RabbitMQ; order data via HTTP |
| Fulfillment | ← receives | Shipment events via RabbitMQ |
| Payments | ← receives | Refund events via RabbitMQ |
| Product Catalog | → queries | Product data via HTTP |
| Customer Identity | → queries | Profile and address data via HTTP |

**Key decisions:** Migrated from SSE to SignalR in Cycle 18 ([ADR 0013](docs/decisions/0013-signalr-migration-from-sse.md)). Owns no domain aggregates — pure composition and notification relay.

---

### Product Catalog

**Folder:** `src/Product Catalog/`

Product master data — SKU, name, description, images, manufacturer info. Event-sourced via Marten with `CatalogProduct` aggregate and `ProductCatalogView` inline projection.

| Communicates with | Direction | Notes |
|---|---|---|
| Pricing | → publishes | Product lifecycle events consumed by Pricing |
| Customer Experience | ← queried by | Product listings for storefront |

**Key decisions:** Migrated from Marten document store to event sourcing in M35.0 (Sessions 5+6). 11 domain events, full inline projection coverage. Legacy `Product` document model retained for vendor assignment handler and migration bootstrap. Vendor assignment (`AssignProductToVendor`) is the sole remaining document-store write path — planned for ES migration in M36.0. See `docs/planning/catalog-listings-marketplaces-evolution-plan.md` for variants/listings roadmap unlocked by the ES migration.

---

### Listings

**Folder:** `src/Listings/`

Owns the channel listing lifecycle — the state machine that governs when and how a product SKU is presented on a given marketplace channel. Event-sourced via Marten with a `Listing` aggregate covering the full lifecycle (Draft → ReadyForReview → Submitted → Live → Paused → Ended).

| Communicates with | Direction | Notes |
|---|---|---|
| Product Catalog | ← receives | 9 granular integration events populate `ProductSummaryView` ACL; `ProductDiscontinued` (IsRecall=true) triggers recall cascade |
| Marketplaces | → publishes | `ListingApproved` triggers adapter submission in Marketplaces BC |
| Backoffice | ← queried by | Listing list and detail views; admin lifecycle actions (approve/pause/end) |

**Key decisions:** Event-sourced `Listing` aggregate ([M36.1](./milestones/m36-1-milestone-closure-retrospective.md)). UUID v5 stream IDs derived from `listing:{sku}:{channelCode}` key — deterministic and collision-resistant across channel reactivations. `ProductSummaryView` ACL decouples Listings from Product Catalog internal status values via a `ProductSummaryStatus` enum. Recall cascade reacts to `ProductDiscontinued` (IsRecall=true) via choreography — no saga; force-downs all Live and Paused listings for the affected SKU and publishes `ListingsCascadeCompleted`.

---

### Marketplaces

**Folder:** `src/Marketplaces/`

Owns marketplace channel configuration and the adapter orchestration layer that submits listings to external marketplace APIs. Uses Marten document store (not event sourcing) for configuration entities: `Marketplace` (channel config, activation state, vault credential paths) and `CategoryMapping` (composite key `{ChannelCode}:{InternalCategory}`, 18 seed mappings across 3 channels).

| Communicates with | Direction | Notes |
|---|---|---|
| Listings | ← receives | `ListingApproved` triggers adapter submission; publishes `MarketplaceListingActivated` or `MarketplaceSubmissionRejected` back |
| Product Catalog | ← receives | 4 granular integration events populate Marketplaces-local `ProductSummaryView` ACL |

**Key decisions:** Document store for configuration data ([ADR 0048](../decisions/0048-marketplace-document-entity-design.md)). Category mapping ownership in Marketplaces BC ([ADR 0049](../decisions/0049-category-mapping-ownership.md)). `ProductSummaryView` ACL isolates Marketplaces from Listings message payload ([ADR 0050](../decisions/0050-marketplaces-product-summary-acl.md)). `IVaultClient` abstraction: `DevVaultClient` in Development, `EnvironmentVaultClient` in production ([ADR 0051](../decisions/0051-vault-implementation-strategy.md)). `IMarketplaceAdapter` interface with three production implementations: `AmazonMarketplaceAdapter` (LWA OAuth 2.0, [ADR 0052](../decisions/0052-amazon-spapi-authentication.md)), `WalmartMarketplaceAdapter` (client credentials, [ADR 0053](../decisions/0053-walmart-marketplace-api-authentication.md)), `EbayMarketplaceAdapter` (refresh token, two-step submit, [ADR 0054](../decisions/0054-ebay-sell-api-authentication.md)). `UseRealAdapters` config flag controls stub vs. real adapter registration — stubs are always active in Development/CI.

---

### Vendor Identity

**Folder:** `src/Vendor Identity/`

Vendor authentication, multi-tenancy, and invitation workflow.

| Communicates with | Direction | Notes |
|---|---|---|
| Vendor Portal | ← queried by | User and vendor data; receives invite commands |

**Key decisions:** EF Core + JWT Bearer tokens — JWT required for Blazor WASM cross-origin calls ([ADR 0028](docs/decisions/0028-jwt-for-vendor-identity.md)). Invitation tokens hashed with SHA-256 ([ADR 0024](docs/decisions/0024-sha256-token-hashing-vendor-invitations.md)).

---

### Vendor Portal

**Folder:** `src/Vendor Portal/`

BFF for vendor operations — order management, pricing updates, user administration, and team management. Blazor WASM frontend with tenant-isolated SignalR groups.

| Communicates with | Direction | Notes |
|---|---|---|
| Vendor Identity | → queries + commands | Authentication, user management |
| Orders | ← receives + queries | Vendor-filtered order events and data |
| Fulfillment | ← receives | Shipment events |
| Payments | ← receives | Refund events |
| Pricing | → queries + commands | Price history reads; MAP/floor price updates |

**Key decisions:** Blazor WASM with in-memory JWT storage ([ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md)). SignalR groups scoped to `vendor:{vendorId}` for tenant isolation. Team management BFF endpoints added in M35.0 (roster and pending invitations views via Marten projections of VendorIdentity events). Team management Blazor page planned for M36.0.

---

### Pricing

**Folder:** `src/Pricing/`

Price rules, MAP (Minimum Advertised Price) / floor prices, temporal pricing, and bulk price updates.

| Communicates with | Direction | Notes |
|---|---|---|
| Product Catalog | ← receives | Product lifecycle events trigger price rule setup |
| Shopping | → publishes | Published prices consumed by carts |
| Vendor Portal | ← receives commands | Vendor price updates; MAP violation alerts pushed back |

**Key decisions:** Money value object for canonical currency representation ([ADR 0018](docs/decisions/0018-money-value-object-canonical-currency.md)). MAP and floor prices are distinct concepts ([ADR 0020](docs/decisions/0020-map-vs-floor-price-distinction.md)). Bulk pricing uses a saga with approval workflow ([ADR 0019](docs/decisions/0019-bulk-pricing-job-audit-trail.md)).

---

### Correspondence

**Folder:** `src/Correspondence/`

Owns transactional customer communication — email and SMS messages triggered by business events across all BCs.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ← receives | OrderPlaced for order confirmations |
| Fulfillment | ← receives | ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed for tracking and delivery notifications |
| Returns | ← receives | ReturnApproved, ReturnDenied, ReturnCompleted, ReturnExpired for return workflow updates |
| Payments | ← receives | RefundCompleted for refund confirmations |
| Customer Identity | → queries | Customer email, phone, notification preferences (Phase 2+) |

**Key decisions:** Renamed from "Notifications" to avoid ambiguity with real-time UI updates handled by Customer Experience ([ADR 0030](docs/decisions/0030-notifications-to-correspondence-rename.md)). Event-sourced Message aggregate with retry lifecycle. M28.0 (Phase 1) implemented OrderPlaced with Stub EmailProvider. M31.0 (Phase 2) added 7 additional integration events and SMS channel infrastructure (StubSmsProvider). Real provider integration (SendGrid, Twilio) deferred to Phase 3+.

---

### Backoffice Identity

**Folder:** `src/Backoffice Identity/`

JWT-based authentication and authorization for internal admin users. Provides access tokens consumed by Backoffice and any BC requiring admin auth.

| Communicates with | Direction | Notes |
|---|---|---|
| Backoffice | ← queried by | Auth tokens for admin operations |

**Key decisions:** EF Core with policy-based RBAC — 7 roles from CopyWriter to SystemAdmin ([ADR 0031](docs/decisions/0031-admin-portal-rbac-model.md)). Single role per user in Phase 1. JWT Bearer authentication (not session-based, unlike Customer Identity).

---

### Backoffice

**Folder:** `src/Backoffice/`

Internal operations portal — BFF pattern composing data from multiple BCs with RBAC via Backoffice Identity. Blazor WASM frontend with role-based navigation, real-time alerts via SignalR, and write operations for product admin, pricing, inventory, returns, and user management.

| Communicates with | Direction | Notes |
|---|---|---|
| Backoffice Identity | → queries | JWT authentication and user management |
| Customer Identity | → queries | Customer profile and address data for CS workflows |
| Orders | → queries + commands | Order search, detail views, cancellation, order notes |
| Returns | → queries + commands | Return listing, detail, approve/deny actions |
| Product Catalog | → queries | Product list for admin views |
| Pricing | → queries + commands | Price management |
| Inventory | → queries + commands | Stock levels, adjustments, receiving |
| Fulfillment | → queries | Pipeline views for warehouse ops |
| Correspondence | → queries | Correspondence history for CS workflows |

**Key decisions:** BFF pattern with 8 feature folders (AlertManagement, CustomerService, DashboardReporting, OrderManagement, OrderNotes, ProductCatalog, ReturnManagement, WarehouseOperations). Blazor WASM with in-memory JWT storage, background token refresh, session-expired recovery UX. Marten projections for dashboard metrics and alert feeds. SignalR hub for real-time operational alerts. Built across M32.0–M35.0 milestones.

---

### Promotions

**Folder:** `src/Promotions/`

Owns promotional campaigns and coupon codes — creation, activation, issuance, validation, redemption, revocation, and usage tracking. Supports batch coupon generation and discount calculation.

| Communicates with | Direction | Notes |
|---|---|---|
| Shopping | ← queries | ValidateCoupon and CalculateDiscount HTTP queries for cart coupon application (M30.1) |
| Pricing (planned) | → queries | Will check MAP floor to prevent below-minimum discounts (M30.2+) |

**Key decisions:** M30.0 implemented complete redemption workflow. M30.1 integrated with Shopping BC for real-time cart coupon application. Event-sourced aggregates (Promotion, Coupon) with inline projections (`CouponLookupView` for O(1) validation). Coupons use deterministic UUID v5 stream IDs from code strings. Batch generation uses fan-out pattern via `OutgoingMessages`. Handlers manually append events via `session.Events.Append()` (not tuple returns).

---

## Planned

### Search

Product search, filtering, faceted navigation, and autocomplete. Read-only — projects data from Product Catalog and Pricing.

| Communicates with | Direction | Notes |
|---|---|---|
| Product Catalog | ← receives | Product data for indexing |
| Pricing | ← receives | Price data for indexing |
| Customer Experience | ← queried by | Search results for storefront |

---

### Recommendations

Personalized product suggestions based on order and cart history.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ← receives | Order history for model training |
| Shopping | ← receives | Cart activity for co-occurrence |
| Customer Experience | ← queried by | Suggestion results for storefront |

---

### Store Credit

Issue and redeem credits, gift cards, and refund-to-credit.

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ← receives | Refund-to-credit triggers |
| Payments | → publishes | Credit redemption adjusts payment |

---

### Analytics

Business intelligence — dashboards, reports, ML model training. Consumes events from across the system.

---

### Operations Dashboard

DevEx/SRE tooling — event stream visualization, saga explorer, dead-letter queue management.
