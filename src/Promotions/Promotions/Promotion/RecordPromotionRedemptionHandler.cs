using Promotions.Coupon;
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
