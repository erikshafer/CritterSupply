# S4 Workflow Inventory (scratch — delete before retro commit)

Source: cross-BC edges enumerated from all 18 S2-full dossiers under `docs/extraction/bcs/`. Clustered by business outcome.

## Final workflow list (15 files)

1. `cart-to-checkout.md` — Shopping → Orders (1 integration event + 1 SignalR fan-out). Choreography.
2. `coupon-and-discount-application.md` — Shopping → Pricing [HTTP] + Shopping → Promotions [HTTP]. Synchronous HTTP only.
3. `coupon-redemption-recording.md` — Shopping → Promotions in-process choreography. ADR 0058.
4. `recall-cascade.md` — Product Catalog → Listings (force-down across non-terminal listings) → `ListingsCascadeCompleted`. Choreography. Declared-vs-implemented (CONTEXTS.md scope mismatch).
5. `marketplace-listing-submission.md` — Listings → Marketplaces (adapter set, 3 production + 3 stub). Hybrid (RabbitMQ + per-channel adapter HTTP). Variants per channel.
6. `order-saga.md` — Orders ↔ Payments ↔ Inventory ↔ Fulfillment ↔ Returns. Orchestration. 16 states. Variants: fraud review, address change, cross-product exchange acknowledgement.
7. `standard-return-refund.md` — Returns ↔ Orders ↔ Payments ↔ Fulfillment ↔ Inventory. Hybrid (state machine + choreography).
8. `cross-product-exchange.md` — Returns orchestrates: Inventory replacement + Payments delta capture / partial refund + CX SignalR push. Orchestration. ADRs 0061, 0062. Variants: capture-failure compensation, inspection rejection.
9. `vendor-onboarding.md` — Vendor Identity → Vendor Portal. Declared-vs-implemented: `VendorUserActivated` route-without-instantiator.
10. `vendor-change-request.md` — Vendor Portal single-BC-internal in code; 3 outbound + 7 inbound contracts declared without producers/consumers in any other BC. Declared-not-wired.
11. `backoffice-fan-in-dashboards.md` — 24 inbound events × 6 projections from 7 BCs.
12. `backoffice-customer-service.md` — Backoffice → Orders / Returns / Customer Identity / Correspondence. Declared-vs-implemented: RBAC policy/claim mismatches.
13. `backoffice-operations-health.md` — M46.0 dead-letter summary endpoint, single endpoint, cross-schema query.
14. `transactional-communication.md` — Correspondence ← 12 inbound × 4 BCs; outbound 3 contracts × 2 queues = 6 routes. Provider abstractions stub-only.
15. `storefront-real-time-updates.md` — Customer Experience subscribes to 23 inbound events × 8 upstream BCs; pushes 5 SignalR message types.

## Cross-BC edge summary

### RabbitMQ publish→subscribe map (consolidated)

**Shopping →**
- `Shopping.CheckoutInitiated` → Orders (queue `orders-checkout-initiated`)
- `Shopping.{ItemAdded,ItemRemoved,ItemQuantityChanged,CouponApplied,CouponRemoved}` → Customer Experience (queue `storefront-notifications`)

**Orders →**
- `Orders.OrderPlaced` → Payments + Inventory (workflow init), Customer Experience + Correspondence (notify), Backoffice (queue `storefront-notifications`)
- `Orders.OrderCancelled` → Inventory, Fulfillment, Customer Experience, Backoffice
- `Orders.ShippingAddressChanged` → Fulfillment (`fulfillment-requests`), Customer Experience (`storefront-notifications`)
- `Orders.OrderPutOnHold` / `OrderReleasedFromHold` / `OrderRejectedForFraud` → Customer Experience, Backoffice
- `Orders.ReservationCommitRequested` / `ReservationReleaseRequested` → Inventory
- `Fulfillment.FulfillmentRequested` (published from Orders) → Fulfillment
- `Payments.RefundRequested` (published from Orders) → Payments

