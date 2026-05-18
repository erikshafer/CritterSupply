# CritterSupply — Business Architecture (Synthesis Brief)

> **Source artifacts:** 18 BC dossiers under `docs/extraction/bcs/`; 15 cross-BC workflow traces under `docs/extraction/workflows/`; structural observations at `docs/extraction/observations.md`. This document re-presents that material as a unified picture; it introduces no new material.
> **Milestone:** [M48.0](../planning/milestones/m48-0-plan.md), Session 6.

---

## 1. What CritterSupply is

CritterSupply is an e-commerce platform that combines a direct-to-customer storefront with a vendor-supplied marketplace and a staff back office. Customers browse a catalog, place items in a cart, complete checkout, and have orders picked, shipped, delivered, and — when needed — returned or exchanged. Vendors supply the catalog and inventory; staff operators monitor the system, intervene on individual customer cases, and decide which catalog items are listed on which external marketplace channels.

Three actor categories interact with the platform across three user-facing surfaces. Customers reach the platform through the **storefront** (`bcs/customer-experience.md`). Vendors reach it through the **vendor portal**, a tenant-scoped self-service surface with its own identity issuer (`bcs/vendor-portal.md`, `bcs/vendor-identity.md`). Staff reach it through the **back office**, a role-keyed operator surface with a third identity issuer (`bcs/backoffice.md`, `bcs/backoffice-identity.md`). A commerce core — cart, orders, payments, inventory, fulfillment, and returns — sits behind all three surfaces; channel surfaces (listings + marketplaces) push the catalog outward to external marketplaces; and a transactional-communication surface (correspondence) sends emails and SMS messages on the system's behalf.

Eighteen bounded contexts are implemented and described in this milestone's artifact set (`bcs/`); each one owns a specific business capability and communicates with its neighbours through a small set of integration patterns documented in section 3 below.

---

## 2. Actors and surfaces

### 2.1 Customers (storefront)

A customer interacts with CritterSupply through the Blazor storefront and its backend-for-frontend (`Storefront.Web` + `Storefront.Api`, `bcs/customer-experience.md`). The BFF holds no domain state of its own: it composes views over HTTP from Shopping, Orders, Product Catalog, and Customer Identity, subscribes to integration events from Shopping, Orders, Payments, Inventory, Fulfillment, and Returns over RabbitMQ, and pushes per-customer updates to the browser via SignalR (`bcs/customer-experience.md`, `observations.md` §6, §9). The customer identity surface is cookie-based — Customer Identity issues a sliding 7-day `CritterSupply.Auth` cookie per ADR 0012 (`bcs/customer-identity.md`, `observations.md` §12).

From the storefront, a customer can browse, add items to a cart, apply a coupon, complete checkout, see real-time order progress, and initiate a return — including a cross-product exchange where a different SKU is requested as the replacement (`workflows/cart-to-checkout.md`, `workflows/storefront-real-time-updates.md`, `workflows/standard-return-refund.md`, `workflows/cross-product-exchange.md`).

### 2.2 Vendors (vendor portal)

A vendor interacts with CritterSupply through a Blazor WebAssembly application (`VendorPortal.Web`) backed by a vendor-scoped BFF (`VendorPortal.Api`) (`bcs/vendor-portal.md`). The vendor identity surface is JWT-based — Vendor Identity issues HMAC-SHA256–signed bearer tokens with a paired refresh cookie, scoped by `VendorTenant` and with tenant-scoped role claims (`bcs/vendor-identity.md`, `observations.md` §12, §14). From the portal, a vendor monitors inventory and sales for their tenant, manages their team (invite, role assignment, deactivation), and submits change requests against catalog data they do not directly own (`bcs/vendor-portal.md`, `workflows/vendor-onboarding.md`, `workflows/vendor-change-request.md`). The change-request submission round-trips on the Vendor Portal host's own Wolverine bus in production code today; the cross-BC review side is declared at the contract level but unwired (`workflows/vendor-change-request.md`, `observations.md` §20).

### 2.3 Operators (back office)

Staff operators interact with CritterSupply through a second Blazor WebAssembly application backed by a hybrid BFF (`Backoffice.Api`) (`bcs/backoffice.md`). The operator identity surface is also JWT-based — Backoffice Identity issues HMAC-SHA256 bearer tokens with persisted server-side refresh tokens (a divergence from Vendor Identity, where refresh tokens are not server-persisted) (`bcs/backoffice-identity.md`, `observations.md` §13). Seven operator roles are defined per ADR 0031: `SystemAdmin`, `Executive`, `OperationsManager`, `WarehouseClerk`, `CustomerService`, `PricingManager`, `CopyWriter`. One role per user is the Phase-1 constraint (`bcs/backoffice-identity.md`, `observations.md` §14).

