# Promotions

> **Source folder:** `src/Promotions/`
> **Status:** Implemented
> **Most recent material milestone:** M30.1 — Shopping ↔ Promotions coupon integration (cart-time validation + discount calculation)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Promotions owns promotional campaigns and coupon codes. A campaign (the `Promotion` aggregate) is created, activated, paused, resumed, expired, or cancelled; from a campaign one or many coupons (the `Coupon` aggregate) are issued — singly or as a batch — and then validated, redeemed, expired, or revoked. Promotions exposes synchronous validation and discount-calculation HTTP endpoints that Shopping calls at cart time, and records redemption when an order is placed.

## Top-level structure

### Aggregates

- `Promotion` — stream ID: UUID v7 (Marten DCB tag type `PromotionStreamId`).
- `Coupon` — stream ID: UUID v5 derived from the coupon code (Marten DCB tag type `CouponStreamId`).

### Commands

- Promotion: `CreatePromotion`, `ActivatePromotion`, `RecordPromotionRedemption`, `GenerateCouponBatch`
- Coupon: `IssueCoupon`, `RedeemCoupon`, `RevokeCoupon`
- Discount: `CalculateDiscount`

### Domain events

- Promotion: `PromotionCreated`, `PromotionActivated`, `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `PromotionRedemptionRecorded`, `CouponBatchGenerated`
- Coupon: `CouponIssued`, `CouponRedeemed`, `CouponExpired`, `CouponRevoked`

### Projections

- `Promotion` snapshot — inline, keyed by stream id.
- `Coupon` snapshot — inline, keyed by stream id.
- `CouponLookupView` — inline, keyed by coupon code (O(1) validation lookup).

### Integration events

- Subscribes to `Orders.OrderPlaced` (`Promotions/OrderIntegration/OrderPlacedHandler.cs`) to record redemption
- HTTP request/response with Shopping (`ValidateCoupon`, `CalculateDiscount`) — synchronous, not via RabbitMQ

### HTTP / API surface (one line)

`Promotions.Api` exposes promotion + coupon administration commands plus the `ValidateCoupon` and `CalculateDiscount` query endpoints consumed by Shopping.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

Not applicable — no `JwtBearer` registration in `Promotions.Api/Program.cs` at S1 stub depth.

## Prior event modeling

- `docs/planning/promotions-event-modeling.md`

## ADRs

- ADR 0058 — Dynamic Consistency Boundary: Promotions Coupon Redemption

## Source citations (S1 stub)

- `src/Promotions/`
- `src/Promotions/Promotions.Api/Program.cs` (projection registrations, DCB tag types)
- `CONTEXTS.md` (section: `Promotions`)
- `docs/decisions/0058-dcb-promotions-coupon-redemption.md`
- `docs/planning/promotions-event-modeling.md`
