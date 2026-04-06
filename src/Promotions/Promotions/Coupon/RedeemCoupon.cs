namespace Promotions.Coupon;

/// <summary>
/// Command to redeem a coupon for an order.
/// This is called when an order is placed with a coupon applied.
/// Phase 1: Single-use coupons only.
/// M40.0: PromotionId added to support DCB boundary query spanning Coupon + Promotion streams.
/// </summary>
public sealed record RedeemCoupon(
    string CouponCode,
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset RedeemedAt);