**Payments →** PaymentAuthorized / PaymentCaptured / PaymentFailed / RefundCompleted / RefundFailed → Orders, Customer Experience, Backoffice. ExchangeAdditionalPaymentCaptured / ExchangePartialRefundIssued / ExchangeDeltaCaptureFailed → Returns + Customer Experience (CX SignalR).

**Inventory →** ReservationConfirmed / ReservationFailed / ReservationCommitted / ReservationReleased → Orders, Customer Experience, Vendor Portal (StockReplenished etc), Backoffice. ReplacementReserved / ReplacementReservationFailed → Returns.

**Fulfillment →** ShipmentHandedToCarrier / TrackingNumberAssigned / ShipmentDelivered / ReturnToSenderInitiated / ReshipmentCreated / BackorderCreated / FulfillmentCancelled / OrderSplitIntoShipments → Orders, Customer Experience, Correspondence, Backoffice.

**Returns →** ReturnRequested / ReturnCompleted / ReturnDenied / ReturnRejected / ReturnExpired / ReturnApproved / ReturnInspected / ExchangeCancelled etc → Orders, Customer Experience, Inventory, Payments, Correspondence, Backoffice. ReserveReplacementForExchange / CaptureExchangeDelta / IssueExchangePartialRefund → Inventory + Payments (cross-product exchange orchestration).

**Product Catalog →** ProductAdded / ProductUpdated / ProductDiscontinued / ProductImageReplaced / ProductDeleted → Listings, Pricing, Backoffice, Vendor Portal.

**Listings →** ListingApproved / ListingForcedDown / ListingsCascadeCompleted / ListingActivated / ListingPaused / ListingDelisted → Marketplaces, Vendor Portal, Backoffice.

**Marketplaces →** MarketplaceListingActivated / MarketplaceSubmissionRejected → Listings, Vendor Portal. Outbound to external adapters (eBay, Amazon, Walmart) over HTTP.

**Vendor Identity →** 11 lifecycle events (VendorTenantCreated/Suspended/Terminated, VendorUserInvited, etc.) → Vendor Portal. `VendorUserActivated` declared / not emitted.

**Vendor Portal →** 3 change-request contracts declared / no external producer/consumer. Internal choreography only.

**Correspondence →** CorrespondenceQueued / CorrespondenceDelivered / CorrespondenceFailed → Backoffice (×2 queues each = 6 publish routes).

**Customer Identity →** 0 outbound integration contracts.
**Backoffice Identity →** 0 outbound integration contracts.
**Pricing →** 0 wired outbound (3 declared / 0 wired).
**Promotions →** 0 outbound contracts.

### Synchronous HTTP edges (cross-BC)

- Shopping → Pricing: `IPricingClient.GetPriceAsync` (`GET /api/pricing/products?skus=...`)
- Shopping → Promotions: `IPromotionsClient.ValidateCouponAsync` (`GET /api/promotions/coupons/{code}/validate`), `.CalculateDiscountAsync` (`POST /api/promotions/discounts/calculate`)
- Customer Experience BFF → Shopping / Orders / Fulfillment / Payments / Product Catalog / Customer Identity / Inventory (queries / proxies)
- Backoffice → Orders / Returns / Customer Identity / Correspondence (queries / proxies)

### In-process Wolverine choreography (cross-feature)

- Shopping `RedeemCouponHandler` returns `CouponRedeemed` in `OutgoingMessages` → Promotions `RecordPromotionRedemptionHandler` (ADR 0058)

### SignalR pushes (Customer Experience)

- `customer:{customerId}` group: CartUpdated, OrderStatusChanged, ShipmentStatusChanged, ReturnStatusChanged, ReturnExchangePaymentChanged
- Backoffice hub `/hub/backoffice`: 5 message types (2 emitted, 3 declared-only)
- Vendor Portal hub `/hub/vendor-portal`: 6 message types (4 tenant-scoped, 2 user-scoped)
