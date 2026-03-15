namespace Promotions.Promotion;

/// <summary>
/// Domain event: A promotion was redeemed (coupon used in an order).
/// Used for tracking redemption counts and enforcing usage limits.
/// Phase 1: Simple counter increment.
/// </summary>
public sealed record PromotionRedemptionRecorded(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset RedeemedAt);
