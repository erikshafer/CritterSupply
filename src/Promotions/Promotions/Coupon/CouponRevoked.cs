namespace Promotions.Coupon;

/// <summary>
/// Domain event: A coupon was revoked by an admin (e.g., fraud, error).
/// </summary>
public sealed record CouponRevoked(
    string CouponCode,
    DateTimeOffset RevokedAt);