The back office is a hybrid BFF: it composes views and proxies writes to upstream BCs via typed clients, but also owns one event-sourced aggregate of its own — `OrderNote`, the operator-authored note attached to an order, per ADR 0037 (`bcs/backoffice.md`, `observations.md` §1, §15). It maintains five read-model projections (`AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`) fed by 24 inbound integration events across 7 upstream BCs (`bcs/backoffice.md`, `workflows/backoffice-fan-in-dashboards.md`, `observations.md` §9). The operator surface includes a customer-service composition workflow that aggregates customer, order, return, and correspondence data behind a single set of operator endpoints (`workflows/backoffice-customer-service.md`) and an operations-health surface that aggregates Wolverine dead-letter rows across every BC's schema (`workflows/backoffice-operations-health.md`).

### 2.4 System (scheduled jobs, choreographies, fan-out)

A fourth participant — the system itself — drives behaviour without direct user initiation. Scheduled saga messages close out the Order saga's 30-day return window (`workflows/order-saga.md`); upstream integration events drive Correspondence's transactional message fan-out (`workflows/transactional-communication.md`); upstream events drive the Storefront BFF's real-time push to connected customers and the Backoffice BFF's fan-in to operator dashboards (`workflows/storefront-real-time-updates.md`, `workflows/backoffice-fan-in-dashboards.md`); and a recall declared in Product Catalog cascades to Listings, Inventory holds, Order pauses, and Fulfillment interception through autonomous reactions (`workflows/recall-cascade.md`).

---

## 3. The bounded context map

CritterSupply implements 18 bounded contexts, grouped functionally below. Each BC is named with its dossier reference and its archetype per `observations.md` §1 (event-sourced [ES] via Marten; EF Core entity-model; Marten document store [doc-store]; backend-for-frontend [BFF]; or BFF + ES hybrid). Per-BC details — aggregates, commands, events, projections, integration messages, HTTP endpoints, and frontend pages — live in each dossier and are aggregated in `observations.md` §2.

### 3.1 Commerce core (transactional spine)

Six bounded contexts carry the core commercial path from cart through delivery and return.

- **Shopping** [ES] — owns the customer's pre-purchase cart lifecycle; freezes per-item price at add-to-cart time per ADR 0017 (`bcs/shopping.md`).
- **Orders** [ES] — owns two aggregates: a `Checkout` stream capturing pre-placement state and an `Order` saga orchestrating the post-placement journey across Payments, Inventory, Fulfillment, Returns, and Customer Experience (`bcs/orders.md`).
- **Payments** [ES] — owns the `Payment` aggregate and the financial-transaction lifecycle (authorize, capture, refund), including cross-product exchange deltas (`bcs/payments.md`).
- **Inventory** [ES] — owns per-warehouse stock per SKU and the reservation lifecycle backing both order fulfillment and return-driven replacements (`bcs/inventory.md`).
- **Fulfillment** [ES] — owns the physical journey of an order: warehouse routing, pick / pack, the carrier lifecycle, return-receipt at the warehouse, and backorder handling (`bcs/fulfillment.md`). Largest event surface in the system at 55 in-domain events across two aggregates (`observations.md` §2).
- **Returns** [ES] — owns the post-delivery return journey and orchestrates cross-product exchanges through Inventory and Payments per ADRs 0061 and 0062 (`bcs/returns.md`, `workflows/cross-product-exchange.md`).

### 3.2 Customer-facing

Three bounded contexts present the customer experience and the catalog the customer browses.

- **Customer Experience** [BFF] — Blazor storefront + composition BFF; no domain state of its own (`bcs/customer-experience.md`).
- **Customer Identity** [EF Core] — owns the customer record, the saved-address book, and the storefront's cookie-based session (`bcs/customer-identity.md`).
- **Product Catalog** [ES] — owns the master `CatalogProduct` record (name, description, category, images, dimensions, status, tags, vendor) for every SKU; the upstream source for Pricing, Listings, Marketplaces, Storefront, and Backoffice product-management views (`bcs/product-catalog.md`).

### 3.3 Pricing and promotions

Two bounded contexts compute the effective price of a cart.

- **Pricing** [ES] — owns the per-SKU price record, including base, floor, ceiling, scheduled changes, retroactive corrections, and a terminal discontinuation state; exposes a `Money` value object reused across Orders, Payments, Promotions, and Returns (`bcs/pricing.md`, `observations.md` §15).
- **Promotions** [ES] — owns campaigns (`Promotion` aggregate) and coupons (`Coupon` aggregate); exposes synchronous coupon-validation and discount-calculation endpoints that Shopping calls at cart time (`bcs/promotions.md`, `workflows/coupon-and-discount-application.md`).

### 3.4 Channels

Two bounded contexts manage publishing catalog items to external marketplaces.

