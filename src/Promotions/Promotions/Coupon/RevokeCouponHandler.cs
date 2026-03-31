using Marten;

namespace Promotions.Coupon;

public static class RevokeCouponHandler
{
    /// <summary>
    /// Revokes a coupon (admin action).
    /// Can revoke coupons in Issued or Redeemed status.
    /// Cannot revoke already-revoked or expired coupons.
    /// </summary>
    public static async Task Handle(
        RevokeCoupon cmd,
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

        // Create domain event
        var evt = new CouponRevoked(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.Reason,
            DateTimeOffset.UtcNow);

        // Manually append event to stream (optimistic concurrency via Marten)
        session.Events.Append(streamId, evt);
    }
}
