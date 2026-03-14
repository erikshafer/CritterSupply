namespace Promotions;

/// <summary>
/// Lifecycle states for a coupon.
/// Phase 1: Issued → Redeemed/Revoked/Expired
/// </summary>
public enum CouponStatus
{
    Issued = 0,    // Created and ready for use
    Redeemed = 1,  // Used in an order (terminal for single-use)
    Revoked = 2,   // Admin cancelled (fraud, error)
    Expired = 3    // Parent promotion expired
}
