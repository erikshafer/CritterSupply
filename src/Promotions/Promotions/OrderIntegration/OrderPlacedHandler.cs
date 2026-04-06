using Messages.Contracts.Orders;
using Wolverine;

namespace Promotions.OrderIntegration;

/// <summary>
/// Handles OrderPlaced integration message from Orders BC.
/// Phase 1 (M30.0): Skeleton implementation — no coupon data in OrderPlaced yet.
/// Phase 2 (M30.1): After Shopping BC integration, OrderPlaced will include AppliedCoupons.
///
/// Pattern: Handler cascading commands (not a saga).
/// M40.0 (DCB): Fans out a single RedeemCoupon command per applied coupon.
/// RecordPromotionRedemption is no longer needed — the DCB RedeemCouponHandler
/// emits CouponRedeemed, which RecordPromotionRedemptionHandler reacts to via choreography.
/// </summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Phase 1: No-op handler — OrderPlaced doesn't contain coupon data yet.
    ///
    /// Future Phase 2 implementation:
    /// <code>
    /// foreach (var appliedPromo in message.AppliedCoupons)
    /// {
    ///     outgoing.Add(new RedeemCoupon(
    ///         appliedPromo.CouponCode,
    ///         appliedPromo.PromotionId,   // required for DCB boundary query
    ///         message.OrderId,
    ///         message.CustomerId,
    ///         message.PlacedAt));
    /// }
    /// // No separate RecordPromotionRedemption needed — RedeemCouponHandler (DCB)
    /// // emits CouponRedeemed, which RecordPromotionRedemptionHandler reacts to via choreography.
    /// </code>
    /// </summary>
    public static OutgoingMessages Handle(OrderPlaced message)
    {
        // Phase 1: No coupon data in OrderPlaced yet — return empty
        // Phase 2: After Shopping BC integration, this will fan out RedeemCoupon commands
        return new OutgoingMessages();
    }
}
