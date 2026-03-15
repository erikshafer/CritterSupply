using Messages.Contracts.Orders;
using Wolverine;

namespace Promotions.OrderIntegration;

/// <summary>
/// Handles OrderPlaced integration message from Orders BC.
/// Phase 1 (M30.0): Skeleton implementation — no coupon data in OrderPlaced yet.
/// Phase 2 (M30.1): After Shopping BC integration, OrderPlaced will include AppliedCoupons.
///
/// Pattern: Handler cascading commands (not a saga).
/// Fans out to RedeemCoupon + RecordRedemption commands using OutgoingMessages.
/// Each command targets a separate aggregate (Coupon, Promotion) with optimistic concurrency.
/// </summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Phase 1: No-op handler — OrderPlaced doesn't contain coupon data yet.
    /// Phase 2: Will fan out to RedeemCoupon and RecordPromotionRedemption.
    ///
    /// Future implementation (Phase 2):
    /// <code>
    /// foreach (var appliedPromo in message.AppliedCoupons)
    /// {
    ///     messages.Add(new RedeemCoupon(
    ///         appliedPromo.CouponCode,
    ///         message.OrderId,
    ///         message.CustomerId,
    ///         message.PlacedAt));
    ///
    ///     messages.Add(new RecordPromotionRedemption(
    ///         appliedPromo.PromotionId,
    ///         message.OrderId,
    ///         message.CustomerId,
    ///         appliedPromo.CouponCode,
    ///         message.PlacedAt));
    /// }
    /// </code>
    /// </summary>
    public static OutgoingMessages Handle(OrderPlaced message)
    {
        // Phase 1: No coupon data in OrderPlaced yet — return empty
        // Phase 2: After Shopping BC integration, this will fan out redemption commands
        return new OutgoingMessages();
    }
}
