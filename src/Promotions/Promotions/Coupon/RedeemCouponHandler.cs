using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Promotions.Coupon;

public static class RedeemCouponHandler
{
    /// <summary>
    /// Loads the coupon aggregate by deterministic UUID v5 stream ID from the coupon code.
    /// Wolverine cannot compute this hash from the command's CouponCode property,
    /// so [WriteAggregate] cannot be used — manual Load/Before/Handle is required.
    /// </summary>
    public static async Task<Coupon?> LoadAsync(
        RedeemCoupon cmd,
        IQuerySession session,
        CancellationToken ct)
    {
        var streamId = Coupon.StreamId(cmd.CouponCode);
        return await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);
    }

    public static ProblemDetails Before(RedeemCoupon cmd, Coupon? coupon)
    {
        if (coupon is null)
            return new ProblemDetails { Detail = $"Coupon with code '{cmd.CouponCode}' not found", Status = 404 };
        if (coupon.Status != CouponStatus.Issued)
            return new ProblemDetails { Detail = $"Cannot redeem coupon '{coupon.Code}' — current status is {coupon.Status}. Only Issued coupons can be redeemed.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Redeems a coupon for an order.
    /// Uses optimistic concurrency (Marten) to prevent double-redemption.
    /// void return — no OutgoingMessages; the integration message is published
    /// by the handler that invokes this command.
    /// </summary>
    public static void Handle(
        RedeemCoupon cmd,
        Coupon coupon,
        IDocumentSession session)
    {
        var evt = new CouponRedeemed(
            coupon.Id,
            coupon.Code,
            coupon.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);

        session.Events.Append(Coupon.StreamId(cmd.CouponCode), evt);
    }
}
