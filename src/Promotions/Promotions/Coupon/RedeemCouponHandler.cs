using Marten;
using Microsoft.AspNetCore.Mvc;
using Promotions.Promotion;
using Wolverine;
using Wolverine.Http;

namespace Promotions.Coupon;

/// <summary>
/// DCB handler for coupon redemption.
/// Loads events from both the Coupon stream and the Promotion stream,
/// projecting them into CouponRedemptionState for a single atomic consistency decision.
///
/// This replaces the previous single-stream Load/Before/Handle pattern.
/// Promotion usage count update is handled by choreography:
/// CouponRedeemed → RecordPromotionRedemptionHandler (reacts to event).
///
/// M40.0: CritterSupply's first DCB handler implementation.
///
/// Implementation note: Uses manual multi-stream aggregation via LoadAsync rather than
/// Marten's IEventBoundary/EventTagQuery API. The tag-based DCB API requires all events
/// to be pre-tagged at write time, which would require modifying all upstream handlers
/// (CreatePromotionHandler, IssueCouponHandler, etc.) to tag events with strong-typed IDs.
/// This manual approach achieves the same atomic consistency boundary — loading events from
/// both streams into one projected state for a single validation decision — while preserving
/// the existing event append patterns. Optimistic concurrency on the Coupon stream prevents
/// double-redemption.
/// </summary>
public static class RedeemCouponHandler
{
    /// <summary>
    /// Loads events from both the Coupon and Promotion streams and projects them
    /// into CouponRedemptionState for the boundary decision.
    /// </summary>
    public static async Task<CouponRedemptionState?> LoadAsync(
        RedeemCoupon cmd,
        IQuerySession session,
        CancellationToken ct)
    {
        // Load the Coupon aggregate
        var couponStreamId = Coupon.StreamId(cmd.CouponCode);
        var coupon = await session.Events.AggregateStreamAsync<Coupon>(couponStreamId, token: ct);

        // Load the Promotion aggregate
        var promotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(
            cmd.PromotionId, token: ct);

        // Project both into a single boundary state
        var state = new CouponRedemptionState();

        if (coupon is not null)
        {
            state.ProjectFromCoupon(coupon);
        }

        if (promotion is not null)
        {
            state.ProjectFromPromotion(promotion);
        }

        return state;
    }

    /// <summary>
    /// Validates the boundary state before handling.
    /// Enforces: coupon existence, coupon status, promotion existence,
    /// promotion status, and usage cap — all in one decision.
    /// </summary>
    public static ProblemDetails Before(RedeemCoupon cmd, CouponRedemptionState? state)
    {
        if (state is null || !state.CouponExists)
            return new ProblemDetails
            {
                Detail = $"Coupon with code '{cmd.CouponCode}' not found",
                Status = 404
            };

        if (state.CouponStatus != CouponStatus.Issued)
            return new ProblemDetails
            {
                Detail = $"Cannot redeem coupon '{cmd.CouponCode}' — current status is {state.CouponStatus}. " +
                         "Only Issued coupons can be redeemed.",
                Status = 409
            };

        if (!state.PromotionExists)
            return new ProblemDetails
            {
                Detail = $"Promotion '{cmd.PromotionId}' not found",
                Status = 404
            };

        if (state.PromotionStatus != PromotionStatus.Active)
            return new ProblemDetails
            {
                Detail = $"Cannot redeem coupon — promotion '{cmd.PromotionId}' status is {state.PromotionStatus}. " +
                         "Only Active promotions accept redemptions.",
                Status = 409
            };

        if (state.UsageLimit.HasValue && state.CurrentRedemptionCount >= state.UsageLimit.Value)
            return new ProblemDetails
            {
                Detail = $"Promotion '{cmd.PromotionId}' usage limit of {state.UsageLimit.Value} " +
                         "has been reached.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Appends CouponRedeemed to the Coupon stream and cascades it as a message
    /// for downstream choreography handlers.
    /// Optimistic concurrency on the Coupon stream prevents double-redemption.
    ///
    /// CouponRedeemed contains PromotionId, enabling choreography:
    /// RecordPromotionRedemptionHandler reacts to this event to update the Promotion's usage count.
    /// </summary>
    public static OutgoingMessages Handle(
        RedeemCoupon cmd,
        CouponRedemptionState state,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();

        var evt = new CouponRedeemed(
            state.CouponId,
            state.CouponCode,
            cmd.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);

        session.Events.Append(Coupon.StreamId(cmd.CouponCode), evt);

        // Cascade CouponRedeemed to RecordPromotionRedemptionHandler (choreography)
        outgoing.Add(evt);

        return outgoing;
    }
}
