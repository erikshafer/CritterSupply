# CritterSupply Bounded Contexts

This document defines the bounded contexts (BCs) within CritterSupply, an e-commerce reference architecture demonstrating event-driven systems using the Critter Stack (Wolverine, Marten, Alba).

**Status:** Living document. When implementing integrations between BCs, consult this file first—it defines message contracts and integration patterns. Update when architecture changes.

---

## Table of Contents

### Implemented Bounded Contexts
- [Shopping](#shopping) — Cart management, pre-purchase exploration
- [Orders](#orders) — Order lifecycle saga, checkout orchestration
- [Payments](#payments) — Payment capture, refunds
- [Inventory](#inventory) — Stock tracking, reservations
- [Fulfillment](#fulfillment) — Shipping, warehouse operations
- [Returns](#returns) — Return/exchange workflows
- [Customer Identity](#customer-identity) — Customer accounts, authentication
- [Customer Experience](#customer-experience) — BFF for Blazor storefront
- [Product Catalog](#product-catalog) — Product master data
- [Vendor Identity](#vendor-identity) — Vendor authentication, multi-tenancy
- [Vendor Portal](#vendor-portal) — BFF for vendor operations
- [Pricing](#pricing) — Price rules, MAP/floor prices

### Planned Bounded Contexts (Priority Order)
- [Correspondence](#correspondence) — Transactional emails/SMS (🔴 High - Cycle 28)
- [Promotions](#promotions) — Coupons, discounts (🔴 High - blocks commerce)
- [Search](#search) — Product search, filtering (🟡 Medium)
- [Recommendations](#recommendations) — Product suggestions (🟡 Medium)
- [Store Credit](#store-credit) — Credits, gift cards (🟡 Medium)
- [Analytics](#analytics) — Business intelligence (🟢 Low)
- [Admin Portal](#admin-portal) — Internal tooling (🟢 Low)
- [Operations Dashboard](#operations-dashboard) — DevEx/SRE tooling (🟢 Low)
- Additional contexts documented in "Future Considerations"

### Key Architectural Decisions
- [Checkout Migration (Cycle 8)](#checkout-migration) — Checkout moved from Shopping to Orders

---

## Checkout Migration

**ADR Reference:** [ADR 0001](docs/decisions/0001-checkout-migration-to-orders.md)

**Summary:** Checkout aggregate was migrated from Shopping BC to Orders BC in Cycle 8 to establish clearer boundaries:
- **Shopping BC** → pre-purchase exploration (cart management, future: wishlists, product browsing)
- **Orders BC** → transactional commitment (checkout + order lifecycle)
- **Integration:** Shopping publishes `CheckoutInitiated` → Orders handles and creates Checkout aggregate

**Completion:** Cycle 19.5 implemented the integration handler (`CheckoutInitiatedHandler`) and removed the obsolete `Shopping.Checkout` aggregate.

---

## Shopping

**Folder:** `Shopping Management/`
**Status:** ✅ Implemented (Cycles 3-8, 16-18)
**Docs:** Cart patterns in `docs/skills/marten-event-sourcing.md`

**Purpose:** Manages customer's pre-purchase experience — cart lifecycle from initialization to checkout handoff.

### Aggregates

**Cart** (event-sourced)
- **Events:** `CartInitialized`, `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged`, `CartCleared`, `CartAbandoned`, `CheckoutInitiated` (terminal)
- **Future Events (Phase 2+):** `CouponApplied`, `PriceRefreshed`, `PromotionApplied`, `CartAssignedToCustomer`
- **Lifecycle:** Cart ends with `CheckoutInitiated` (handoff to Orders) or `CartAbandoned` (anonymous timeout) or `CartCleared` (user action)

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| _(none — initiates flow)_ | `CheckoutInitiated` → Orders BC |

### Core Invariants
- Cart cannot contain items with zero/negative quantity
- Cart cannot transition to checkout if empty
- Unit prices captured at `ItemAdded` time (price-at-add-to-cart immutability, [ADR 0017](docs/decisions/0017-price-freeze-at-add-to-cart.md))
- Anonymous carts expire after TTL; authenticated carts persist indefinitely

### What it doesn't own
- Checkout process (Orders BC)
- Payment processing (Payments BC)
- Inventory operations (Inventory BC)
- Product catalog or pricing rules (Catalog/Pricing BCs)

### References
- **Skills:** `marten-event-sourcing.md` (aggregate patterns), `wolverine-message-handlers.md` (handler patterns)
- **ADRs:** [ADR 0001](docs/decisions/0001-checkout-migration-to-orders.md) (checkout handoff), [ADR 0017](docs/decisions/0017-price-freeze-at-add-to-cart.md) (price freeze)

---

## Orders

**Folder:** `Order Management/`
**Status:** ✅ Implemented (Cycles 4-9, 19.5, 24-27)
**Docs:** `docs/skills/wolverine-sagas.md`, [ADR 0029](docs/decisions/0029-order-saga-design-decisions.md)

**Purpose:** Owns commercial commitment and orchestrates order lifecycle across Payments, Inventory, Fulfillment via stateful saga.

### Aggregates

**Checkout** (event-sourced)
- **Events:** `CheckoutStarted`, `ShippingAddressSelected`, `ShippingMethodSelected`, `PaymentMethodProvided`, `CheckoutCompleted` (terminal)
- **Address Handling:** Stores `AddressId`, queries Customer Identity BC for `AddressSnapshot` at completion (temporal consistency, [ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md))

**Order Saga** (Wolverine saga + Decider pattern)
- **States:** `Placed`, `PendingPayment`, `PaymentConfirmed`, `PaymentFailed`, `OnHold`, `Fulfilling`, `Shipped`, `Delivered`, `Cancelled`, `ReturnRequested`, `Closed`
- **Architecture:** `OrderDecider` (pure functions) + `Order` saga class (Wolverine conventions)
- **Entry Point:** `Order.Start(CheckoutCompleted)` — maps integration message to domain command

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `CheckoutInitiated` (Shopping) | `OrderPlaced` → Inventory, Payments |
| `PaymentCaptured/Failed` (Payments) | `PaymentRequested` → Payments |
| `PaymentAuthorized` (Payments) | `ReservationCommitRequested` → Inventory |
| `ReservationCommitted/Failed` (Inventory) | `ReservationReleaseRequested` → Inventory |
| `ShipmentDispatched/Delivered` (Fulfillment) | `FulfillmentRequested` → Fulfillment |
| `ShipmentDeliveryFailed` (Fulfillment) | `RefundRequested` → Payments |
| `ReturnApproved/Completed/Rejected` (Returns) | `OrderCancelled`, `OrderCompleted` |
| `RefundCompleted/Failed` (Payments) | |

### Core Invariants
- Order cannot be placed without checkout prerequisites (address, payment method)
- Order cannot proceed to fulfillment without confirmed payment
- Order cannot be cancelled after shipment dispatch
- Refund cannot exceed original captured amount
- State transitions must follow valid paths

### What it doesn't own
- Payment gateway integration (Payments BC)
- Stock management (Inventory BC)
- Physical fulfillment (Fulfillment BC)
- Return inspection (Returns BC)
- Customer communications (Correspondence BC)

### References
- **Skills:** `wolverine-sagas.md` (saga patterns), `wolverine-message-handlers.md` (compound handlers)
- **ADRs:** [ADR 0001](docs/decisions/0001-checkout-migration-to-orders.md), [ADR 0014](docs/decisions/0014-checkout-migration-completion.md), [ADR 0029](docs/decisions/0029-order-saga-design-decisions.md)
- **Cycles:** [cycle-24-fulfillment-integrity-returns-prerequisites.md](docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

---

## Payments

**Folder:** `Payment Processing/`
**Status:** ✅ Implemented (Cycles 4, 6, 10)
**Docs:** `docs/skills/external-service-integration.md`, [ADR 0010](docs/decisions/0010-stripe-payment-gateway-integration.md)

**Purpose:** Owns financial transaction lifecycle — captures funds, handles failures, processes refunds.

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `PaymentRequested` (Orders) | `PaymentAuthorized` (async gateways) |
| `RefundRequested` (Orders) | `PaymentCaptured` → Orders |
| | `PaymentFailed` → Orders |
| | `RefundCompleted` → Orders |
| | `RefundFailed` → Orders |

### Payment Gateways
- **Stripe** (implemented, default) — [ADR 0010](docs/decisions/0010-stripe-payment-gateway-integration.md)
- **PayPal** (research complete) — `docs/planning/spikes/paypal-api-integration.md`, `docs/examples/paypal/`
- **Strategy Pattern:** `IPaymentGateway` interface with stub/production implementations

### Core Invariants
- Payment cannot be captured without authorization
- Refund cannot exceed captured amount
- Failed payments must include reason code
- Gateway interactions must be idempotent

### What it doesn't own
- Order business logic (Orders BC)
- Refund eligibility rules (Returns BC)
- Customer notifications (Correspondence BC)

### References
- **Skills:** `external-service-integration.md` (strategy pattern, graceful degradation)
- **ADRs:** [ADR 0010](docs/decisions/0010-stripe-payment-gateway-integration.md)
- **Spikes:** `docs/planning/spikes/paypal-api-integration.md`

---

## Inventory

**Folder:** `Inventory Management/`
**Status:** ✅ Implemented (Cycles 5, 24)
**Docs:** Reservation patterns in `wolverine-sagas.md`

**Purpose:** Tracks stock levels and manages soft reservations (holds) until commitment or release.

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `OrderPlaced` (Orders) | `ReservationConfirmed` → Orders |
| `ReservationCommitRequested` (Orders) | `ReservationFailed` → Orders |
| `ReservationReleaseRequested` (Orders) | `ReservationCommitted` → Orders |
| `ShipmentDispatched` (Fulfillment) | `ReservationReleased` → Orders |
| | `StockAdjusted` (internal) |

### Lifecycle (per reservation)
- `Pending` — hold placed, awaiting payment
- `Committed` — hard allocation, ready for fulfillment
- `Released` — hold released (cancellation/failure)
- `Expired` — auto-released after TTL

### Core Invariants
- Available stock = physical stock - reserved quantity
- Reservation cannot exceed available stock
- Committed reservations cannot be released (only via shipment dispatch)
- Stock adjustments must include reason code

### What it doesn't own
- Purchase order management (future Procurement BC)
- Multi-warehouse routing (Fulfillment BC)
- Replenishment automation (future)

### References
- **Skills:** `wolverine-sagas.md` (saga coordination), `marten-event-sourcing.md`
- **Cycles:** [cycle-24-fulfillment-integrity-returns-prerequisites.md](docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

---

## Fulfillment

**Folder:** `Fulfillment Management/`
**Status:** ✅ Implemented (Cycles 7, 24)
**Docs:** `docs/planning/fulfillment-evolution-plan.md`

**Purpose:** Manages physical order fulfillment — pick, pack, ship, track.

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `FulfillmentRequested` (Orders) | `ShipmentDispatched` → Orders, Inventory |
| `ReturnApproved` (Returns) | `ShipmentDelivered` → Orders |
| | `ShipmentDeliveryFailed` → Orders |
| | `ReturnReceived` → Returns |

### Fulfillment Center Network
- Phase 1: Single fulfillment center (stub implementation)
- Phase 2+: Multi-warehouse routing via Order Routing Engine (ORE) — see `fulfillment-evolution-plan.md`

### Core Invariants
- Shipment cannot be created without committed inventory reservation
- Tracking number must be captured at dispatch
- Delivery confirmation requires tracking event
- Pick/pack operations must respect order cutoff times (documented in evolution plan)

### What it doesn't own
- Inventory stock levels (Inventory BC)
- Shipping carrier integration (Phase 2+ — see evolution plan)
- Return inspection (Returns BC)

### References
- **Planning:** `docs/planning/fulfillment-evolution-plan.md` (multi-warehouse roadmap)
- **Cycles:** [cycle-24-fulfillment-integrity-returns-prerequisites.md](docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

---

## Returns

**Folder:** `Returns/`
**Status:** ✅ Implemented (Cycles 25-27)
**Docs:** Returns event model in CONTEXTS.md (Appendix A), `docs/planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md`

**Purpose:** Manages return and exchange workflows — eligibility, inspection, refunds, replacements.

### Aggregates

**Return** (event-sourced)
- **Types:** `ReturnType` enum — `Refund`, `Exchange`
- **Events:** `ReturnInitiated`, `ReturnApproved`, `ReturnDenied`, `InspectionStarted`, `InspectionPassed`, `InspectionFailed`, `RefundRequested`, `ReturnCompleted`, `ExchangeApproved`, `ExchangeDenied`, `ReplacementShipped`, `ReturnExpired`
- **Lifecycle:** Requested → Approved → Shipped → Inspecting → (Inspection outcome) → terminal state

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `ShipmentDelivered` (Fulfillment) | `ReturnApproved` → Orders, Fulfillment |
| `ReturnReceived` (Fulfillment) | `ReturnDenied` → Orders |
| `RefundCompleted` (Payments) | `ReturnCompleted` → Orders, Payments |
| | `RefundRequested` → Payments |
| | `ReturnExpired` → Orders |

### Core Invariants
- Return cannot be initiated outside eligibility window (30 days post-delivery)
- Return cannot skip inspection (except advanced automation Phase 2+)
- Refund cannot be issued before inspection passes
- Exchange replacement price must be ≤ original item price (no upcharge)
- Exchange must be same SKU (Phase 1 constraint)

### What it doesn't own
- Order eligibility (queries Orders BC)
- Payment refund processing (Payments BC)
- Return shipment logistics (customer ships, Fulfillment receives)

### References
- **Cycles:** [cycle-25-returns-bc-phase-1.md](docs/planning/cycles/cycle-25-returns-bc-phase-1.md), [cycle-26-returns-bc-phase-2-retrospective.md](docs/planning/cycles/cycle-26-returns-bc-phase-2-retrospective.md), [cycle-27-returns-bc-phase-3-retrospective.md](docs/planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md)
- **Skills:** `wolverine-message-handlers.md`, `marten-event-sourcing.md`
- **Event Model:** See Appendix A below for detailed Return lifecycle

---

## Customer Identity

**Folder:** `Customer Identity/`
**Status:** ✅ Implemented (Cycles 10-13, 17, 19)
**Docs:** [ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md), [ADR 0012](docs/decisions/0012-simple-session-based-authentication.md)

**Purpose:** Manages customer accounts, authentication, profile data, addresses.

### Technology

- **Database:** EF Core + PostgreSQL (not Marten — relational fit, [ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md))
- **Authentication:** Session-based (cookie auth, [ADR 0012](docs/decisions/0012-simple-session-based-authentication.md))

### Subdomains

- **Registration/Authentication** — Account creation, login, session management
- **Profile Management** — Customer info (name, email, phone)
- **Address Management** — Saved shipping addresses (CRUD, address snapshots for orders)

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| _(minimal integration — command-driven)_ | `CustomerRegistered` → Analytics |
| | `AddressSnapshot` queries (Orders BC) |

### Core Invariants
- Email address must be unique (cannot create duplicate accounts)
- Customer cannot authenticate without verified email (Phase 2+)
- Address snapshot is immutable — represents address as it existed at query time
- Session tokens must expire after inactivity period

### Address Snapshot Pattern

Orders BC queries `/api/identity/addresses/{addressId}/snapshot` at checkout completion. Returns immutable `AddressSnapshot` record embedded in `CheckoutCompleted` integration message. Ensures temporal consistency — orders preserve address as it existed at checkout time.

### What it doesn't own
- Order history (Orders BC)
- Payment methods (Payments BC — Phase 2+)
- Loyalty points (future Loyalty BC)
- Marketing preferences (future Marketing BC)

### References
- **Skills:** `efcore-wolverine-integration.md` (EF Core + Wolverine patterns)
- **ADRs:** [ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md), [ADR 0012](docs/decisions/0012-simple-session-based-authentication.md)
- **Cycles:** [cycle-17-customer-identity-integration.md](docs/planning/cycles/cycle-17-customer-identity-integration.md)

---

## Customer Experience

**Folder:** `Customer Experience/`
**Status:** ✅ Implemented (Cycles 16-20)
**Docs:** `docs/skills/bff-realtime-patterns.md`, [ADR 0013](docs/decisions/0013-signalr-migration-from-sse.md)

**Purpose:** Backend-for-Frontend (BFF) for customer-facing Blazor storefront — stateless composition + real-time updates.

### Architecture Pattern

**Stateless Composition BFF** — aggregates data from multiple BCs (Shopping, Orders, Catalog, Customer Identity) into view models for Blazor UI.

**Projects:**
- `Storefront/` (domain project) — Composition/, Clients/, Notifications/ (integration handlers)
- `Storefront.Api/` (Web SDK) — Queries/, Clients/ (implementations), StorefrontHub.cs (SignalR)
- `Storefront.Web/` (Blazor Server) — MudBlazor UI, SignalR client

**Real-Time Updates:** SignalR hub at `/hub/storefront` ([ADR 0013](docs/decisions/0013-signalr-migration-from-sse.md), migrated from SSE in Cycle 18)

### Integration Contract

| Receives (via RabbitMQ → SignalR) | Queries (HTTP) | Publishes |
|----------|-----------|-----------|
| `ItemAdded` (Shopping) | Shopping BC (cart) | _(none — pure BFF)_ |
| `CheckoutInitiated` (Shopping) | Orders BC (orders) | |
| `OrderPlaced` (Orders) | Catalog BC (products) | |
| `ShipmentDispatched` (Fulfillment) | Customer Identity BC (profile, addresses) | |
| `ShipmentDelivered` (Fulfillment) | | |
| `RefundCompleted` (Payments) | | |

### Core Patterns
- **Integration handlers** live in `Storefront/Notifications/` (domain project)
- **Handlers push to SignalR** via `IEventBroadcaster` → `StorefrontHub`
- **Blazor components subscribe** via `HubConnection` (MudBlazor `MudSnackbar` for toasts)
- **View composition** in `Storefront.Api/Queries/` endpoints

### What it doesn't own
- Domain aggregates (other BCs own truth)
- Business logic (delegates to domain BCs)
- Persistent state (ephemeral composition only)

### References
- **Skills:** `bff-realtime-patterns.md` (BFF patterns), `wolverine-signalr.md` (SignalR transport)
- **ADRs:** [ADR 0004](docs/decisions/0004-sse-over-signalr.md) (SSE rationale), [ADR 0013](docs/decisions/0013-signalr-migration-from-sse.md) (SignalR migration)
- **Cycles:** [cycle-16-customer-experience.md](docs/planning/cycles/cycle-16-customer-experience.md), [cycle-18-customer-experience-phase-2.md](docs/planning/cycles/cycle-18-customer-experience-phase-2.md)

---

## Product Catalog

**Folder:** `Product Catalog/`
**Status:** ✅ Implemented (Cycles 21-22)
**Docs:** `docs/planning/catalog-listings-marketplaces-glossary.md`, Evolution plan in `catalog-listings-marketplaces-evolution-plan.md`

**Purpose:** Product master data — SKU, name, description, images, manufacturer info.

### Current Implementation

- **Document Store** (Marten, non-event-sourced) — CRUD operations on `Product` documents
- **Phase 0:** Product model with `sku`, `name`, `description`, `price` (embedded), `manufacturerName`

### Future Evolution (Cycles 29-35)

**Planned Migration:** Document store → Event-sourced aggregates ([ADR 0026](docs/decisions/0026-polecat-sql-server-migration.md), catalog-specific decision pending)

**Rationale:** Support recall cascades, compliance audits, price history, variant models (see `catalog-listings-marketplaces-evolution-plan.md`)

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| _(commands from Admin Portal)_ | `ProductAdded` → Pricing BC (Phase 1) |
| | `ProductDiscontinued` → Pricing BC |

### What it doesn't own
- Price rules (Pricing BC)
- Inventory levels (Inventory BC)
- Marketplace listings (future Listings BC)
- Product search indexing (future Search BC)

### References
- **Planning:** `docs/planning/catalog-listings-marketplaces-cycle-plan.md` (Cycles 29-35 roadmap), `catalog-listings-marketplaces-glossary.md` (domain language), `catalog-variant-model.md` (variant design)
- **Skills:** `marten-document-store.md` (current implementation)

---

## Vendor Identity

**Folder:** `Vendor Identity/`
**Status:** ✅ Implemented (Cycles 22-23)
**Docs:** [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md), [ADR 0028](docs/decisions/0028-jwt-for-vendor-identity.md)

**Purpose:** Vendor authentication, multi-tenancy, invitation workflow.

### Technology

- **Database:** EF Core + PostgreSQL (same pattern as Customer Identity)
- **Authentication:** JWT Bearer tokens ([ADR 0028](docs/decisions/0028-jwt-for-vendor-identity.md)) — required for Blazor WASM cross-origin calls

### Multi-Tenancy Model

- **Vendor** (tenant) — `VendorId` GUID, company name, status (Active/Suspended)
- **VendorUser** — Users belong to a single vendor, `VendorId` embedded in JWT claims
- **Invitation workflow** — SHA-256 hashed tokens ([ADR 0024](docs/decisions/0024-sha256-token-hashing-vendor-invitations.md))

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| _(commands from Vendor Portal)_ | `VendorRegistered` → Analytics |
| | `VendorUserInvited`, `VendorUserAccepted` |

### Core Invariants
- Vendor email must be unique per vendor (not globally unique — different vendors can invite same email)
- JWT tokens must include `VendorId` claim for tenant isolation
- Invitation tokens expire after 7 days
- Vendor must be Active for users to authenticate

### What it doesn't own
- Vendor product pricing (Pricing BC)
- Vendor order history (Vendor Portal aggregates from Orders BC)
- Vendor payout processing (future)

### References
- **Skills:** `efcore-wolverine-integration.md`
- **ADRs:** [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md), [ADR 0024](docs/decisions/0024-sha256-token-hashing-vendor-invitations.md), [ADR 0028](docs/decisions/0028-jwt-for-vendor-identity.md)
- **Cycles:** [cycle-22-retrospective.md](docs/planning/cycles/cycle-22-retrospective.md), [cycle-23-plan.md](docs/planning/cycles/cycle-23-plan.md)

---

## Vendor Portal

**Folder:** `Vendor Portal/`
**Status:** ✅ Implemented (Cycles 22-23)
**Docs:** `docs/skills/blazor-wasm-jwt.md`, [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md)

**Purpose:** BFF for vendor operations — order management, pricing updates, user administration.

### Architecture Pattern

**Stateless BFF + RBAC** — Blazor WASM frontend with JWT authentication, SignalR for real-time order updates.

**Projects:**
- `VendorPortal/` (domain project) — Composition/, Clients/, Notifications/ (integration handlers)
- `VendorPortal.Api/` (Web SDK) — Queries/, Commands/, VendorPortalHub.cs (SignalR)
- `VendorPortal.Web/` (Blazor WASM) — MudBlazor UI, JWT in-memory storage ([ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md))

**Roles:** `VendorAdmin`, `OrderManager`, `PricingManager`

### Integration Contract

| Receives (via RabbitMQ → SignalR) | Queries (HTTP) | Commands (HTTP) |
|----------|-----------|-----------|
| `OrderPlaced` (Orders, multi-vendor filter) | Orders BC (vendor orders) | Pricing BC (set MAP/floor) |
| `ShipmentDispatched` (Fulfillment) | Pricing BC (price history) | Vendor Identity BC (invite users) |
| `RefundCompleted` (Payments) | Vendor Identity BC (users) | |

### Real-Time Updates

- **SignalR groups:** `vendor:{vendorId}` — tenant-isolated push
- **JWT auth:** `AccessTokenProvider` delegate in Blazor WASM ([ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md))
- **Token refresh:** Background `System.Threading.Timer` (no `IHostedService` in WASM)

### What it doesn't own
- Vendor authentication (Vendor Identity BC)
- Order fulfillment (Fulfillment BC)
- Product catalog (Catalog BC)

### References
- **Skills:** `blazor-wasm-jwt.md` (WASM patterns), `wolverine-signalr.md` (JWT auth + groups)
- **ADRs:** [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md), [ADR 0025](docs/decisions/0025-blazor-wasm-poc-learnings.md)
- **Cycles:** [cycle-23-plan.md](docs/planning/cycles/cycle-23-plan.md)

---

## Pricing

**Folder:** `Pricing/`
**Status:** ✅ Implemented (Cycles 22-23)
**Docs:** `docs/planning/pricing-event-modeling.md`, [ADR 0018](docs/decisions/0018-money-value-object-canonical-currency.md), [ADR 0020](docs/decisions/0020-map-vs-floor-price-distinction.md)

**Purpose:** Price rules, MAP (Minimum Advertised Price) / floor prices, temporal pricing, bulk price updates.

### Aggregates

**PriceRule** (event-sourced)
- **Events:** `PriceRuleCreated`, `PriceActivated`, `PriceExpired`, `MAPFloorSet`, `FloorPriceSet`
- **Lifecycle:** Draft → Active → Expired (or Scheduled → Active)
- **Audit Trail:** Every price change persisted as event (who, when, why)

**BulkPricingJob** (saga, [ADR 0019](docs/decisions/0019-bulk-pricing-job-audit-trail.md))
- **Purpose:** Apply price changes to many SKUs with approval workflow
- **Events:** `BulkJobCreated`, `BulkJobApproved`, `BulkJobApplied`, `BulkJobCompleted`

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `ProductAdded` (Catalog) | `PricePublished` → Catalog, Shopping |
| `ProductDiscontinued` (Catalog) | `PriceExpired` → Catalog, Shopping |
| Commands (Admin Portal / Vendor Portal) | `MAPViolationDetected` → Vendor Portal |

### Core Invariants
- SKU must have exactly one active base price
- Promotional price cannot be below MAP floor (if configured)
- Price history is immutable (superseded, never modified)
- Scheduled price changes use Wolverine delayed messages (durable scheduling)

### What it doesn't own
- Product catalog data (Catalog BC)
- Coupon codes or promotion eligibility (Promotions BC)
- Vendor cost basis (Vendor Portal BC)

### References
- **Planning:** `docs/planning/pricing-event-modeling.md`, `pricing-ux-review.md`
- **ADRs:** [ADR 0017](docs/decisions/0017-price-freeze-at-add-to-cart.md), [ADR 0018](docs/decisions/0018-money-value-object-canonical-currency.md), [ADR 0019](docs/decisions/0019-bulk-pricing-job-audit-trail.md), [ADR 0020](docs/decisions/0020-map-vs-floor-price-distinction.md)
- **Cycles:** [cycle-22-retrospective.md](docs/planning/cycles/cycle-22-retrospective.md)

---

## Planned Bounded Contexts

The following BCs are identified for future implementation. Detailed planning docs exist where referenced.

---

## Correspondence

**Status:** 🔴 **High Priority — Cycle 28**
**Docs:** `docs/planning/correspondence-event-model.md`, `correspondence-risk-analysis-roadmap.md`, [ADR 0030](docs/decisions/0030-notifications-to-correspondence-rename.md)

**Purpose:** Transactional customer communications — order confirmations, shipping updates, return status via email/SMS/push.

**Why High Priority:** CritterSupply sagas fire every event Correspondence needs (`OrderPlaced`, `ShipmentDispatched`, `RefundCompleted`), but customers receive zero communication unless watching the Blazor storefront. 40-50% email open rates = immediate UX impact.

**Renamed (Cycle 28):** Originally "Notifications" BC; renamed to "Correspondence" to avoid ambiguity with real-time UI updates (handled by Customer Experience BC via SignalR). See [ADR 0030](docs/decisions/0030-notifications-to-correspondence-rename.md).

### Integration Contract

| Receives (10 events from 4 BCs) | Publishes |
|----------|-----------|
| `OrderPlaced`, `OrderCancelled` (Orders) | `CorrespondenceQueued` |
| `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed` (Fulfillment) | `CorrespondenceDelivered` |
| `RefundCompleted` (Payments) | `CorrespondenceFailed` |
| `ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnExpired` (Returns) | |

### Phased Rollout

- **Phase 1 (Cycle 28):** 4 events (Orders, Payments) + email (SendGrid)
- **Phase 2:** 6 Returns events + SMS (Twilio)
- **Phase 3:** Push notifications (FCM) + observability

### Core Patterns
- **Idempotency:** Wolverine `MessageId` storage prevents duplicate sends
- **Retry:** 3 attempts, exponential backoff (5min, 30min, 2hr)
- **Preferences:** Query Customer Identity BC (Polly circuit breaker)
- **Channels:** `ICorrespondenceChannel` interface (email/SMS/push)

### References
- **Planning:** `docs/planning/correspondence-event-model.md` (event model), `correspondence-risk-analysis-roadmap.md` (risk matrix)
- **ADRs:** [ADR 0030](docs/decisions/0030-notifications-to-correspondence-rename.md)

---

## Promotions

**Status:** 🔴 **High Priority** — Requires Pricing BC live first
**Purpose:** Coupon codes, percentage discounts, BOGO deals, free shipping thresholds.

**Why High Priority:** Shopping BC already defines future events (`CouponApplied`, `PromotionApplied`) — Promotions BC gives them authority. Direct revenue impact.

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `CouponApplied` (Shopping) | `PromotionActivated` → Shopping |
| `CheckoutInitiated` (Shopping) | `PromotionExpired` → Shopping |
| Commands (Admin Portal) | `CouponValidated/Rejected` → Shopping |
| | `DiscountCalculated` → Shopping |

### Core Invariants
- Coupon redeemed once per customer (unless multi-use)
- Discount cannot reduce price below MAP floor (queries Pricing BC)
- Stacking rules enforced (some promotions mutually exclusive)
- Usage limits enforced atomically (race condition risk at high traffic)

### What it doesn't own
- Base prices (Pricing BC)
- Campaign delivery (future Marketing BC)
- Payment processing (Payments BC)

---

## Search

**Status:** 🟡 **Medium Priority**
**Purpose:** Product search, filtering, faceted navigation, autocomplete.

**Technology Options:**
- **Postgres Full-Text Search** (lightweight, Phase 1)
- **Elasticsearch** (recommended for production scale)
- **Meilisearch** (simpler alternative to ES)

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `ProductAdded` (Catalog) | _(none — read-only BC)_ |
| `ProductUpdated` (Catalog) | |
| `PricePublished` (Pricing) | |

**Pattern:** Projection from Catalog + Pricing events → search index. Customer Experience BC queries Search BC for product listings.

---

## Recommendations

**Status:** 🟡 **Medium Priority** — Requires Analytics BC first
**Purpose:** Personalized product suggestions — "Customers who bought X also bought Y", recently viewed, trending.

**Algorithms:**
- **Phase 1:** Collaborative filtering (cart co-occurrence)
- **Phase 2:** ML.NET models trained on order history (Analytics BC data)

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `OrderPlaced` (Orders) | _(none — read-only BC)_ |
| `ItemAdded` (Shopping) | |

**Pattern:** Projection from orders/carts → recommendation model. Customer Experience BC queries for suggestions.

---

## Store Credit

**Status:** 🟡 **Medium Priority**
**Purpose:** Issue/redeem credits, gift cards, refund-to-credit option.

### Integration Contract

| Receives | Publishes |
|----------|-----------|
| `RefundRequested` (Orders) | `CreditIssued` → Customer Experience |
| Commands (Admin Portal) | `CreditRedeemed` → Orders, Payments |

**Pattern:** Event-sourced `CreditAccount` aggregate per customer. Redeemed credits reduce order total at checkout.

---

## Analytics

**Status:** 🟢 **Low Priority**
**Docs:** `docs/research/event-sourcing-analytics-ml-opportunities.md`

**Purpose:** Business intelligence — dashboards, reports, ML model training.

**Technology Stack:**
- **Phase 1:** Marten projections → Polyglot Notebooks (C# REPL for exploratory analysis)
- **Phase 2:** Debezium CDC → Kafka → data lake (Parquet)
- **Phase 3:** ML.NET models (churn prediction, inventory forecasting)

**Patterns:**
- **Redis caching** for ML inference features (1-3ms vs 15-55ms PostgreSQL)
- **Dual-publish:** RabbitMQ (operational) + Kafka (analytics)

### References
- **Research:** `docs/research/event-sourcing-analytics-ml-opportunities.md`

---

## Admin Portal

**Status:** 🟢 **Low Priority**
**Docs:** `docs/planning/admin-portal-event-modeling.md`, `admin-portal-ux-research.md`

**Purpose:** Internal tooling for operations, customer service, merchandising.

**Roles:** `Executive`, `OperationsManager`, `CustomerService`, `CopyWriter`, `PricingManager`, `WarehouseClerk`

**Architecture:** BFF pattern, Next.js SSR (recommended), SignalR for real-time alerts, RBAC via JWT.

### Integration Pattern

Admin Portal.Api composes views from multiple BCs (fan-out queries), routes commands to domain BCs, relays integration events to SignalR groups.

### References
- **Planning:** `docs/planning/admin-portal-event-modeling.md`, `admin-portal-ux-research.md`, `admin-portal-research-discovery.md`

---

## Operations Dashboard

**Status:** 🟢 **Low Priority**
**Purpose:** DevEx/SRE tooling — event stream visualization, saga explorer, DLQ management.

**Technology:** React (Next.js), SignalR, OpenTelemetry integration.

**Features:**
- Live event stream monitor (color-coded by result)
- Saga state machine visualization
- Dead Letter Queue dashboard (view, replay)
- Handler performance metrics (P50/P95/P99 latency)

**Audience:** On-call engineers, new engineers learning the system.

---

## Future Considerations (Long-Term)

These BCs are identified but not prioritized for near-term roadmap:

- **Reviews** — Product reviews, ratings (requires Catalog BC)
- **Procurement / Supply Chain** — Purchase orders, supplier management
- **Shipping / Logistics** — Carrier rate shopping, label generation (currently stubbed in Fulfillment BC)

---

## Appendix A: Returns BC Event Model (Detailed)

**Note:** This appendix provides detailed Returns BC lifecycle for implementation reference. See main Returns section above for integration contract.

### Return Aggregate Lifecycle

```
Return Stream States:
  Requested → Approved/Denied (terminal if denied)
           → Approved → Shipped → Inspecting → (Inspection outcome)
                                              → Passed → Completed (refund)
                                              → Failed → Rejected (terminal)
           → Expired (terminal, no action taken)
```

### Events (Full List)

**Refund Flow:**
1. `ReturnInitiated(OrderId, ReturnType.Refund, Reason, Items[])`
2. `ReturnApproved(ReturnId, ReturnLabel)` — publishes to Orders, Fulfillment
3. `InspectionStarted(InspectorId, ReceivedDate)` — triggered by `ReturnReceived` from Fulfillment
4. `InspectionPassed(InspectorId, Notes)` OR `InspectionFailed(FailureReason)`
5. `RefundRequested(RefundAmount)` — publishes to Payments
6. `ReturnCompleted(RefundId)` — terminal state, publishes to Orders

**Exchange Flow:**
1. `ReturnInitiated(OrderId, ReturnType.Exchange, ReplacementSku, Reason)`
2. `ReturnApproved(ReturnId, ReturnLabel)`
3. `InspectionStarted`, `InspectionPassed` (same as refund)
4. `ExchangeApproved(ReplacementSku, ShippingMethod)` — manual approval command
5. `ReplacementShipped(TrackingNumber)` — publishes to Orders
6. `ReturnCompleted(ExchangeId)` — terminal state

**Denial/Expiry:**
- `ReturnDenied(Reason)` — terminal, publishes to Orders
- `ReturnExpired()` — terminal, eligibility window closed

### Exchange Invariants (Phase 1)

- Replacement price must be ≤ original item price (no upcharge)
- Same SKU only (cross-SKU exchanges Phase 2+)
- Inspection failure → complete rejection (no partial exchange)
- Price difference → automatic refund to customer

### Integration Messages (8 total)

**Published by Returns BC:**
1. `ReturnApproved` → Orders (update saga), Fulfillment (expect return shipment)
2. `ReturnDenied` → Orders
3. `ReturnCompleted` → Orders, Payments (refund confirmation)
4. `RefundRequested` → Payments
5. `ReturnExpired` → Orders

**Received by Returns BC:**
1. `ShipmentDelivered` (Fulfillment) — start eligibility window
2. `ReturnReceived` (Fulfillment) — trigger inspection
3. `RefundCompleted` (Payments) — close return

### Command Handlers (Phase 3 — Exchange Workflow)

- `ApproveExchangeHandler` — manual approval after inspection
- `DenyExchangeHandler` — reject exchange (inspection failed or policy violation)
- `ShipReplacementItemHandler` — dispatch replacement (integrates with Fulfillment BC)

### References
- **Implementation:** `src/Returns/Returns/Returns/Return.cs`, `ReturnEvents.cs`, `ApproveExchangeHandler.cs`
- **Tests:** `tests/Returns/Returns.UnitTests/ExchangeWorkflowTests.cs`, `tests/Returns/Returns.Api.IntegrationTests/ExchangeWorkflowEndpointTests.cs`
- **Cycles:** [cycle-27-returns-bc-phase-3-retrospective.md](docs/planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md)

---

## Appendix B: Cross-Context Integration Patterns

### Orchestration vs. Choreography

**When to use Orchestration (Saga):**
- Multi-step workflows with complex compensation logic (Order saga)
- Central coordinator needed to enforce business rules
- Strong consistency requirements (payment + inventory + fulfillment)

**When to use Choreography (Event-driven):**
- Loosely coupled workflows (Correspondence BC reacting to events)
- No central coordinator needed
- Eventual consistency acceptable

### Message Naming Conventions

- **Commands:** Imperative (e.g., `PlaceOrder`, `CapturePayment`)
- **Events:** Past tense (e.g., `OrderPlaced`, `PaymentCaptured`)
- **Integration Messages:** Published to RabbitMQ, handled by multiple BCs

### Idempotency Patterns

- **Wolverine MessageId:** Handler stores `MessageId` to prevent duplicate processing
- **Natural Keys:** Use deterministic IDs (UUID v5 for cart/checkout, [ADR 0016](docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md))
- **Projections:** Use `$version` for optimistic concurrency

### References
- **Skills:** `wolverine-sagas.md` (orchestration), `wolverine-message-handlers.md` (choreography)
- **ADRs:** [ADR 0016](docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md)

---

## Appendix C: Documentation Quick Reference

### Skills (Implementation Patterns)
- `wolverine-sagas.md` — Saga patterns, decider pattern, lifecycle
- `wolverine-message-handlers.md` — Compound handlers, return patterns
- `wolverine-signalr.md` — SignalR transport, JWT auth, group routing
- `marten-event-sourcing.md` — Aggregate patterns, domain events
- `marten-document-store.md` — CRUD patterns, query filtering
- `efcore-wolverine-integration.md` — EF Core + Wolverine patterns
- `bff-realtime-patterns.md` — BFF composition, SignalR integration
- `blazor-wasm-jwt.md` — WASM + JWT patterns, token refresh
- `external-service-integration.md` — Strategy pattern, graceful degradation
- `critterstack-testing-patterns.md` — Alba integration tests, pure function tests
- `testcontainers-integration-tests.md` — TestContainers setup, fixtures

### ADRs (Architectural Decisions)
- [ADR 0001](docs/decisions/0001-checkout-migration-to-orders.md) — Checkout migration
- [ADR 0002](docs/decisions/0002-ef-core-for-customer-identity.md) — EF Core for Customer Identity
- [ADR 0010](docs/decisions/0010-stripe-payment-gateway-integration.md) — Stripe payment gateway
- [ADR 0012](docs/decisions/0012-simple-session-based-authentication.md) — Session-based auth (Customer)
- [ADR 0013](docs/decisions/0013-signalr-migration-from-sse.md) — SSE → SignalR migration
- [ADR 0016](docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md) — UUID v5 for natural keys
- [ADR 0017](docs/decisions/0017-price-freeze-at-add-to-cart.md) — Price freeze at cart add
- [ADR 0018](docs/decisions/0018-money-value-object-canonical-currency.md) — Money value object
- [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md) — Blazor WASM for Vendor Portal
- [ADR 0028](docs/decisions/0028-jwt-for-vendor-identity.md) — JWT for Vendor Identity
- [ADR 0029](docs/decisions/0029-order-saga-design-decisions.md) — Order saga design
- [ADR 0030](docs/decisions/0030-notifications-to-correspondence-rename.md) — Correspondence BC rename

### Planning Docs (Cycle Plans & Research)
- `docs/planning/CURRENT-CYCLE.md` — Active cycle tracking
- `docs/planning/cycles/` — Cycle retrospectives
- `docs/planning/correspondence-event-model.md` — Correspondence BC event model
- `docs/planning/pricing-event-modeling.md` — Pricing BC event model
- `docs/planning/fulfillment-evolution-plan.md` — Multi-warehouse roadmap
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md` — Catalog evolution (Cycles 29-35)
- `docs/planning/admin-portal-event-modeling.md` — Admin Portal planning
- `docs/research/event-sourcing-analytics-ml-opportunities.md` — Analytics roadmap

---

**Document Status:** Living document, updated 2026-03-13 (Cycle 27 complete, Cycle 28 planning).
**Next Update:** After Correspondence BC implementation (Cycle 28).