- **Listings** [ES] — owns the per-SKU per-channel listing state machine (`Draft → ReadyForReview → Submitted → Live → Paused → Ended`, with a recall force-down branch) (`bcs/listings.md`).
- **Marketplaces** [doc-store] — owns channel configuration, the internal-to-channel category map, and the per-channel adapters that authenticate and submit to Amazon SP-API, eBay, and Walmart per ADRs 0052 / 0053 / 0054 (`bcs/marketplaces.md`).

### 3.5 Vendor

Two bounded contexts handle vendor identity and the vendor self-service portal.

- **Vendor Identity** [EF Core] — owns `VendorTenant`, `VendorUser`, and `VendorUserInvitation` lifecycles; issues vendor JWTs and publishes 11 lifecycle integration events consumed by Vendor Portal (`bcs/vendor-identity.md`, `workflows/vendor-onboarding.md`).
- **Vendor Portal** [doc-store] — composes a vendor-scoped read model from upstream events, exposes HTTP + SignalR endpoints over it, and serves the Blazor WASM frontend (`bcs/vendor-portal.md`).

### 3.6 Operator

Two bounded contexts handle staff identity and the back-office surface.

- **Backoffice Identity** [EF Core] — owns `BackofficeUser`, the seven-role RBAC enum per ADR 0031, and the backoffice JWT issuer (`bcs/backoffice-identity.md`).
- **Backoffice** [BFF + ES hybrid] — composes operator views over typed clients to seven upstream BCs, maintains five read-model projections, hosts the operator SignalR hub, and owns one event-sourced aggregate (`OrderNote`) per ADR 0037 (`bcs/backoffice.md`).

### 3.7 Cross-cutting

One bounded context handles transactional messaging across the system.

- **Correspondence** [ES] — subscribes to 12 lifecycle integration events from Orders, Fulfillment, Returns, and Payments; opens a `Message` event stream per inbound trigger; queues an internal `SendMessage` command; calls a provider abstraction; records the delivery outcome; and republishes `CorrespondenceQueued` / `CorrespondenceDelivered` / `CorrespondenceFailed` for downstream observers (`bcs/correspondence.md`, `workflows/transactional-communication.md`).

---

## 4. The customer purchase journey (system spine)

The customer purchase journey is CritterSupply's central workflow and the system's longest cross-BC trace. It begins on the storefront and ends — sometimes weeks later — with the saga closing after the return window expires.

A customer signs in (cookie session, Customer Identity, ADR 0012) and browses the catalog rendered by the Storefront BFF, which composes data from Product Catalog and Pricing (`bcs/customer-experience.md`). Adding an item to the cart calls Shopping, which queries Pricing live to seal the price onto the cart line per ADR 0017 and queries Promotions live if a coupon is applied; the cart is held as a Marten event stream (`workflows/cart-to-checkout.md`, `workflows/coupon-and-discount-application.md`). When the customer completes checkout, Orders runs the `Checkout` aggregate to a terminal state and republishes `Shopping.CartCheckoutCompleted` for `PlaceOrderHandler`, starting the **Order saga** (`bcs/orders.md`, `workflows/order-saga.md`).

The Order saga is the canonical implementation of the Decider pattern per ADR 0029 and the system's most-cited orchestration (`observations.md` §10). It drives the order across roughly seven BCs end-to-end: Payments authorizes and captures the payment; Inventory reserves stock per SKU; once payment is captured and all reservations confirmed, Orders fans out reservation-commit requests; on commit, Fulfillment is invited to pick, pack, and hand off to the carrier; carrier lifecycle events (`ShipmentHandedToCarrier`, `TrackingNumberAssigned`, `ShipmentDelivered`) drive the saga forward; and a delayed `ReturnWindowExpired` saga-internal message scheduled 30 days out closes the saga once any in-flight returns conclude (`workflows/order-saga.md`). Throughout, downstream observers react: Customer Experience pushes status updates over SignalR to the customer's browser (`workflows/storefront-real-time-updates.md`); Correspondence subscribes to the same lifecycle events and queues emails (`workflows/transactional-communication.md`); and Backoffice projections aggregate the same events into operator dashboards (`workflows/backoffice-fan-in-dashboards.md`).

If a customer initiates a return within the 30-day window, the **standard return + refund** workflow runs in parallel with the Order saga (`workflows/standard-return-refund.md`). Returns runs the `Return` aggregate's state machine — eligibility check, approval, physical receipt, inspection, refund or exchange — and choreographs Payments (for the refund) and Inventory (for the restock), with the Order saga acknowledging the resulting state transitions but not orchestrating them. If the customer chooses a different SKU as the replacement, the **cross-product exchange** workflow runs on top of the standard return: Returns becomes the orchestrator, reserves replacement stock through Inventory per ADR 0061, captures a price delta (or issues a partial refund) through Payments per ADR 0062, and runs the compensation paths if either fails (`workflows/cross-product-exchange.md`). The Order saga acknowledges each state transition but Returns remains the authoritative state owner (`observations.md` §10).

