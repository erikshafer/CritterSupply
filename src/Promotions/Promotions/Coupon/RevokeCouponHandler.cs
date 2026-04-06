using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Promotions.Coupon;

public static class RevokeCouponHandler
{
    /// <summary>
    /// Loads the coupon aggregate by deterministic UUID v5 stream ID from the coupon code.
    /// Same Load() pattern as RedeemCouponHandler — Wolverine cannot compute the hash.
    /// </summary>
    public static async Task<Coupon?> LoadAsync(
        RevokeCoupon cmd,
        IQuerySession session,
        CancellationToken ct)
    {
        var streamId = Coupon.StreamId(cmd.CouponCode);
        return await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);
    }

    public static ProblemDetails Before(RevokeCoupon cmd, Coupon? coupon)
    {
        if (coupon is null)
            return new ProblemDetails { Detail = $"Coupon with code '{cmd.CouponCode}' not found", Status = 404 };
        if (coupon.Status == CouponStatus.Revoked)
            return new ProblemDetails { Detail = $"Cannot revoke coupon '{coupon.Code}' — it is already revoked.", Status = 409 };
        if (coupon.Status == CouponStatus.Expired)
            return new ProblemDetails { Detail = $"Cannot revoke coupon '{coupon.Code}' — it is already expired.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Revokes a coupon (admin action).
    /// Can revoke coupons in Issued or Redeemed status.
    /// void return — no OutgoingMessages.
    /// </summary>
    public static void Handle(
        RevokeCoupon cmd,
        Coupon coupon,
        IDocumentSession session)
    {
        var evt = new CouponRevoked(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.Reason,
            DateTimeOffset.UtcNow);

        // Tag the event for DCB tag table population
        var wrapped = session.Events.BuildEvent(evt);
        wrapped.AddTag(new CouponStreamId(Coupon.StreamId(cmd.CouponCode)));
        session.Events.Append(Coupon.StreamId(cmd.CouponCode), wrapped);
    }
}
