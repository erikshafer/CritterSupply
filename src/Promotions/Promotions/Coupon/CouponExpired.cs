namespace Promotions.Coupon;

/// <summary>
/// Domain event: A coupon expired because its parent promotion expired.
/// </summary>
public sealed record CouponExpired(
    string CouponCode,
    DateTimeOffset ExpiredAt);