The coupon-redemption-recording workflow runs alongside the saga: at order placement, Promotions records the coupon redemption on its DCB-protected tagged streams (`workflows/coupon-redemption-recording.md`); this is the one workflow where Marten Dynamic Consistency Boundary is operationally relevant (`observations.md` §4). And throughout the saga's lifetime, transactional emails are queued by Correspondence on each subscribed lifecycle event (`workflows/transactional-communication.md`).

The complete journey — browse → cart → checkout → placement → payment → reservation → commitment → fulfillment → delivery → optional return or exchange → saga closure — touches all six commerce-core BCs plus Customer Experience, Customer Identity, Product Catalog, Pricing, Promotions, Correspondence, and (for back-office observability) Backoffice.

---

## 5. The channel surface

The channel surface — Listings + Marketplaces — pushes CritterSupply's catalog outward to external marketplaces (Amazon US, Walmart US, eBay US) and pulls back listing-status feedback (`bcs/listings.md`, `bcs/marketplaces.md`, `workflows/marketplace-listing-submission.md`).

The two BCs are paired but independent. Listings owns the per-SKU per-channel listing state machine (`Draft → ReadyForReview → Submitted → Live → Paused → Ended`, with a recall force-down branch); Marketplaces owns the channel registry, the internal-to-channel category map, and a per-channel adapter set that performs the external HTTP submission. Each BC independently translates Product Catalog integration events into a local `ProductSummaryView` projection — an anticorruption layer pattern that appears identically in both BCs and isolates each from the upstream message payload per ADR 0050 (`observations.md` §9).

The marketplace adapter set is the system's primary outbound HTTP boundary. Per-marketplace authentication is governed by ADRs 0052 (Amazon SP-API), 0053 (eBay), and 0054 (Walmart). A `Marketplaces:UseRealAdapters` configuration flag gates the production adapter set behind a stub set for development environments (`bcs/marketplaces.md`). The eBay adapter additionally tracks orphaned `create-offer` / `publish-offer` drafts via the `OrphanedEbayDraft` document and a sweep job per ADR 0055; ADR 0056 governs resilience patterns across the adapter set.

A recall declared in Product Catalog cascades through both channel BCs as well as the commerce core: Product Catalog publishes `ProductDiscontinued` with `IsRecall=true`; Listings force-downs every non-terminal listing for the affected SKU; Marketplaces deactivates the corresponding channel listings; Inventory places a hold; and Orders pauses any in-flight saga touching the SKU (`workflows/recall-cascade.md`).

---

## 6. The vendor surface

The vendor surface — Vendor Identity + Vendor Portal — supports vendor self-service for monitoring sales, managing the vendor team, and submitting catalog change requests.

Vendor onboarding begins when an operator invites a vendor tenant from the back office: Vendor Identity creates a `VendorTenant`, issues a `VendorUserInvitation` token, and publishes `VendorTenantCreated` and `VendorUserInvited` to RabbitMQ; Vendor Portal's read-model handlers consume the events and seed the vendor's view of their tenant (`workflows/vendor-onboarding.md`, `bcs/vendor-identity.md`). The activation step — where the invited user would accept the invitation and trigger `VendorUserActivated` — is declared at the contract level but the producer is not implemented in code today; the operational path from invitation to first login is handled by direct JWT issuance through `POST /api/vendor-identity/auth/login` (`workflows/vendor-onboarding.md`, `observations.md` §20).

The vendor change-request workflow is the surface through which a vendor proposes changes to catalog data Product Catalog owns (`workflows/vendor-change-request.md`). The vendor submits a change request from the portal; the submission round-trips on the Vendor Portal host's local Wolverine bus to create the `ChangeRequest` document; the vendor sees the request in the portal as `Pending`. The cross-BC review side — where another BC (intended: Product Catalog) consumes the outbound contract and publishes a decision back — is declared as contract surface but unwired in production code; the 10 contract records (3 outbound submission types + 7 inbound decision types) exist as messaging routes with no counter-side producer or consumer anywhere in `src/` (`workflows/vendor-change-request.md`, `observations.md` §17, §20). This is the largest single instance of the declared-not-wired pattern in the system; the structural pattern itself is described in section 10 below.

Within the vendor BC's own authority — team management, change-request initiation, read-model projection from upstream — the surface is fully operational (`bcs/vendor-portal.md`).

---

## 7. The operator surface

The operator surface — Backoffice Identity + Backoffice — supports staff in monitoring system health, handling individual customer cases, and writing operator notes against orders.

