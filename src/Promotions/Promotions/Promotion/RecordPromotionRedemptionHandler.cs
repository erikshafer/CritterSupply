using Marten;
using Promotions.Coupon;

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
/// M40.0 S1B: Uses FetchForWriting + tagged append for DCB tag table population.
/// </summary>
public static class RecordPromotionRedemptionHandler
{
    /// <summary>
    /// Choreography: reacts to CouponRedeemed events emitted by the DCB RedeemCouponHandler.
    /// Uses FetchForWriting for optimistic concurrency + tags the event for DCB.
    /// </summary>
    public static async Task Handle(
        CouponRedeemed evt,
        IDocumentSession session,
        CancellationToken ct)
    {
        await session.Events.FetchForWriting<Promotion>(evt.PromotionId, ct);

        var recorded = new PromotionRedemptionRecorded(
            evt.PromotionId,
            evt.OrderId,
            evt.CustomerId,
            evt.CouponCode,
            evt.RedeemedAt);

        var wrapped = session.Events.BuildEvent(recorded);
        wrapped.AddTag(new PromotionStreamId(evt.PromotionId));
        session.Events.Append(evt.PromotionId, wrapped);
    }
}
