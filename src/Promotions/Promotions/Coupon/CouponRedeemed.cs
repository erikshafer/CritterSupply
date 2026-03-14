namespace Promotions.Coupon;

/// <summary>
/// Domain event: A coupon was redeemed in an order.
/// </summary>
public sealed record CouponRedeemed(
    string CouponCode,
    Guid OrderId,
    DateTimeOffset RedeemedAt);