Seven roles are defined per ADR 0031 with one role per user as the Phase-1 constraint (`observations.md` §14). The back office is a hybrid BFF: it composes operator views over typed clients to seven upstream BCs (Customer Identity, Orders, Returns, Correspondence, Payments, Inventory, Fulfillment); it maintains five read-model projections fed by 24 inbound integration events across the same seven BCs; it hosts an operator SignalR hub with five declared message types; and it owns the `OrderNote` event-sourced aggregate — the only event-sourced state that lives in the BFF, per ADR 0037 (`bcs/backoffice.md`, `workflows/backoffice-fan-in-dashboards.md`).

Three operator workflows are documented:

The **customer-service composition** workflow drives the customer-detail view: an operator with the `CustomerService` role looks up a customer by ID and the BFF aggregates customer profile (Customer Identity), order history (Orders), return history (Returns), and correspondence history (Correspondence) into a single screen, all live on each request (`workflows/backoffice-customer-service.md`). Four endpoints in this surface — `GET /api/backoffice/customers/{customerId}`, `GET /api/backoffice/customers/{customerId}/correspondence`, `GET /api/backoffice/customers`, `GET /api/backoffice/orders/search` — are currently inaccessible to `CustomerService`-role operators because of policy-string mismatches between the issuer (kebab-case role claims) and the registry (PascalCase policy strings against the wrong role string) (`observations.md` §21).

The **fan-in dashboards** workflow drives operator dashboards: 24 integration events from seven BCs feed five Marten projections (`AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`) and a SignalR push pipeline lifts changed projection slices to the operator's WASM client in real time (`workflows/backoffice-fan-in-dashboards.md`). Three of the hub's five declared message types are wired end-to-end; two — `ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented` — are declared but have no producer in code (`observations.md` §19).

The **operations health** workflow is a cross-schema read aggregator added in M46.0: a single Operations Manager–scoped endpoint reads every BC's Wolverine dead-letter table and rolls the rows up into a system-wide health view (`workflows/backoffice-operations-health.md`).

---

## 8. Cross-cutting concerns

Four system-wide concerns cut across the BC map: identity, transactional communication, real-time push, and event-sourced persistence.

### 8.1 Identity

Three identity issuers operate in CritterSupply, one per actor category, and they coexist on hosts that need to accept more than one issuer (`observations.md` §12, §13).

- **Customer Identity** issues a cookie-based session (`CritterSupply.Auth`, 7-day sliding) per ADR 0012. The cookie is propagated to the Storefront SignalR hub via a `?customerId=` query string on the connection URL (`bcs/customer-identity.md`, `bcs/customer-experience.md`).
- **Vendor Identity** issues HMAC-SHA256–signed JWT bearer access tokens (`Jwt:SigningKey`, 15-minute lifetime) paired with a 7-day refresh cookie; refresh tokens are **not** persisted server-side and validity is governed by cookie contents (`bcs/vendor-identity.md`).
- **Backoffice Identity** issues HMAC-SHA256–signed JWT bearer access tokens (`Jwt:SecretKey`, 15-minute lifetime) paired with a 7-day refresh; refresh tokens **are** persisted server-side on `BackofficeUser.RefreshToken`, gating validity on a server-side row lookup that supports immediate invalidation on Logout / Reset / Deactivate (`bcs/backoffice-identity.md`). This is the one substantive divergence between the two JWT issuers (`observations.md` §13).

Hosts that need to accept JWTs from more than one issuer register both via the multi-issuer scheme registration pattern documented in ADR 0032 (`bcs/orders.md`, `observations.md` §13). Several hosts (Storefront.Api, Orders.Api) register the schemes as a hosting prerequisite without applying them to any endpoint.

RBAC differs across the three issuers: Backoffice Identity defines a closed seven-role enum per ADR 0031; Vendor Identity uses tenant-scoped roles enforced by per-handler claim-check code with no registered authorization policies in Vendor Portal API; Customer Identity carries no RBAC (`observations.md` §14).

### 8.2 Transactional communication

Correspondence is the system's single hub for transactional customer messaging. It subscribes to 12 lifecycle integration events from Orders, Fulfillment, Returns, and Payments; opens a `Message` event stream per inbound trigger; queues an internal `SendMessage` command; calls an `IEmailProvider` / `ISmsProvider` abstraction; records the delivery outcome on the same stream; and republishes `CorrespondenceQueued` / `CorrespondenceDelivered` / `CorrespondenceFailed` for Backoffice and Customer Experience to observe (`bcs/correspondence.md`, `workflows/transactional-communication.md`).

Production provider integrations (SendGrid, Twilio) are not present in tree: `StubEmailProvider` and `StubSmsProvider` are registered unconditionally; no `IPushProvider` interface exists despite a `PushMessage` record being declared; no feature-flag gates the stub-vs-production split (`observations.md` §22). Retries are scheduled on a `5 min` / `30 min` / `2 hr` cadence.

