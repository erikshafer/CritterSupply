using Promotions.Promotion;

namespace Promotions.Coupon;

/// <summary>
/// DCB boundary state projected from events spanning both Coupon and Promotion streams.
/// Used by RedeemCouponHandler to enforce coupon validity + promotion status + redemption cap
/// as a single atomic consistency decision.
///
/// Apply() methods mutate state in-place (class, not record) because the boundary model
/// aggregates events from multiple streams and Marten projects them sequentially.
///
/// M40.0: CritterSupply's first DCB boundary state implementation.
/// </summary>
public class CouponRedemptionState
{
    // --- Coupon state (projected from Coupon stream events) ---

    /// <summary>Whether a CouponIssued event was seen (coupon exists).</summary>
    public bool CouponExists { get; private set; }

    /// <summary>Current status of the coupon.</summary>
    public CouponStatus CouponStatus { get; private set; }

    /// <summary>The coupon's stream ID (UUID v5 from code).</summary>
    public Guid CouponId { get; private set; }

    /// <summary>The coupon code.</summary>
    public string CouponCode { get; private set; } = string.Empty;

    /// <summary>The promotion ID associated with the coupon.</summary>
    public Guid CouponPromotionId { get; private set; }

    // --- Promotion state (projected from Promotion stream events) ---

    /// <summary>Whether a PromotionCreated event was seen (promotion exists).</summary>
    public bool PromotionExists { get; private set; }

    /// <summary>Current status of the promotion.</summary>
    public PromotionStatus PromotionStatus { get; private set; }

    /// <summary>Promotion usage limit (null = unlimited).</summary>
    public int? UsageLimit { get; private set; }

    /// <summary>Current redemption count for the promotion.</summary>
    public int CurrentRedemptionCount { get; private set; }

    // --- Coupon stream Apply methods ---

    public void Apply(CouponIssued e)
    {
        CouponExists = true;
        CouponStatus = CouponStatus.Issued;
        CouponId = Coupon.StreamId(e.CouponCode);
        CouponCode = e.CouponCode;
        CouponPromotionId = e.PromotionId;
    }

    public void Apply(CouponRedeemed e)
    {
        CouponStatus = CouponStatus.Redeemed;
    }

    public void Apply(CouponRevoked e)
    {
        CouponStatus = CouponStatus.Revoked;
    }

    public void Apply(CouponExpired e)
    {
        CouponStatus = CouponStatus.Expired;
    }

    // --- Promotion stream Apply methods ---

    public void Apply(PromotionCreated e)
    {
        PromotionExists = true;
        PromotionStatus = PromotionStatus.Draft;
        UsageLimit = e.UsageLimit;
    }

    public void Apply(PromotionActivated e)
    {
        PromotionStatus = PromotionStatus.Active;
    }

    public void Apply(PromotionPaused e)
    {
        PromotionStatus = PromotionStatus.Paused;
    }

    public void Apply(PromotionResumed e)
    {
        PromotionStatus = PromotionStatus.Active;
    }

    public void Apply(PromotionCancelled e)
    {
        PromotionStatus = PromotionStatus.Cancelled;
    }

    public void Apply(PromotionExpired e)
    {
        PromotionStatus = PromotionStatus.Expired;
    }

    public void Apply(PromotionRedemptionRecorded e)
    {
        CurrentRedemptionCount++;
    }
}
