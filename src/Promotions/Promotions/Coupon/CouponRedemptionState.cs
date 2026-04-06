using Promotions.Promotion;

namespace Promotions.Coupon;

/// <summary>
/// DCB boundary state spanning both Coupon and Promotion aggregates.
/// Used by RedeemCouponHandler to enforce coupon validity + promotion status + redemption cap
/// as a single atomic consistency decision.
///
/// Projected from existing Coupon and Promotion aggregates via ProjectFromCoupon/ProjectFromPromotion
/// helper methods. The LoadAsync method in RedeemCouponHandler loads both aggregates and
/// populates this state before Before() validation.
///
/// M40.0: CritterSupply's first DCB boundary state implementation.
/// </summary>
public class CouponRedemptionState
{
    // --- Coupon state ---

    /// <summary>Whether the coupon exists.</summary>
    public bool CouponExists { get; private set; }

    /// <summary>Current status of the coupon.</summary>
    public CouponStatus CouponStatus { get; private set; }

    /// <summary>The coupon's stream ID (UUID v5 from code).</summary>
    public Guid CouponId { get; private set; }

    /// <summary>The coupon code.</summary>
    public string CouponCode { get; private set; } = string.Empty;

    /// <summary>The promotion ID associated with the coupon.</summary>
    public Guid CouponPromotionId { get; private set; }

    // --- Promotion state ---

    /// <summary>Whether the promotion exists.</summary>
    public bool PromotionExists { get; private set; }

    /// <summary>Current status of the promotion.</summary>
    public PromotionStatus PromotionStatus { get; private set; }

    /// <summary>Promotion usage limit (null = unlimited).</summary>
    public int? UsageLimit { get; private set; }

    /// <summary>Current redemption count for the promotion.</summary>
    public int CurrentRedemptionCount { get; private set; }

    /// <summary>
    /// Projects state from a loaded Coupon aggregate.
    /// </summary>
    public void ProjectFromCoupon(Coupon coupon)
    {
        CouponExists = true;
        CouponStatus = coupon.Status;
        CouponId = coupon.Id;
        CouponCode = coupon.Code;
        CouponPromotionId = coupon.PromotionId;
    }

    /// <summary>
    /// Projects state from a loaded Promotion aggregate.
    /// </summary>
    public void ProjectFromPromotion(Promotions.Promotion.Promotion promotion)
    {
        PromotionExists = true;
        PromotionStatus = promotion.Status;
        UsageLimit = promotion.UsageLimit;
        CurrentRedemptionCount = promotion.CurrentRedemptionCount;
    }
}