### 8.3 Real-time push

Three SignalR hubs operate in CritterSupply, one per actor surface (`observations.md` §6, §19).

- **Storefront hub** (Customer Experience) — five typed message channels covering cart, checkout, order, return, and exchange events; all five wired end-to-end (`workflows/storefront-real-time-updates.md`).
- **Backoffice hub** — five declared `IBackofficeWebSocketMessage` types; two wired end-to-end (`LiveMetricUpdated`, `AlertCreated`); three declared with no producer (`ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented`) (`workflows/backoffice-fan-in-dashboards.md`, `observations.md` §19).
- **Vendor Portal hub** — used for vendor-tenant-scoped push; the declared `ForceLogout` message type has no producer or client receive branch (`observations.md` §19).

### 8.4 Event-sourced persistence

Marten is the primary persistence technology; eleven BCs use it as an event store (`observations.md` §1). Three identity BCs use Entity Framework Core entity models; two BCs (Marketplaces, Vendor Portal) use Marten as a document store rather than an event store (`observations.md` §1).

Stream-ID strategy is per-aggregate, not per-BC. UUID v7 (sortable, generated at command time) is the default; UUID v5 (deterministic, SHA-1 hash from a namespace + key) is used where natural keys exist — Pricing's `ProductPrice` (namespace `pricing:{SKU}`, ADR 0016), both Fulfillment aggregates, Product Catalog's `CatalogProduct` per ADR 0042's catalog namespace, and Promotions' DCB-tagged streams (`observations.md` §3).

Projection lifecycle is inline by default. Of the system's roughly thirty projection registrations, three are async — all in Inventory (`AlertFeedView`, `NetworkInventorySummaryView`, `BackorderImpactView`) and operationally-required-async per M42.3 (`observations.md` §5).

Marten Dynamic Consistency Boundary (DCB) is BC-specific and verified per BC against source. Promotions uses DCB end-to-end (tag types, boundary queries, retry policy); no other BC uses DCB. Pricing is the BC that prose elsewhere has implied uses DCB but does not (`observations.md` §4).

---

## 9. Recurring structural patterns

Seven patterns recur across the BC map. Each is described once below and cited to the observations document for the supporting inventory.

### 9.1 Two orchestrators, choreography everywhere else

Only two BCs orchestrate cross-BC behaviour with stateful sagas: Orders (the `Order` saga with 16+ states; the canonical Decider implementation per ADR 0029) and Returns (the `Return` aggregate state machine + cross-product exchange orchestration per ADRs 0061 / 0062) (`observations.md` §10). Every other BC participates by choreography — reacting to upstream events and publishing its own. In the cross-product exchange, where both orchestrators overlap, Returns is the orchestrator and the Order saga is an acknowledger (`workflows/cross-product-exchange.md`).

### 9.2 BFFs as composition + fan-out

Three BCs are composition layers: Customer Experience (pure BFF), Backoffice (BFF + one ES aggregate), and Vendor Portal (read-model + BFF + frontend) (`observations.md` §1, §9). All three compose views by combining live HTTP queries to upstream BCs with subscription-fed local projections. All three also host a SignalR hub for push to their respective frontends. None of them orchestrate cross-BC behaviour with saga state.

### 9.3 Anticorruption layer through local projections

Two BCs maintain independent local projections of upstream catalog data via subscription: Listings and Marketplaces each own a `ProductSummaryView` projection populated from the same Product Catalog integration events, maintained independently in each owning BC (`observations.md` §9). This is the system's canonical ACL pattern, governed by ADR 0050.

### 9.4 Two stream-ID strategies

UUID v7 is the default; UUID v5 is used where a natural key (a SKU, a return ID, a promotion code) makes a deterministic stream identifier appropriate (`observations.md` §3). The pattern is per-aggregate; an S1 stub claim does not bind, and the dossier per aggregate is authoritative (`observations.md` §3, §4).

### 9.5 Snapshot vs live read at BC boundaries

Cross-BC reads divide into two patterns (`observations.md` §16). Data is **snapshotted at boundary** when the consumer owns durable state that outlives subsequent edits in the source: Orders requests `GetAddressSnapshot` from Customer Identity at checkout completion per ADR 0002, and the address as of order time is preserved on the `Order` even if the customer subsequently edits the source `CustomerAddress`. Shopping freezes `UnitPrice` on cart items at add-time per ADR 0017. Data is **fetched live** when the consumer is a BFF read view that reflects current state on each request: Customer Experience composes cart, checkout, order, and customer-history views on every request; Backoffice composes its customer-service surface live on every operator action; Shopping fetches the authoritative price from Pricing each time the cart is mutated.

### 9.6 Inline projections by default

