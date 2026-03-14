namespace Promotions.Coupon;

/// <summary>
/// Command to issue a new coupon for a promotion.
/// Phase 1: Admin manually issues individual coupons.
/// Phase 2+: Batch generation for bulk coupon creation.
/// </summary>
public sealed record IssueCoupon(
    string CouponCode,
    Guid PromotionId);
