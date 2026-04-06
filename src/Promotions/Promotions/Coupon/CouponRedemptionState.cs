using Promotions.Promotion;

namespace Promotions.Coupon;

/// <summary>
/// DCB boundary state spanning Coupon and Promotion event streams.
/// Marten projects this via Apply() methods from all events matching the EventTagQuery.
/// Used by RedeemCouponHandler to enforce all redemption invariants in one atomic decision.
///
/// M40.0 S1B: Rewritten from ProjectFromCoupon/ProjectFromPromotion helpers to standard
/// Apply() methods, enabling real EventTagQuery + [BoundaryModel] + IEventBoundary&lt;T&gt;.
/// </summary>
public sealed class CouponRedemptionState
{
    // --- Coupon state (projected from Coupon stream events) ---

    /// <summary>Whether the coupon exists.</summary>
    public bool CouponExists { get; private set; }

    /// <summary>The coupon's stream ID (UUID v5 from code).</summary>
    public Guid CouponId { get; private set; }

    /// <summary>The coupon code.</summary>
    public string CouponCode { get; private set; } = string.Empty;

    /// <summary>Current status of the coupon.</summary>
    public CouponStatus CouponStatus { get; private set; }

    // --- Promotion state (projected from Promotion stream events) ---

    /// <summary>Whether the promotion exists.</summary>
    public bool PromotionExists { get; private set; }

    /// <summary>Current status of the promotion.</summary>
    public PromotionStatus PromotionStatus { get; private set; }

    /// <summary>Promotion usage limit (null = unlimited).</summary>
    public int? UsageLimit { get; private set; }

    /// <summary>Current redemption count for the promotion.</summary>
    public int CurrentRedemptionCount { get; private set; }

    // --- Coupon stream event Apply methods ---

    public void Apply(CouponIssued e)
    {
        CouponExists = true;
        CouponId = Coupon.StreamId(e.CouponCode);
        CouponCode = e.CouponCode;
        CouponStatus = CouponStatus.Issued;
    }

    public void Apply(CouponRedeemed e) => CouponStatus = CouponStatus.Redeemed;
    public void Apply(CouponRevoked e) => CouponStatus = CouponStatus.Revoked;
    public void Apply(CouponExpired e) => CouponStatus = CouponStatus.Expired;

    // --- Promotion stream event Apply methods ---

    public void Apply(PromotionCreated e)
    {
        PromotionExists = true;
        PromotionStatus = PromotionStatus.Draft;
        UsageLimit = e.UsageLimit;
    }

    public void Apply(PromotionActivated e) => PromotionStatus = PromotionStatus.Active;
    public void Apply(PromotionPaused e) => PromotionStatus = PromotionStatus.Paused;
    public void Apply(PromotionResumed e) => PromotionStatus = PromotionStatus.Active;
    public void Apply(PromotionCancelled e) => PromotionStatus = PromotionStatus.Cancelled;
    public void Apply(PromotionExpired e) => PromotionStatus = PromotionStatus.Expired;
    public void Apply(PromotionRedemptionRecorded e) => CurrentRedemptionCount++;
}