Projections register as `Inline` across the codebase; async registrations are concentrated in Inventory's operationally-required-async surface per M42.3 (`observations.md` §5).

### 9.7 DCB is BC-specific, not BC-wide

Marten DCB is used end-to-end in one BC (Promotions) and prose elsewhere has implied without code support that DCB is in use in others (Pricing) (`observations.md` §4). The pattern is: DCB-or-not must be verified per BC against source.

---

## 10. Where declaration meets implementation

A recurring shape across the system is **declared but not wired**: a contract, an event, a hub message, an authorization policy, or a provider interface exists in code but no production code path produces or consumes it. The pattern is described here as a structural fact about CritterSupply at this point in time; the inventory itself lives in `observations.md` Parts V and VI.

The pattern recurs at five scales (`observations.md` §17–§22):

- **Domain events declared on aggregates with `Apply` branches but no production emitter.** This appears across Pricing, Promotions, Correspondence, Returns (terminal-state enum values), and Shopping. The recurring shape is that an aggregate declares its future-state event surface ahead of the emitter being wired (`observations.md` §18).
- **Integration contracts declared with routing but no producer or consumer in any other BC.** The largest single concentration is the vendor change-request workflow (10 declared-not-wired routes); the system-wide count exceeds 25 (`observations.md` §17, §20).
- **SignalR hub message types declared without an instantiator.** Three on the Backoffice hub; one on the Vendor Portal hub; none on the Storefront hub (`observations.md` §19).
- **Authorization policies declared with role-string mismatches.** Four customer-service endpoints on the Backoffice surface that name the `CustomerService` policy resolve to a role string Backoffice Identity does not emit, blocking the intended access (`observations.md` §21).
- **Provider abstractions registered as stubs only.** Correspondence's email, SMS, and push provider abstractions are stub-only with no feature-flag split (`observations.md` §22).

The pattern is concentrated, not uniform: Pricing, Promotions, Vendor Portal, Backoffice, and Correspondence carry the bulk of the inventory; the commerce-core BCs (Orders, Payments, Inventory, Fulfillment) are largely wired (`observations.md` §17, §18). The shape is consistent across categories: a contract or event exists ahead of the emitter or consumer being implemented.

---

## 11. Documentation state

Three documentation surfaces show drift against the implementation: `CONTEXTS.md`, prior event-model artifacts, and a small number of API `README.md` narrative diagrams.

`CONTEXTS.md` drift consolidates to 12+ items across four categories per `observations.md` §23: direction inversion (a publish listed as a subscribe or vice versa), omission (an edge or event present in code that the document does not mention), stale narrative (a description that reflects a previous implementation), and misclassification (a relationship of one kind described as another). Specific instances are enumerated in `observations.md` §23.

Event-model artifacts in `docs/planning/` diverge from current code in several BCs (`observations.md` §24): Backoffice's revised event model has multiple items that resolved differently in code (notably `OrderNote` storage per ADR 0037); Vendor Portal and Backoffice Identity prescribed Argon2id for password hashing, code uses PBKDF2; Fulfillment's prior 49-event target predates the M41.0 successor-rename + SLA / hazmat / claims expansion that brought the current count to 55.

API README narrative drift appears in `Fulfillment.Api/README.md` (still references the retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events superseded by `ShipmentHandedToCarrier` / `ReturnToSenderInitiated` per M41.0) and `Shopping.Api/README.md` (documents an unimplemented background-job emitter for `CartAbandoned`) (`observations.md` §25).

ADR coverage is uneven across BCs. Fifteen cross-BC ADRs govern relationships spanning two or more BCs; Marketplaces is the densest BC at 8+ ADRs (per-marketplace authentication, governance, orphaned-draft sweep, resilience); Returns has cross-product exchange ADRs (0061 / 0062); Orders carries the Decider pattern (0029) and address-snapshot (0002) ADRs; Pricing, Promotions, and the Product Catalog / Listings / Marketplaces axis each have one or more (`observations.md` §26, §27).

---

## 12. Reader's guide to the source artifacts

This synthesis is a re-presentation. Readers seeking depth follow the citations into the load-bearing artifacts below.

### 12.1 The 18 BC dossiers

Under `docs/extraction/bcs/`. Each dossier documents one BC at full depth: purpose, aggregates, commands, domain events, projections, integration events (in / out), HTTP / API surface, frontend surface, identity / auth posture, prior event modeling, ADRs, tests as behavioural evidence, drift notes, and source citations at line granularity.

