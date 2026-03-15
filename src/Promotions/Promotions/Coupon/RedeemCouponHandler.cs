using Marten;
using Wolverine;
using Wolverine.Marten;

namespace Promotions.Coupon;

public static class RedeemCouponHandler
{
    /// <summary>
    /// Redeems a coupon for an order.
    /// Enforces single-use constraint: coupon must be in Issued status.
    /// Uses optimistic concurrency (Marten) to prevent double-redemption.
    /// </summary>
    public static CouponRedeemed Handle(
        RedeemCoupon cmd,
        [WriteAggregate] Coupon coupon)
    {
        // Invariant: Coupon must be in Issued status to redeem
        if (coupon.Status != CouponStatus.Issued)
        {
            throw new InvalidOperationException(
                $"Cannot redeem coupon {coupon.Code} — current status is {coupon.Status}. " +
                "Only coupons in Issued status can be redeemed.");
        }

        // Return domain event — Wolverine + Marten handle persistence
        return new CouponRedeemed(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);
    }

    /// <summary>
    /// Load method: Resolves coupon by deterministic UUID v5 from code.
    /// Throws if coupon doesn't exist.
    /// </summary>
    public static Task<Coupon?> Load(RedeemCoupon cmd, IDocumentSession session)
    {
        var streamId = Coupon.StreamId(cmd.CouponCode);
        return session.Events.AggregateStreamAsync<Coupon>(streamId);
    }
}
