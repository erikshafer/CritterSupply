namespace Promotions.Coupon;

/// <summary>
/// Command to revoke a coupon (admin action).
/// Used for fraud prevention or manual correction.
/// </summary>
public sealed record RevokeCoupon(
    string CouponCode,
    string Reason);
