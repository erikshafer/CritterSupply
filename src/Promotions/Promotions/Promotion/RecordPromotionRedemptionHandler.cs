using Microsoft.AspNetCore.Mvc;
using Promotions.Coupon;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Promotions.Promotion;

/// <summary>
/// Choreography handler: reacts to CouponRedeemed events to record the redemption
/// on the Promotion aggregate. This was converted from a command handler
/// (RecordPromotionRedemption) to an event handler (CouponRedeemed) in M40.0
/// as part of the DCB pattern implementation.
///
/// The DCB RedeemCouponHandler has already enforced all invariants (promotion active,
/// cap not exceeded) before CouponRedeemed was emitted. This handler is a consequence
/// that updates the count as a result of the committed fact.
///
/// [WriteAggregate] resolves the Promotion aggregate by convention from
/// CouponRedeemed.PromotionId (matches {AggregateName}Id).
///
/// Concurrency Strategy:
/// Uses Marten's optimistic concurrency via [WriteAggregate].
/// The DCB handler prevents the root cause of double-redemption.
/// This handler only records the count update as a downstream consequence.
/// </summary>
public static class RecordPromotionRedemptionHandler
{
    /// <summary>
    /// Choreography: reacts to CouponRedeemed events emitted by the DCB RedeemCouponHandler.
    /// M40.0: Primary path for recording promotion redemptions.
    /// </summary>
    public static Events Handle(
        CouponRedeemed evt,
        [WriteAggregate] Promotion promotion)
    {
        var events = new Events();
        events.Add(new PromotionRedemptionRecorded(
            promotion.Id,
            evt.OrderId,
            evt.CustomerId,
            evt.CouponCode,
            evt.RedeemedAt));
        return events;
    }
}

/// <summary>
/// Legacy command handler for RecordPromotionRedemption.
/// SUPERSEDED (M40.0): The DCB RedeemCouponHandler now emits CouponRedeemed,
/// and RecordPromotionRedemptionHandler reacts to that event via choreography.
/// This handler is retained for backward compatibility with existing command invocations.
/// </summary>
public static class LegacyRecordPromotionRedemptionHandler
{
    public static ProblemDetails Before(RecordPromotionRedemption cmd, Promotion? promotion)
    {
        if (promotion is null)
            return new ProblemDetails { Detail = $"Promotion '{cmd.PromotionId}' not found", Status = 404 };
        if (promotion.Status != PromotionStatus.Active)
            return new ProblemDetails { Detail = $"Cannot record redemption for promotion '{promotion.Id}' — status is {promotion.Status}. Only Active promotions accept redemptions.", Status = 409 };
        if (promotion.UsageLimit.HasValue && promotion.CurrentRedemptionCount >= promotion.UsageLimit.Value)
            return new ProblemDetails { Detail = $"Promotion '{promotion.Id}' usage limit of {promotion.UsageLimit.Value} has been reached.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        RecordPromotionRedemption cmd,
        [WriteAggregate] Promotion promotion)
    {
        var events = new Events();
        events.Add(new PromotionRedemptionRecorded(
            promotion.Id,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.CouponCode,
            cmd.RedeemedAt));
        return events;
    }
}