- Commerce core: [`shopping.md`](./bcs/shopping.md), [`orders.md`](./bcs/orders.md), [`payments.md`](./bcs/payments.md), [`inventory.md`](./bcs/inventory.md), [`fulfillment.md`](./bcs/fulfillment.md), [`returns.md`](./bcs/returns.md)
- Customer-facing: [`customer-experience.md`](./bcs/customer-experience.md), [`customer-identity.md`](./bcs/customer-identity.md), [`product-catalog.md`](./bcs/product-catalog.md)
- Pricing and promotions: [`pricing.md`](./bcs/pricing.md), [`promotions.md`](./bcs/promotions.md)
- Channels: [`listings.md`](./bcs/listings.md), [`marketplaces.md`](./bcs/marketplaces.md)
- Vendor: [`vendor-identity.md`](./bcs/vendor-identity.md), [`vendor-portal.md`](./bcs/vendor-portal.md)
- Operator: [`backoffice-identity.md`](./bcs/backoffice-identity.md), [`backoffice.md`](./bcs/backoffice.md)
- Cross-cutting: [`correspondence.md`](./bcs/correspondence.md)

### 12.2 The 15 workflow traces

Under `docs/extraction/workflows/`. Each trace documents one cross-BC workflow: status, type, initiating actor, BCs involved, most recent material milestone, purpose, actors and triggers, step-by-step trace, projections, compensation paths, variants, BC roles, tests, ADRs, declared-vs-implemented gaps, and source citations.

- Customer purchase & promotions: [`cart-to-checkout.md`](./workflows/cart-to-checkout.md), [`coupon-and-discount-application.md`](./workflows/coupon-and-discount-application.md), [`coupon-redemption-recording.md`](./workflows/coupon-redemption-recording.md)
- Order fulfillment: [`order-saga.md`](./workflows/order-saga.md), [`recall-cascade.md`](./workflows/recall-cascade.md)
- Returns: [`standard-return-refund.md`](./workflows/standard-return-refund.md), [`cross-product-exchange.md`](./workflows/cross-product-exchange.md)
- Vendor: [`vendor-onboarding.md`](./workflows/vendor-onboarding.md), [`vendor-change-request.md`](./workflows/vendor-change-request.md)
- Marketplace & channel: [`marketplace-listing-submission.md`](./workflows/marketplace-listing-submission.md)
- Operator: [`backoffice-fan-in-dashboards.md`](./workflows/backoffice-fan-in-dashboards.md), [`backoffice-customer-service.md`](./workflows/backoffice-customer-service.md), [`backoffice-operations-health.md`](./workflows/backoffice-operations-health.md)
- Cross-cutting: [`transactional-communication.md`](./workflows/transactional-communication.md), [`storefront-real-time-updates.md`](./workflows/storefront-real-time-updates.md)

### 12.3 Structural observations

[`observations.md`](./observations.md) organizes 30 numbered sections into 8 parts: System composition (Part I, §1–5); Integration topology (Part II, §6–11); Identity and authorization (Part III, §12–14); Shared concepts (Part IV, §15–16); Declared vs implemented patterns (Part V, §17–22); Documentation drift (Part VI, §23–25); ADR coverage (Part VII, §26–27); Test coverage patterns (Part VIII, §28–30). Sections cited in this synthesis: §1 (archetypes), §2 (per-BC counts), §3 (stream IDs), §4 (DCB), §5 (projection lifecycle), §6 (transport split), §9 (ACL patterns), §10 (saga topology), §12–14 (identity), §15–16 (shared concepts and read patterns), §17–22 (declared-vs-implemented), §23–25 (documentation drift), §26–27 (ADRs).

### 12.4 Cross-BC ADRs cited

- ADR 0002 — Address snapshot at checkout (Customer Identity, Orders)
- ADR 0012 — Storefront cookie authentication (Customer Identity, Customer Experience)
- ADR 0016 — Pricing UUID v5 stream ID
- ADR 0017 — Cart price-freeze at add-to-cart
- ADR 0029 — Decider pattern (canonical: Order saga)
- ADR 0031 — Backoffice Phase-1 one-role-per-user
- ADR 0032 — Multi-issuer JWT registration
- ADR 0037 — `OrderNote` as event-sourced aggregate in Backoffice
- ADR 0042 — Catalog UUID v5 namespace
- ADR 0050 — Marketplaces ACL governance
- ADR 0052 / 0053 / 0054 — Per-marketplace authentication (Amazon / eBay / Walmart)
- ADR 0055 — Orphaned eBay draft sweep / status polling
- ADR 0056 — Marketplaces resilience
- ADR 0058 — DCB on Promotions coupon redemption
- ADR 0061 — Cross-product exchange replacement reservation
- ADR 0062 — Cross-product exchange Payments choreography

Full ADR set lives under `docs/decisions/`; per-BC ADR concentration is summarized in `observations.md` §27.

---

*End of synthesis brief. The 18 BC dossiers, 15 workflow traces, and `observations.md` are the load-bearing source artifacts; this document re-presents them as a unified picture without introducing new material.*
