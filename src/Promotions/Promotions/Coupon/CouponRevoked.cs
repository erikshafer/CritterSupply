namespace Promotions.Coupon;

/// <summary>
/// Domain event: A coupon was revoked by an admin (e.g., fraud, error).
/// Phase 1: Admin action only.
/// </summary>
public sealed record CouponRevoked(
    Guid CouponId,
    string CouponCode,
    Guid PromotionId,
    string Reason,
    DateTimeOffset RevokedAt);
