namespace Promotions.Coupon;

/// <summary>
/// Command to redeem a coupon for an order.
/// This is called when an order is placed with a coupon applied.
/// Phase 1: Single-use coupons only.
/// </summary>
public sealed record RedeemCoupon(
    string CouponCode,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset RedeemedAt);
