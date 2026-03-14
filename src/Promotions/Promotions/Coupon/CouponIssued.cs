namespace Promotions.Coupon;

/// <summary>
/// Domain event: A new coupon was issued for a promotion.
/// </summary>
public sealed record CouponIssued(
    string CouponCode,
    Guid PromotionId,
    DateTimeOffset IssuedAt);
