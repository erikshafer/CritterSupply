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
    public static async Task<(Coupon, CouponRedeemed)> Handle(
        RedeemCoupon cmd,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load coupon by deterministic UUID v5 from code
        var streamId = Coupon.StreamId(cmd.CouponCode);
        var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

        if (coupon is null)
        {
            throw new InvalidOperationException(
                $"Coupon with code '{cmd.CouponCode}' not found");
        }

        // Invariant: Coupon must be in Issued status to redeem
        if (coupon.Status != CouponStatus.Issued)
        {
            throw new InvalidOperationException(
                $"Cannot redeem coupon {coupon.Code} — current status is {coupon.Status}. " +
                "Only coupons in Issued status can be redeemed.");
        }

        // Create domain event
        var evt = new CouponRedeemed(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);

        // Return tuple: updated aggregate + event
        return (coupon.Apply(evt), evt);
    }
}
