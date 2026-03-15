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

**Constraint:** Exchanges are same-SKU only (Phase 1). 30-day eligibility window post-delivery.

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

Product master data — SKU, name, description, images, manufacturer info. Currently a Marten document store (non-event-sourced).

| Communicates with | Direction | Notes |
|---|---|---|
| Pricing | → publishes | Product lifecycle events consumed by Pricing |
| Customer Experience | ← queried by | Product listings for storefront |

**Constraint:** Planned migration to event sourcing for audit trail and variant support — see `docs/planning/catalog-listings-marketplaces-evolution-plan.md`.

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

BFF for vendor operations — order management, pricing updates, user administration. Blazor WASM frontend with tenant-isolated SignalR groups.

| Communicates with | Direction | Notes |
|---|---|---|
| Vendor Identity | → queries + commands | Authentication, user management |
| Orders | ← receives + queries | Vendor-filtered order events and data |
| Fulfillment | ← receives | Shipment events |
| Payments | ← receives | Refund events |
| Pricing | → queries + commands | Price history reads; MAP/floor price updates |

**Key decisions:** Blazor WASM with in-memory JWT storage ([ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md)). SignalR groups scoped to `vendor:{vendorId}` for tenant isolation.

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
| Backoffice (planned) | ← queried by | Auth tokens for admin operations |

**Key decisions:** EF Core with policy-based RBAC — 7 roles from CopyWriter to SystemAdmin ([ADR 0031](docs/decisions/0031-admin-portal-rbac-model.md)). Single role per user in Phase 1. JWT Bearer authentication (not session-based, unlike Customer Identity).

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

### Backoffice

Internal tooling for operations, customer service, and merchandising. BFF pattern composing data from multiple BCs with RBAC via Backoffice Identity.

---

### Operations Dashboard

DevEx/SRE tooling — event stream visualization, saga explorer, dead-letter queue management.
