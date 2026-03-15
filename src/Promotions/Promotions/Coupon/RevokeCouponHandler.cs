using Marten;
using Wolverine;
using Wolverine.Marten;

namespace Promotions.Coupon;

public static class RevokeCouponHandler
{
    /// <summary>
    /// Revokes a coupon (admin action).
    /// Can revoke coupons in Issued or Redeemed status.
    /// Cannot revoke already-revoked or expired coupons.
    /// </summary>
    public static CouponRevoked Handle(
        RevokeCoupon cmd,
        [WriteAggregate] Coupon coupon)
    {
        // Invariant: Cannot revoke an already-revoked or expired coupon
        if (coupon.Status == CouponStatus.Revoked)
        {
            throw new InvalidOperationException(
                $"Cannot revoke coupon {coupon.Code} — it is already revoked.");
        }

        if (coupon.Status == CouponStatus.Expired)
        {
            throw new InvalidOperationException(
                $"Cannot revoke coupon {coupon.Code} — it is already expired.");
        }

        // Return domain event — Wolverine + Marten handle persistence
        return new CouponRevoked(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.Reason,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Load method: Resolves coupon by deterministic UUID v5 from code.
    /// Throws if coupon doesn't exist.
    /// </summary>
    public static Task<Coupon?> Load(RevokeCoupon cmd, IDocumentSession session)
    {
        var streamId = Coupon.StreamId(cmd.CouponCode);
        return session.Events.AggregateStreamAsync<Coupon>(streamId);
    }
}
